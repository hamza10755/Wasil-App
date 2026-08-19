# Day 3 — Concurrency Incident Reports

## Incident #1: Oversold Stock

### 1. Experiment
- **Endpoint**: `POST /api/v1/orders`
- **Concurrency**: 50 parallel requests fired at the exact same instant using NBomber's `Simulation.KeepConstant(copies: 50, during: 5s)`.
- **Starting State**: Product ID `160001` (Refined Steel Computer sr2s) configured with a starting `StockQuantity` of exactly **1**.

### 2. Expected
Exactly **1** order succeeds, and all other **49** orders are rejected with an error (due to insufficient stock). The final `StockQuantity` in the database must be exactly **0** (never negative).

### 3. Observed
- **Successful Orders**: **15** orders succeeded.
- **Final Stock Quantity**: **0**.
- **Result**: Phantom stock was sold! 14 orders were placed for products that did not exist in stock.

### 4. Why
This is a classic **lost update / check-then-act** race condition:
1. Request A and Request B arrive at the same time.
2. Request A reads the product row and sees `StockQuantity = 1`.
3. Request B reads the product row and sees `StockQuantity = 1`.
4. Request A passes the check `product.StockQuantity < lineDto.Quantity` (1 < 1 is false).
5. Request B passes the check `product.StockQuantity < lineDto.Quantity` (1 < 1 is false).
6. Request A decrements the stock: `product.StockQuantity -= 1` (stock becomes 0 in memory) and saves changes.
7. Request B decrements the stock: `product.StockQuantity -= 1` (stock becomes 0 in memory) and saves changes.
8. Both transactions commit. Both orders are successfully created, resulting in oversold stock.

### 5. Fix
We applied **pessimistic locking** on the product selection query in `OrderService.PlaceOrder` by sorting the requested product IDs (to prevent deadlocks) and using raw SQL with database locking hints:
```csharp
var products = _dbContext.Products
    .FromSqlRaw($"SELECT * FROM Product WITH (UPDLOCK, ROWLOCK) WHERE productId IN ({string.Join(",", sortedProductIds)})")
    .ToDictionary(p => p.Id);
```
- **Why this closes the race**: The `WITH (UPDLOCK, ROWLOCK)` lock hint ensures that any transaction reading the product row acquires an update lock on it. No other transaction can acquire an update lock on the same row until the holding transaction commits or rolls back. This serializes all order requests modifying the same product stock.
- **Cost**: Holds lock on product rows for the duration of the order transaction, introducing minor latency under high contention for the same product, but guarantees consistency.

### 6. Proof
We ran the exact same experiment again (50 concurrent order requests on Product ID `200001` with starting stock = 1):
- **Successful Orders**: **1**
- **Final Stock Quantity**: **0**
- **Failed Requests**: **296** (all rejected with `Insufficient stock for product` errors).

## Incident #2: Duplicate Order Codes

### 1. Experiment
- **Endpoint**: `POST /api/v1/orders`
- **Concurrency**: 50 parallel requests.
- **Starting State**: We enable `ASPNETCORE_FORCE_COLLISION=true` in the API environment. This forces the order code generator to yield a static colliding order code (`2026COLLID99`) for the first two order attempts.

### 2. Expected
Even if two concurrent requests generate identical order codes, the system must handle the collision gracefully. One request should successfully write the order with the code, while the colliding request(s) must retry, obtain a new unique code, and successfully place the order without returning a raw database unique constraint 500 error to the client.

### 3. Observed
- **Before Fix**: Under code collision, one request succeeded, while the colliding request failed with a `500 Internal Server Error` wrapping `Microsoft.Data.SqlClient.SqlException: Cannot insert duplicate key row...` (Unique constraint violation).
- **After Fix**: Under the same collision setup, the colliding request retries, generates a unique code, and successfully places the order. Both orders are created successfully (status code 201).

### 4. Why
1. Request A and Request B are processed concurrently.
2. Both generate the same order code `"2026COLLID99"`.
3. Both run `Any(o => o.OrderCode == "2026COLLID99")` and find that it does not exist yet.
4. Both attempt to insert the order.
5. The database commits Request A first.
6. Request B attempts to commit next, triggering a unique constraint index violation on `OrderCode`. The transaction throws a `DbUpdateException` and is rolled back.

