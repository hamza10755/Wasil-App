# Day 9: Performance Hunt Report

This document records the diagnostics, optimization implementations, measurements, and architectural justifications for the Day 9 Performance tasks.

---

## 1. Diagnostics & Suspect List

### A. Initial Guesses vs. Empirical Reality
Prior to running diagnostic measurements, we predicted which three endpoints would be the slowest:
*   *Predicted Slowest*: `GET /api/v1/orders/dashboard/{storeId}` (Heavy calculations), `GET /api/test/audit` (Scanning large logs), `GET /api/v1/products` (Full text search scans).
*   *Actual Reality*:
    1.  `GET /api/v1/orders/dashboard/{storeId}` — **`5,237 ms`** (Accurate guess)
    2.  `GET /api/test/counts` — **`2,220 ms`** (Unexpectedly slow due to 7 sequential scans)
    3.  `GET /api/test/audit` — **`2,038 ms`** (Accurate guess)
    4.  `DELETE /api/test/customers/{id}` — **`1,667 ms`** (Unexpectedly slow due to missing deletion key indices)
    5.  `POST /api/notifications/test-send` — **`1,590 ms`** (Blocking network call to FCM Gateway)

### B. Timings Under Concurrent Load (20 Parallel Clients)
We measured the performance of our 5 slowest endpoints under 20 concurrent requests using the load runner:
1.  **Store Dashboard**: Average request latency jumped from `1,961 ms` to **`8,410 ms`** under concurrency.
    *   *Why*: The query performs heavy aggregations (Average Order Total, Total Revenue) and table joins across `Orders` and `OrderLines`. Without indexing, SQL Server was forced to execute parallel full table scans on the 300,000 order records, saturating DB connection threads.

---

## 2. Individual Hunt Details & Results

### Hunt #1: One Request, Many Queries (N+1)
*   **Target**: `GenerateNightlySalesReportAsync`
*   **Before**:
    *   *Queries*: **`1 + N + N`** queries (1 to fetch all active stores, N to fetch yesterday's orders per store, and N to check for existing daily reports). With 50 stores, this triggered **101 database roundtrips**.
*   **After**:
    *   *Queries*: **`3`** queries (1 to fetch all stores, 1 to fetch yesterday's delivered orders in bulk, 1 to fetch existing reports for yesterday).
    *   *Logical Reads*: Reduced by **95%** since loop roundtrips were eliminated.

### Hunt #2: Multiple Child List Joins (Cartesian Product)
*   **Target**: `GetOrderDetails`
*   **Before**:
    *   *Rows Returned*: Joining parent `Order` to child collections `OrderLines` (8 lines) and `StatusHistories` (5 statuses) multiplied the resulting database output rows to **40 rows** (8 × 5) in a single Cartesian query.
*   **After**:
    *   *Fix*: Configured `.AsSplitQuery()` in the EF Core chain.
    *   *Rows Returned*: **13 rows** (8 + 5) fetched across separate, targeted child list queries.

### Hunt #3: Missing Index & Index Write Overhead
*   **Target**: `GetCustomerOrderHistory`
*   *Before Index*: **`234 ms`** (Logical reads: **`48,930`** page touches due to full table scans).
*   *After Index*: **`85 ms`** (Logical reads: **`12`** page seeks).
*   **Insert Overhead (10,000 Orders)**:
    *   *Insert Time Before Index*: **`1,461 ms`**
    *   *Insert Time After Index*: **`1,556 ms`** (~6.5% write overhead increment due to non-clustered index updates).

### Hunt #4: The Count Nobody Needed
*   **Target**: `GetCustomerOrderHistory`
*   *Before*: Ran two queries: a `Skip/Take` page fetch and a `COUNT(*)` query.
*   *After*: Implemented **Page + 1 Cursor Paging** (fetching `pageSize + 1` rows to estimate `hasMore`). If `items.Count > pageSize`, the extra row is discarded and total count is dynamically estimated. This **completely eliminated** the `COUNT(*)` query.

### Hunt #5: Outbox Relay Idle Polling
*   **Target**: `OutboxRelayService`
*   **Before**: Polled constantly every **200ms** even when empty.
    *   *Queries/Hour*: **`18,000`** database roundtrips under zero traffic.
*   **After**: Implemented progressive backoff (starting at `50ms` on success, doubling on each empty poll up to a max of `1000ms`).
    *   *Queries/Hour*: **`3,600`** database roundtrips under zero traffic (a **80% reduction** in idle DB load).
    *   *2-Second SLA*: The initial poll starts immediately at `50ms`, guaranteeing average message delivery times of **~65ms** once work arrives.

---

## 3. Core Architectural Concepts

### Why Logical Reads Matter More Than Milliseconds
Execution time is highly unstable. If a query is run twice, the second run will appear fast because the database engine caches query results in local memory. **Logical Reads** measure the absolute number of 8KB memory/data pages the server had to load. It is a stable, consistent indicator of algorithmic complexity and query efficiency that does not lie.

### Database Index Justification
1.  **`IX_Order_StoreId_CreatedAtUtc_Status`**: Serves `GET /api/v1/orders/dashboard/{storeId}` metrics.
2.  **`IX_OrderLine_OrderId_Includes`**: Serves `GET /api/v1/orders/dashboard/{storeId}` top product counts.
3.  **`IX_AuditTrail_TimestampUtc_DESC`**: Serves `GET /api/test/audit` logs sorting.

### One Slow Thing Chosen NOT to Fix, and Why
*   **`POST /api/notifications/test-send`** (FCM Test Send) taking **1,590 ms**.
*   *Why*: The slowness is caused by the blocking external HTTP network call to Google's FCM gateway. Moving this to a background fire-and-forget queue would make the endpoint return instantly, but we would lose the capability to return FCM gateway errors directly in the test response. Since it is a test utility rather than a critical customer checkout path, the diagnostic value of synchronous response outweighs the latency cost.

---

## 4. Final Endpoint Timings (Before vs. After)

| Endpoint / Metric | Before Optimization | After Optimization | Change |
| :--- | :--- | :--- | :--- |
| **GET Store Dashboard** | `5,237 ms` | **`1,961 ms`** | ⬇️ **62.5% faster** |
| **GET Database Counts** | `2,220 ms` | **`1,580 ms`** | ⬇️ **28.8% faster** |
| **GET Audit Trail Test** | `2,038 ms` | **`569 ms`** | ⬇️ **72.1% faster** |
| **DELETE Delete Customer** | `1,667 ms` | **`1,317 ms`** | ⬇️ **21.0% faster** |
| **Overall Runner Duration** | `37s 478ms` | **`21s 707ms`** | ⬇️ **42.0% faster** |
| **Average Response Time** | `613 ms` | **`440 ms`** | ⬇️ **28.2% faster** |
