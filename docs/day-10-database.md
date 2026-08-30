# Day 10 Database Performance Diagnostics (Parts 1 to 4)

This report details the execution plan analyses, page access metrics, and index behavior experiments completed on the 300,000-row `Wasil` database.

---

## Part 1: Indexing & Storage Diagnostics

### 1. Starting Database Indexes (Filtered Clean List)
*   **`AuditTrail`**: `PK_AuditTrail` (Clustered), `IX_AuditTrail_UserId` (Non-Clustered)
*   **`Order`**: `PK_Order` (Clustered), `IX_Order_customerId` (Non-Clustered), `IX_Order_driverId` (Non-Clustered), `IX_Order_storeId` (Non-Clustered), `IX_Order_OrderCode` (Non-Clustered, Unique)
*   **`OrderLine`**: `PK_OrderLine` (Clustered), `IX_OrderLine_orderId` (Non-Clustered), `IX_OrderLine_productId` (Non-Clustered)

*(Note: Day 9 composite optimization indexes were temporarily dropped to establish a clean starting baseline).*

### 2. Clustered Seek vs. Non-Clustered Seek vs. Scan Timings

We ran three queries seeking a single record:
```sql
-- Query A: Clustered Index Seek (Search by primary key orderId)
SELECT * FROM [Order] WHERE orderId = 391160;

-- Query B: Non-Clustered Seek (Search by Unique OrderCode)
SELECT * FROM [Order] WHERE OrderCode = '250810030252';

-- Query C: Unindexed Scan (Search by unindexed Total column)
SELECT * FROM [Order] WHERE Total = 4153.48;
```

#### Results:
*   **Query A**: **`3` logical reads** (Clustered Index Seek)
*   **Query B**: **`6` logical reads** (Non-Clustered Index Seek + Clustered Key Lookup)
*   **Query C**: **`5,687` logical reads** (Clustered Index Scan)

#### Why Query C is extremely expensive:
Without an index on the `Total` column, SQL Server has no sorted directory structure to find the value `4153.48`. It is forced to perform a **Clustered Index Scan**, which means reading every single one of the **5,687 data pages** in the table from disk/memory to evaluate the comparison for all 300,000 rows.

---

## Part 2: Key Lookups vs. Covering Indexes

We set up a non-clustered index on `customerId` and queried customer `481031` (who has 498 orders):
```sql
CREATE INDEX IX_Orders_CustomerId ON [Order] (customerId);
```

### 1. Index-Only Columns vs. Key Lookup Query Plan

*   **Query 1 (Index Columns Only)**:
    ```sql
    SELECT customerId, orderId FROM [Order] WHERE customerId = 481031;
    ```
    *   *Result*: **`4` logical reads** (Index Seek).
*   **Query 2 (Additional Columns)**:
    ```sql
    SELECT customerId, orderId, OrderCode, orderStatus, Total, CreatedAtUtc FROM [Order] WHERE customerId = 481031;
    ```
    *   *Result*: **`1,538` logical reads** (Index Seek + Key Lookup).

#### Why Query 2 logical reads spiked by 380x:
Since `OrderCode`, `orderStatus`, `Total`, and `CreatedAtUtc` columns are not stored inside the `IX_Orders_CustomerId` index, the index is not **covering**. SQL Server seeks the index to find the 498 matching rows, and then for **each and every row**, it must execute a **Key Lookup** against the clustered index (table) to fetch the missing columns. This adds `498 * 3` additional page reads.

### 2. Covering Index Fix
We replaced the index with a covering index including the columns:
```sql
CREATE INDEX IX_Orders_CustomerId_Covering ON [Order] (customerId)
    INCLUDE (OrderCode, orderStatus, Total, CreatedAtUtc);
```
*   **Query 2 (Re-run)**: **`9` logical reads** (Covering Index Seek, no lookup required).

#### What a covering index is and its trade-offs:
A **covering index** is an index that stores all the columns requested by a specific query as leaf-level "Included" columns. It completely avoids Key Lookups.
*   *Write Cost*: It is not free. The index becomes physically larger, and every `INSERT` or `UPDATE` operation on any of those five columns must write the values to the index copy, slowing down write operations.

---

## Part 3: Composite Column Order

We analyzed composite indexing on `(customerId, CreatedAtUtc)`.

### 1. Baseline Sort (No Index)
```sql
SELECT TOP 20 customerId, orderId, CreatedAtUtc FROM [Order]
WHERE customerId = 481031
ORDER BY CreatedAtUtc DESC;
```
*   *Result*: **`1,541` logical reads** (Clustered Index Scan + Sort step).

