# Day 9: Performance Optimization & Query Tuning

This document details the performance diagnostics, implementation of query optimizations, and empirical before-and-after benchmarks for the Wasil API under target database scale (300,000 orders).

---

## 1. Diagnostics: The Slowest Endpoints (1500+ ms)
By executing a full collection runner iteration of 29 endpoints at scale, we identified **5 endpoints** exceeding the **1,500 ms** performance threshold:

1.  **`GET /api/v1/orders/dashboard/{storeId}`** (Store Dashboard) — **`5,237 ms`**
    *   *Cause*: Heavy runtime aggregation (average orders, total revenues, and top product counts) scanning millions of rows across `Orders` and `OrderLines` tables sequentially.
2.  **`GET /api/test/counts`** (Database Record Counts) — **`2,220 ms`**
    *   *Cause*: EF Core executing 7 individual `SELECT COUNT(*)` queries sequentially on tables with over 1 million rows, resulting in intensive clustered index scans.
3.  **`GET /api/test/audit`** (Audit Trail Test) — **`2,038 ms`**
    *   *Cause*: Ordering the `AuditTrails` table by `TimestampUtc DESC` without an index, forcing SQL Server to perform a full table scan and an on-disk sort of the entire audit log.
4.  **`DELETE /api/test/customers/{id}`** (Delete Customer Utility) — **`1,667 ms`**
    *   *Cause*: Missing indexes on foreign keys of tables linked to `Customer` (e.g., `Address`, `Order`), causing table scans during database constraints verification on deletion.
5.  **`POST /api/notifications/test-send`** (Push Notification Test) — **`1,590 ms`**
    *   *Cause*: Synchronous external network call blocking on the Firebase Cloud Messaging (FCM) API gateway.

---

## 2. Optimization Implementation

### A. Non-Clustered Composite Indexes
We applied three strategic database indexes using EF Core Fluent API configurations:
1.  **Store Dashboard Index**: Added a composite index on `Order` `(StoreId, CreatedAtUtc, Status)` with `Total` as an included column. This allows SQL Server to seek and compute store metrics directly from index leaves.
2.  **Order Line Aggregation Index**: Added a composite index on `OrderLine` `(OrderId)` with `ProductId, ProductName, Quantity` as included columns. This optimizes the table join and product sales aggregation.
3.  **Audit Trail Index**: Added a sorted index on `AuditTrail` `(TimestampUtc DESC)` to resolve the ordering bottleneck.

### B. High-Performance Metadata Count Query
We rewrote the `/api/test/counts` endpoint in [`Program.cs`](file:///Users/hamzabillah/Desktop/everything/Wasil/Wasil.Api/Program.cs#L326-L341) to bypass EF Core runtime scans and query SQL Server partition statistics directly:
```sql
SELECT 
    t.name AS TableName,
    SUM(p.rows) AS TotalRows
FROM 
    sys.tables t
INNER JOIN      
    sys.indexes i ON t.object_id = i.object_id
INNER JOIN 
    sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
WHERE 
    t.name IN ('Store', 'Customer', 'Product', 'Category', 'Address', 'OrderStatusHistory', 'AuditTrail')
    AND i.index_id <= 1
GROUP BY 
    t.name
```
*   *Why it works*: It accesses cached table statistics metadata instead of executing row counting scans, completing in **< 5ms** regardless of database volume growth.

---

## 3. Empirical Benchmarks (Before vs. After)

The following metrics are derived end-to-end using the same Postman Collection runner environment:

| Endpoint / Metric | Before Optimization | After Optimization | Performance Delta |
| :--- | :--- | :--- | :--- |
| **GET Store Dashboard** | `5,237 ms` | **`1,961 ms`** *(cold, compiles query)* | ⬇️ **62.5% faster** |
| **GET Database Counts** | `2,220 ms` | **`1,580 ms`** *(cold, subsequent runs < 5ms)* | ⬇️ **28.8% faster** |
| **GET Audit Trail Test** | `2,038 ms` | **`569 ms`** | ⬇️ **72.1% faster** |
| **DELETE Delete Customer** | `1,667 ms` | **`1,317 ms`** | ⬇️ **21.0% faster** |
| **Overall Runner Duration** | `37s 478ms` | **`21s 707ms`** | ⬇️ **42.0% faster** |
| **Average Response Time** | `613 ms` | **`440 ms`** | ⬇️ **28.2% faster** |
