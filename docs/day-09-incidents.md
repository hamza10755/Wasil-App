# Day 9 Performance Incident Log

This document records the detailed incident reports for each of the performance hunts conducted on Day 9.

---

## Incident #1: Hunt 1 — Sales Report N+1 Loops

### 1. What was measured
*   **Endpoint/Method**: `GenerateNightlySalesReportAsync` background job.
*   **Starting Numbers**: 101 queries executed to generate reports for 50 stores (taking ~1,100ms total).

### 2. What was expected
*   A database operation fetching store reports for a single day should execute in a constant number of database queries (e.g. 2–4 queries) regardless of the number of stores.

### 3. What was found
*   **SQL queries executed**: 1 query to get all stores, then 50 queries to get orders per store, then 50 queries to check if a report exists for each store.
*   **Queries run**: `1 + 2N` queries (101 roundtrips for 50 stores).

### 4. Why it was slow
*   The method was executing database calls (`_dbContext.Orders.Where(...)` and `_dbContext.DailyReports.FirstOrDefaultAsync(...)`) inside a `foreach (var store in stores)` loop. This forced sequential database roundtrips for each iteration, causing latency scaling linearly with the number of stores.

### 5. What was changed
*   Modified the method to query all yesterday's delivered orders and all yesterday's daily reports in bulk using single LINQ requests outside the loop, storing results in-memory using `ToDictionary`.
*   *Cost*: Slightly higher memory allocation on the server thread to hold the dictionaries of orders and reports.

### 6. The numbers after
*   **Queries executed**: **3 queries** total (97% reduction in DB roundtrips).
*   **Total duration**: ~45ms (over 20x faster).

---

## Incident #2: Hunt 2 — Cartesian Join in Order Details

### 1. What was measured
*   **Endpoint/Method**: `GET /api/v1/orders/{orderId}` (`GetOrderDetails`).
*   **Starting Numbers**: `403 ms` (Logical reads: `168`).

### 2. What was expected
*   An order with 8 lines and 5 status records should fetch and map exactly 13 child rows.

### 3. What was found
*   **Rows returned**: The SQL join query returned **40 rows** for a single order containing 8 lines and 5 statuses.

### 4. Why it was slow
*   EF Core eagerly loaded both child collections (`OrderLines` and `StatusHistories`) using `.Include()` in a single SQL query. This forced SQL Server to perform an identity Cartesian join, repeating order line data for every status history record.

### 5. What was changed
*   Configured `.AsSplitQuery()` in the EF query.
*   *Cost*: The server makes multiple smaller SQL queries instead of one single query, requiring slightly more database connection overhead.

### 6. The numbers after
*   **Rows returned**: **13 rows** mapped (8 lines + 5 statuses) across split queries.
*   **Latency**: `125 ms` (Logical reads: `22`).

---

## Incident #3: Hunt 3 — Missing Index in Customer History

### 1. What was measured
*   **Endpoint/Method**: `GET /api/v1/orders/customer/{customerId}` (`GetCustomerOrderHistory`).
*   **Starting Numbers**: `234 ms` (Logical reads: **`48,930`** page touches).

### 2. What was expected
*   Retrieving 20 orders for a customer out of 300,000 records should seek directly to the customer's entries, requiring less than 50 logical page reads.

### 3. What was found
*   The query plan executed a **Clustered Index Scan** across the entire `Order` table.

### 4. Why it was slow
*   The database had no index on the `CustomerId` or `CreatedAtUtc` column. The database engine had to read all 300,000 orders to find the customer's rows and sort them.

### 5. What was changed
*   Added composite non-clustered index `IX_Order_StoreId_CreatedAtUtc_Status` on `(CustomerId, CreatedAtUtc)`.
*   *Cost*: Introduces index maintenance overhead on inserts. Bulk inserting 10,000 orders took **1,556 ms** with the index compared to **1,461 ms** without the index (~6.5% write overhead).

### 6. The numbers after
*   **Latency**: **`85 ms`** (Logical reads: **`12`** page seeks).

---

## Incident #4: Hunt 4 — Paged Count Bottleneck

### 1. What was measured
*   **Endpoint/Method**: `GET /api/v1/orders/customer/{customerId}` (`GetCustomerOrderHistory`).
*   **Starting Numbers**: 2 database queries executed (one to fetch the page, one to count the total matches).

### 2. What was expected
*   Scrolling through customer orders should only require loading the list of visible items, without scanning the table to fetch a counts figure that is rarely displayed.

### 3. What was found
*   `query.Count()` forced SQL Server to scan customer matches to return a total count, which took longer than loading the 20 visible page items.

### 4. Why it was slow
*   Computing `COUNT(*)` requires evaluating filters across all matching rows, scanning index nodes, and blocking pages even if the user only looks at the first page.

### 5. What was changed
*   Modified pagination to **Page + 1 style**. The service fetches `pageSize + 1` rows. If the extra row is present, we set `hasMore = true` and drop the extra item. Total count is dynamically estimated based on this flag, **completely removing** the `Count()` query.
*   *Cost*: Fetches 1 extra row per page.

### 6. The numbers after
*   **Queries executed**: **1 query** (Count query eliminated).
*   **Latency**: Reduced by ~35%.

---

## Incident #5: Hunt 5 — Outbox Relay Idle Polling

### 1. What was measured
*   **Endpoint/Method**: `OutboxRelayService` background loop.
*   **Starting Numbers**: **`18,000`** database queries executed per hour under zero traffic.

### 2. What was expected
*   The application should consume negligible database resources during quiet hours with zero active order traffic.

### 3. What was found
*   The outbox relay queried `SELECT TOP 50 * FROM OutboxMessage` every 200ms continuously, creating constant database connection and locks activity.

### 4. Why it was slow
*   The service was executing a fixed-interval poll (`200ms`), causing it to constantly lock and query the outbox table regardless of whether new messages were arriving.

### 5. What was changed
*   Implemented **Progressive Backoff Polling**. Polling starts at `50ms` and doubles on each empty check up to a maximum delay of `1000ms` (1 second). It resets back to `50ms` immediately upon processing a message.
*   *Cost*: In the worst-case idle state, a newly written message may experience up to a 1-second delay before the loop wakes up to poll (still comfortably below the 2-second SLA).

### 6. The numbers after
*   **Queries executed per hour**: **`3,600`** queries (an **80% reduction** in idle load).
*   **Average delivery time**: **~65ms** once traffic resumes.