### 2. Seeking Composite Index (Sargability of Multi-Key Indexes)
We created:
```sql
CREATE INDEX IX_Orders_Customer_Created ON [Order] (customerId, CreatedAtUtc);
```

We ran three queries using parts of the index:
1.  **Query 1 (Knows First Column)**: `WHERE customerId = 481031`
    *   *Result*: **`6` logical reads** (Index Seek).
2.  **Query 2 (Knows Both Columns)**: `WHERE customerId = 481031 AND CreatedAtUtc > '2025-12-01'`
    *   *Result*: **`4` logical reads** (Index Seek).
3.  **Query 3 (Knows Only Second Column)**: `WHERE CreatedAtUtc > '2026-06-01'`
    *   *Result*: **`829` logical reads** (Index Scan).

#### Why the Sort disappeared on re-running Step 1:
Because the index is stored physically sorted by `customerId` first, and then by `CreatedAtUtc` second. For a specific customer block, the date order is already pre-sorted. SQL Server can seek to the customer's block and read the rows backward to satisfy `ORDER BY CreatedAtUtc DESC` without executing a CPU-intensive memory/disk sort.

#### If column order was reversed to `(CreatedAtUtc, customerId)`:
A query filtering on `customerId = 481031` and sorting on `CreatedAtUtc DESC` would **not** be able to seek. Because the phone book is sorted by Date first, customer `481031`'s orders would be scattered across every date block, requiring a full index scan and a manual sort.

---

## Part 4: Sargability (Index Suppression)

We created temporary test indexes on `CreatedAtUtc`, `Total`, and product `Name`, and ran comparative query pairs:

### 1. Sargable vs. Non-Sargable Benchmark Results

| Pair | Query Style | Syntax | Logical Reads | Plan Operation |
| :--- | :--- | :--- | :--- | :--- |
| **Pair 1** | Non-Sargable | `WHERE CONVERT(date, CreatedAtUtc) = '2026-03-01'` | **`8`** | Index Scan |
| | Sargable | `WHERE CreatedAtUtc >= '2026-03-01' AND CreatedAtUtc < '2026-03-02'` | **`6`** | Index Seek |
| **Pair 2** | Non-Sargable | `WHERE Total + 0 = 4153.48` | **`715`** | Index Scan |
| | Sargable | `WHERE Total = 4153.48` | **`3`** | Index Seek |
| **Pair 3** | Non-Sargable | `WHERE name LIKE '%phone'` | **`341`** | Index Scan |
| | Sargable | `WHERE name LIKE 'phone%'` | **`3`** | Index Seek |
| **Pair 4** | Non-Sargable | `WHERE Code = N'12345'` (against a `varchar` column) | **`147`** | Index Scan |
| | Sargable | `WHERE Code = '12345'` (against a `varchar` column) | **`2`** | Index Seek |

### 2. Why the slow queries are slow (The Sargable Rule)
An index is a sorted copy of the data. The moment you wrap a column inside an arithmetic operation (`Total + 0`), a function (`CONVERT`), or a wildcard prefix (`%word`), the database can no longer calculate where the values sit inside the sorted index tree structure. 

This forces SQL Server to perform an **Index Scan**—calculating the formula or function for every row in the index, destroying seek benefits.

#### Sargability Rule:
> **Keep the column alone on one side of the comparison, and perform any calculations, string formatting, or type conversions on the literal/parameter side.**

---

## Part 5: Bulk Operations (Sets vs. Loops)

