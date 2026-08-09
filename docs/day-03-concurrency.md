# Concurrency & Lock Engineering Toolkit

This document explains the core concurrency concepts we analyzed, implemented, and verified in Wasil, detailing where they were applied (or why they were skipped).

---

## 1. Race Conditions & Lost Updates
 A race condition occurs when the correctness of a system depends on the execution timing or interleaving of concurrent operations. A *Lost Update* is a specific race anomaly where two transactions concurrently read the same state, modify it independently, and write it back sequentially—causing the second write to overwrite (and thus lose) the update made by the first.
* **Where We Met it :** We reproduced it in **Incident #1 (Overselling Stock)**. Two concurrent checkouts loaded the same product stock of `10`, both verified it was sufficient, decremented it to `9` in memory, and wrote it back. One of the checkouts was completely lost in the database, causing stock to become negative.

---

## 2. Transactions and Atomicity (All-or-Nothing)
 A database transaction groups multiple SQL operations into a single logical unit of work. Atomicity guarantees that either all operations within the group execute and commit successfully, or the entire transaction is rolled back, leaving the database completely untouched (no partial states).
* **Where We Met it :** Used in **Incident #5 (Idempotency)**. The checkout handler places an order, logs an audit trail, and records the idempotency key. If placing the order fails (e.g. stock validation fails), the entire unit rolls back, ensuring no empty idempotency placeholder or orphan order rows are committed.

---

## 3. Isolation Levels (Read Committed vs. Snapshot/Serializable)
 Isolation levels define the degree to which concurrently executing transactions are isolated from one another. 
  - **Read Committed (SQL Server Default):** Prevents reading dirty (uncommitted) data, but allows *Non-Repeatable Reads* (a row read twice inside a transaction can change because another transaction committed in between) and *Phantom Reads* (new rows can appear).
  - **Serializable:** The strictest isolation level. It places range locks on read data, preventing any modifications to locked ranges and completely eliminating all read anomalies.
  - **Snapshot (MVCC):** Rather than locking, it provides each transaction with a consistent transaction-scoped snapshot of the data (using `tempdb` row versioning). It prevents dirty reads, non-repeatable reads, and phantom reads without blocking writers.
* **Database Default:** SQL Server defaults to **Read Committed**.

---

## 4. Optimistic vs. Pessimistic Concurrency

  - **Optimistic Concurrency Control (OCC):** Assumes conflicts are rare. Transactions read and modify data without locks, but verify on write (via a version number or `rowversion` token) that no other transaction has modified the row. If a conflict is detected, the transaction fails and must be retried.
  - **Pessimistic Locking:** Assumes conflicts are likely. It proactively locks rows at the beginning of the read operation (e.g., using `SELECT ... WITH (UPDLOCK, ROWLOCK)`), blocking concurrent transactions until the lock holder commits.
* **Decision Rule:** Use **OCC** for high-read/low-write workloads where conflicts are rare, as it avoids locking overhead. Use **Pessimistic Locking** for high-contention, low-latency, critical operations (e.g., inventory deduction or checkout) where retry loops are expensive or business failures are unacceptable.

---

## 5. Atomic Single-Statement Updates
 Performing an update in a single SQL statement (e.g., `UPDATE Product SET Stock = Stock - 1 WHERE Id = @Id AND Stock >= 1`) instead of loading the row, calculating the new value in C#, and writing it back.
* **Where We Met it :** We discussed it as a lock-free option for stock decrementing. However, because our order creation requires complex business logic (generating audit trails, building order lines, validating store status), we chose transaction-scoped pessimistic row locks to encapsulate the entire block.

---

## 6. Unique Constraints as the Last Line of Defense
 Application-level checks (e.g. `Any(c => c.Code == code)`) are susceptible to race conditions. A database UNIQUE constraint acts as a bulletproof last line of defense. The application must gracefully catch the unique violation exception and handle it (e.g. by retrying or returning a specific response).
* **Where We Met it :** Applied in **Incident #2 (Order Code Collisions)**. When an order code collision threw a unique constraint exception, the application caught it, rolled back the save point, generated a new code, and retried up to 3 times before failing.

---

## 7. Idempotency Keys
 An API design pattern where a client sends a unique header (`Idempotency-Key`). If the server receives a request with a key it has already processed, it returns the cached response of the original request instead of executing the operation again.
* **Where We Met it :** Implemented in **Incident #5 (Idempotency: the double-tap)**. We used a primary key unique constraint on the key inside the database, combined with a `UPDLOCK` wait-and-query flow to handle both concurrent bursts and delayed retries safely.