### 5. Fix
We wrapped `_dbContext.SaveChanges()` in the retry loop in `PlaceOrder` with a try-catch block catching `DbUpdateException`.
- **Catching unique constraint violations**: We inspect the SQL error numbers: `2601` (duplicate key index) and `2627` (unique constraint violation).
- **Graceful Retrying**: If a collision is caught, we update the `OrderCode` on the tracked entity with a newly generated code, log a warning, and retry `SaveChanges()` in the next iteration of the loop.
- **Cost**: Extremely low. Only incurs a retry overhead when a true key collision occurs.

### 6. Proof
We ran the load test with `ASPNETCORE_FORCE_COLLISION=true` and a starting product stock of **10**.
- **Successful Orders**: **10** (all orders succeeded up to the stock limit, none failed due to unique constraint violations).
- **Logs**: The warning logs show the system detected the collision and retried successfully without crashing.

## Incident #3: OTP Guess-Limit Bypass & Double Consume

### 1. Experiment
- **Endpoint**: `POST /api/auth/customer/verify-otp`
- **Starting State**: An OTP is requested for a customer phone number starting with `"999"`, which triggers our backdoor to generate a predictable OTP code `"123456"`.
- **Test A (Guess-Limit)**: Fire 30 concurrent verification requests with an **incorrect** code (`"999999"`).
- **Test B (Double-Consume)**: Fire 5 concurrent verification requests with the **correct** code (`"123456"`).

### 2. Expected
- **Test A**: Exactly 3 wrong attempts should be checked before the OTP is locked out (returning `429 Too Many Attempts` for all subsequent attempts).
- **Test B**: Exactly 1 login attempt succeeds and receives a `200 OK` + JWT token, while all other attempts are rejected with `401 Unauthorized`.

### 3. Observed
- **Before Fix**:
  * **Test A**: **27** requests returned `401 Unauthorized` (meaning they were all allowed to be checked as invalid attempts) and only 3 returned `429`, indicating a massive guess-limit bypass.
  * **Test B**: Under high concurrency, multiple requests could retrieve the valid OTP from cache, verify it, and obtain valid tokens (double consume) before the cache key was deleted.
- **After Fix**:
  * **Test A**: Exactly **2** requests returned `401` (wrong guess) and **28** returned `429` (locked out/limit exceeded).
  * **Test B**: Exactly **1** request successfully returned `200 OK`, and the remaining **4** returned `401`.

### 4. Why
This is a **read-modify-write / check-then-act** race condition on the in-memory cache:
1. Multiple requests for the same phone number arrive at the same time.
2. They all call `_cache.TryGetValue` and read the same `OtpDetails` object.
3. Because reading and modifying/deleting from the cache is not atomic, multiple threads pass the check `Attempts < 3` or `Code matches` at the same time before any thread can update the counter or remove the key from the cache.

### 5. Fix
We introduced thread-safe serialization for the in-memory cache operations inside `VerifyOtp` using a static `ConcurrentDictionary<string, object>` to obtain a lock object per phone number:
```csharp
var lockObj = OtpLocks.GetOrAdd(cacheKey, _ => new object());
lock (lockObj)
{
    // Atomically check cache, check attempts, increment attempts, or remove key
}
```
- **Why this closes the race**: Concurrent requests for the same phone number are forced to acquire the lock and execute the cache validation and state updates sequentially. This guarantees that guess counts are incremented atomically and consumption of correct codes deletes the cache entry instantly before any subsequent thread can read it.
- **Cost**: Extremely low lock contention because it locks *per phone number* rather than globally.

### 6. Proof
We ran Scenario 3 using the `Task.WhenAll` concurrent caller:
- **Test A (Guess-Limit)**: Enforced successfully. Exactly 3 wrong guesses were checked (2 returned `401` and 28 returned `429`).
- **Test B (Double-Consume)**: Prevented successfully. Exactly 1 request returned `200 OK` (JWT issued) and the other 4 returned `401`.

## Incident #4: Refresh-Token Rotation Race

### 1. Experiment
- **Endpoint**: `POST /api/auth/refresh`
- **Starting State**: An active user session with a valid Access Token and a generated active Refresh Token.
- **Action**: Fire 2 concurrent POST requests to the refresh endpoint at the exact same instant using the **same** Refresh Token.