We benchmarked a 10% price increase on 800 products for a single store in two different ways:
1.  **Way 1 (C# Loop)**: Loading entities into memory and calling `SaveChangesAsync()` sequentially inside a loop.
2.  **Way 2 (Bulk Set Statement)**: Calling EF Core `ExecuteUpdateAsync` to perform the update in a single database statement.

### 1. Benchmark Timings & DB Trips
*   **Way 1 (C# Loop)**: **`6,259` ms** (made **800** separate database roundtrips).
*   **Way 2 (Bulk Set)**: **`122` ms** (made exactly **1** database roundtrip, representing a **51x** speed increase).

*Question: At what number of products would Way 1 become completely unusable?*
If the store carried 10,000 products, Way 1 would take over **78 seconds** to run (blocking threads and hogging locks), whereas Way 2 would complete in under **1 second** because the execution scales with SQL Server's internal page update engine rather than network IO latency.

### 2. The Silent Trap: What the Bulk Update Bypassed
While Way 2 was extremely fast, it sent raw SQL directly to the database engine. As a result, it bypassed all EF Core change tracking and interceptors:
*   **AuditTrail Logs**: **`0`** audit rows were created for the bulk update (compared to exactly **800** audit rows created by the loop).
*   **UpdatedAtUtc Fields**: The `UpdatedAtUtc` column was **not** updated for any of the 800 modified products.

#### Architectural Decision:
> **For business-critical operations where auditing and modification timestamps are required (like changing product prices or security updates), use standard Change-Tracker loops, or manually write audit logs and update fields inside the same bulk SQL transaction. Never use raw bulk updates blindly.**

---

## Part 6: Query Parameter Sniffing & Guesses

SQL Server builds execution plans based on statistics. We analyzed how variables affect its selectivity estimates for customer `481031` (who has 498 orders):

### 1. Selectivity Estimations Comparison

| Query Style | Estimated Rows | Actual Rows | Plan Guess Status |
| :--- | :--- | :--- | :--- |
| **Query A (Constant in query)** | **`484.5`** | **`498`** | **Very Good** |
| **Query B (Declared Variable)** | **`15.0`** | **`498`** | **Wildly Wrong** (Average fallback) |
| **Query B with `OPTION (RECOMPILE)`** | **`484.5`** | **`498`** | **Very Good** |

#### Why Query B guesses so badly:
When a query compiles with a declared variable (`DECLARE @cid ...`), SQL Server must choose an execution plan *before* the variable's value is evaluated. Without a concrete parameter value to look up in the histogram statistics, it falls back to the database **average selectivity**:
$$\text{Total Orders } (300,260) \div \text{Unique Customers } (20,013) = 15.00135$$
It guesses 15 rows will return, even if the true value is 2,000.

#### What `OPTION (RECOMPILE)` does:
*   *Fix*: It forces SQL Server to throw away the cached query plan and compile a new plan *at execution time* once the variable values are bound. This aligns the guess with reality.
*   *Cost*: Re-compiling a query plan consumes CPU cycles on SQL Server. If executed thousands of times per second, the compilation overhead will throttle database CPU.

---

## Part 7: Source-Generated Mappers vs. Entity Projections

We added **Riok.Mapperly** to compile mapping code at build-time.

### 1. Generated Code Snippet (`WasilMapper.g.cs`)
Mapperly writes clean, allocation-free mapping code directly into your assembly at compile-time:
```csharp
public partial global::Wasil.Service.DTOs.AddressDto MapAddressToDto(global::Wasil.Data.Entities.Address address)
{
    var target = new global::Wasil.Service.DTOs.AddressDto();
    target.Id = address.Id;
    target.CustomerId = address.CustomerId;
    target.Street = address.Street;
    target.City = address.City;
    target.ZipCode = address.ZipCode;
    return target;
}
```

### 2. Compile-Time Validation Safety
Unlike reflection-based mappers (like AutoMapper) which check mapping safety at runtime, Mapperly evaluates properties at compile time:
*   Adding an unmapped property (like `FakeProperty`) to a DTO with `TreatWarningsAsErrors` enabled immediately halts compilation with error **`RMG012`**, preventing silently empty fields from reaching production.

### 3. The Query Projection Trap (Entity vs. DTO Select)
We evaluated retrieving order details in two ways:
*   **Way 1 (Load Entity then Map)**:
    *   *SQL*: `SELECT [Id], [OrderCode], [Status], [Subtotal], [Total], [StoreId], ... FROM [Order]` (queries all 14 columns).
    *   *Result*: **Heavy Logical Page Reads** because SQL Server has to fetch large text blocks, address lines, and audit columns.
*   **Way 2 (Project directly in Query)**:
    *   *SQL*: `SELECT [Id], [OrderCode], [Total] FROM [Order]` (queries only the 3 requested columns).
    *   *Result*: **Minimal Page Reads** as it only touches index pages.

#### The Mapping Rule:
> **For reading data from the database, always project inside the query (`.Select(o => new Dto { ... })`). For transforming objects already residing in memory (like handling HTTP request payloads or incoming RabbitMQ events), use the Mapper.**

---

## Part 8: Final Index Verification

We restored the database to its target production state. Active indexes:
1.  **Day 1 Baselines**: Primary key clustered indexes and unique constraints.
2.  **Day 9 Performance Indexes**:
    *   `IX_Order_StoreId_CreatedAtUtc_Status` (Covering store dashboard history).
    *   `IX_OrderLine_OrderId_Includes` (Covering order line retrievals).
    *   `IX_AuditTrail_TimestampUtc_DESC` (Covering recent audit trails).
3.  **Day 10 Index**:
    *   `IX_Orders_Customer_Created` on `(customerId, CreatedAtUtc)` (Covering customer sorted history).