### 2. Expected
Exactly one request should perform the standard rotation (creating one new active refresh token chain). The second concurrent request should be handled gracefully (returning the already rotated token chain instead of failing or triggering a session-nuking reuse defense action).

### 3. Observed
- **Before Fix**:
  * Both requests succeeded (`200 OK`) and received two completely different refresh tokens.
  * In the database, two active refresh tokens were created for the user. This is a **Double-Issue** vulnerability, splitting the token chain and allowing multiple active sessions to coexist under one token's authority.
- **After Fix**:
  * Both requests succeeded (`200 OK`) and returned the **exact same** rotated refresh token.
  * In the database, only **one** new active refresh token was created. The second request successfully detected the concurrent retry within the 2-second grace period and returned the already-rotated token.

### 4. Why
The default Entity Framework query loads the refresh token row without locking it. Under concurrent requests:
1. Thread A and Thread B retrieve the same `storedRefreshToken` where `RevokedOn == null`.
2. Both pass the validation check.
3. Both update `RevokedOn = DateTime.UtcNow` and add a new active refresh token to the context.
4. Both save changes successfully, creating two active refresh tokens.

### 5. Fix
We wrapped the refresh token check and update logic in a database transaction block managed by the EF Core execution strategy and utilized a pessimistic update row lock on the refresh token:
```csharp
var strategy = _context.Database.CreateExecutionStrategy();
return await strategy.ExecuteAsync<IActionResult>(async () =>
{
    using var transaction = await _context.Database.BeginTransactionAsync();
    var storedRefreshToken = await _context.RefreshTokens
        .FromSqlRaw("SELECT * FROM RefreshTokens WITH (UPDLOCK, ROWLOCK) WHERE Token = {0}", request.RefreshToken)
        .FirstOrDefaultAsync();
    
    // ... validation and grace period check ...
});
```
We also implemented a **2-second grace period**:
If a token is already revoked, but the revocation timestamp `RevokedOn` is less than 2.0 seconds ago, we assume it is a concurrent client retry. Instead of revoking all sessions, we fetch and return the `ReplacedByToken` token chain generated by the winning thread.

### 6. Proof
We ran Scenario 4 concurrency test:
- **Responses**: Both requests returned status `200 OK` and returned the same active token.
- **Database state**: Exactly 1 new active refresh token was inserted.
- **Logs**: The server logs successfully logged: `"Concurrent refresh token retry detected... within grace period. Returning existing rotated token."`

## Incident #5: Idempotency: the double-tap

### 1. Experiment
- **Endpoint**: `POST /api/v1/orders`
- **Starting State**: A customer placing an order with a distinct payload (products with stock).
- **Action**: Fire 10 concurrent requests at the exact same instant using the same `Idempotency-Key` header, then wait 3 seconds and fire a delayed 11th request using the same key.

### 2. Expected
Exactly one order should be created in the database, and all 11 requests should receive a successful `201 Created` response containing that identical single order payload.

### 3. Observed
- **Before Fix**:
  * All 10 concurrent requests and the 11th delayed request succeeded and created **11 duplicate orders** for the customer.
- **After Fix**:
  * Exactly **1** order was created in the database.
  * The winning request returned `201 Created`. The concurrent requests returned `409 Conflict` (during processing) or `201 Created` (once the winning request completed and cached the result).
  * The 11th delayed retry returned `201 Created` containing the exact same order details.

### 4. Why
Without an idempotency key verification system, the server treats every incoming HTTP request as a new distinct intent, processing and committing duplicate orders. Using a standard "SELECT to check, then INSERT" check-then-act flow would introduce a race condition under concurrency, allowing duplicate inserts before the first commits.

### 5. Fix
We introduced a database-backed unique key idempotency table `IdempotentRequests` and wrapped the creation step inside a transaction:
1. Try to insert an `IdempotentRequest` placeholder with the key and a null body.
2. If the insert succeeds, we place the order, serialize the order JSON, update the row's `ResponseBody` and `StatusCode`, and commit.
3. If the insert throws a UNIQUE key violation exception, we roll back, issue a pessimistic select query `SELECT * FROM IdempotentRequests WITH (UPDLOCK, ROWLOCK) WHERE IdempotencyKey = {key}`. This blocks the request until the winning thread commits. Once released, it reads and returns the cached JSON response.
4. We also modified `OrderService.PlaceOrder` to support ambient transactions: it now checks if a transaction is already active (`_dbContext.Database.CurrentTransaction != null`) before calling `BeginTransaction()`, allowing it to run safely inside the controller's idempotency transaction context.

### 6. Proof
We ran Scenario 5 idempotency concurrency test:
- **Database state**: Exactly 1 order was created for the customer.
- **Responses**: Concurrent requests returned `409 Conflict` or `201 Created` with identical payloads. The delayed 11th retry request returned `201 Created` with the identical order payload.

## Incident #6: Induce a deadlock, then design it out

### 1. Experiment
- **Endpoint**: `POST /api/v1/orders` (without Idempotency-Key header to run normal creation)
- **Starting State**: Two products P1 and P2 in the same store with stock.
- **Action**: Fire two concurrent request payloads:
  * **Order A**: Product 1 first, then Product 2.
  * **Order B**: Product 2 first, then Product 1.
  Repeat in a loop of 20 iterations at the same instant.

### 2. Expected
All orders should complete successfully, or if there is locking, they should block sequentially and complete without throwing database transaction deadlock failures.

### 3. Observed
- **Before Fix (Vulnerable)**:
  * SQL Server threw multiple deadlock exception alerts in the server logs: `"Transaction (Process ID X) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction."`
  * Although EF Core's transient retry strategy successfully re-ran the failed transactions and eventually completed the requests, it caused high database CPU utilization and added up to 3 seconds of transient retry latency.
- **After Fix (Secure)**:
  * All 20 iterations succeeded instantly without any deadlock exceptions in the SQL Server logs.

### 4. Why
When locks are acquired in an inconsistent order (e.g. Order A locks P1 first, then P2; Order B locks P2 first, then P1), concurrent requests create a circular wait condition where Transaction A holds P1 and blocks waiting for P2, while Transaction B holds P2 and blocks waiting for P1. SQL Server resolves this deadlock by terminating one of the processes.

### 5. Fix
We enforced consistent lock ordering by sorting the product IDs in ascending order before querying and locking them in the database:
```csharp
var productIds = dto.Lines.Select(l => l.ProductId).Distinct().ToList();
var sortedProductIds = productIds.OrderBy(id => id).ToList();

var products = _dbContext.Products
    .FromSqlRaw($"SELECT * FROM Product WITH (UPDLOCK, ROWLOCK) WHERE productId IN ({string.Join(",", sortedProductIds)})")
    .ToDictionary(p => p.Id);
```
Since both transactions now lock the products in the exact same ascending sequence `(P1, then P2)`, circular locks are impossible.

### 6. Proof
We ran Scenario 6 deadlock concurrency test:
- **Responses**: All 40 requests completed successfully.
- **Logs**: The SQL Server logs verified that 0 deadlock exceptions were thrown during the run.

## Incident #7: The Dual-Write Gap (Day 6 Outbox)

### 1. Experiment
- **Endpoint**: `POST /api/v1/orders`
- **Starting State**: Application running normally, saving orders and publishing events directly from the controller.
- **Action**: Inject `Environment.Exit(1)` immediately after database context `SaveChanges()` commits, but before the event is published to RabbitMQ. Send one order.

### 2. Expected
The database transaction and the RabbitMQ publishing must happen as a single atomic unit. If the server crashes, either the order is not saved and no event is sent, or both succeed.

### 3. Observed
The order was successfully saved to SQL Server, but the event was never published to RabbitMQ. The server crashed silently, leaving the order in a "Pending" status forever. No errors or logs captured the failure.

### 4. Why
SQL Server and RabbitMQ are two different systems with no shared transaction coordinator. If the application dies in the gap between the two writes, the second write is lost.

### 5. Fix
We removed direct publishing from the controller/service request path. Instead, we write the event details into an `OutboxMessage` record in the same database transaction as the order, and configured a background `OutboxRelayService` to poll and publish these messages asynchronously.
- **Why this closes the race**: Both rows are committed to SQL Server in a single atomic database transaction. If the app crashes, neither or both are saved.
- **Cost**: Adds minor storage overhead and a small delay (up to 200ms) before the event reaches the broker.

### 6. Proof
We ran the crash test with `Environment.Exit(1)` injected after `transaction.Commit()`. The app crashed. On startup, the `OutboxRelayService` immediately detected the unsent outbox record, published it successfully, and the consumer updated the tally. Nothing was lost.
