using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using NBomber.CSharp;
using NBomber.Http;
using NBomber.Http.CSharp;
using Wasil.Data;
using Wasil.Data.Entities;
using Wasil.Data.Enums;

namespace ApiLoadTests;

class Program
{
    static async Task Main(string[] args)
    {
        const string baseUrl = "http://localhost:5247";
        Console.WriteLine("=================================================");
        Console.WriteLine($" Wasil Concurrency Test Harness - NBomber");
        Console.WriteLine($" Target API: {baseUrl}");
        Console.WriteLine("=================================================");

        // Parse connection string and JWT settings from Wasil.Api appsettings.json
        string apiSettingsPath = Path.Combine(AppContext.BaseDirectory, "../../../../Wasil.Api/appsettings.json");
        if (!File.Exists(apiSettingsPath))
        {
            apiSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "Wasil.Api/appsettings.json");
        }

        string connectionString = "Server=127.0.0.1;Database=WasilDbContext;User Id=sa;Password=WasilP@ssw0rd!;TrustServerCertificate=True;";
        string jwtKey = "WasilSecretKeyForJwtTokenGeneration123456";
        string jwtIssuer = "Wasil.Api";
        string jwtAudience = "Wasil.Api.Users";

        if (File.Exists(apiSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(apiSettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connSection) &&
                    connSection.TryGetProperty("DefaultConnection", out var defaultConn))
                {
                    connectionString = defaultConn.GetString() ?? connectionString;
                }
                if (doc.RootElement.TryGetProperty("Jwt", out var jwtSection))
                {
                    if (jwtSection.TryGetProperty("Key", out var keyProp)) jwtKey = keyProp.GetString() ?? jwtKey;
                    if (jwtSection.TryGetProperty("Issuer", out var issuerProp)) jwtIssuer = issuerProp.GetString() ?? jwtIssuer;
                    if (jwtSection.TryGetProperty("Audience", out var audProp)) jwtAudience = audProp.GetString() ?? jwtAudience;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to parse appsettings.json: {ex.Message}. Using default credentials.");
            }
        }

        // Setup DbContext to initialize database state for test
        var dbOptions = new DbContextOptionsBuilder<WasilDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        using var db = new WasilDbContext(dbOptions);

        Console.WriteLine($"DB Customers: {await db.Customers.CountAsync()}");
        Console.WriteLine($"DB Products : {await db.Products.CountAsync()}");
        Console.WriteLine($"DB Stores   : {await db.Stores.CountAsync()}");

        // Find a customer user and a product
        var customer = await db.Customers.Include(c => c.User).FirstOrDefaultAsync();
        if (customer == null)
        {
            Console.WriteLine("Error: No customer found in the database. Please run seed first.");
            return;
        }

        Console.WriteLine($"Found Customer: {customer.FirstName} {customer.LastName}, User Role: {customer.User?.Role}, User ID: {customer.UserId}");

        var product = await db.Products.FirstOrDefaultAsync();
        if (product == null)
        {
            Console.WriteLine("Error: No product found. Please run seed first.");
            return;
        }

        // Ensure target customer has at least one address
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
        if (address == null)
        {
            address = new Address
            {
                CustomerId = customer.Id,
                Street = "Test Main St",
                City = "Amman",
                ZipCode = "11190"
            };
            db.Addresses.Add(address);
            await db.SaveChangesAsync();
        }

        // Setup Experiment: Set stock of product to exactly 10
        Console.WriteLine($"Setting product '{product.Name}' (ID: {product.Id}, Store ID: {product.StoreId}) stock to 10...");
        product.StockQuantity = 100;
        db.Entry(product).State = EntityState.Modified;
        await db.SaveChangesAsync();

        // Generate JWT token for this customer
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, customer.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, customer.UserId.ToString()),
            new Claim(ClaimTypes.Role, "Customer")
        };
        var jwtToken = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );
        string tokenString = new JwtSecurityTokenHandler().WriteToken(jwtToken);

        // Setup HttpClient
        using var httpClient = Http.CreateDefaultClient();

        // 1. Scenario: Public Stores GET
        var fetchStoresScenario = Scenario.Create("fetch_stores", async context =>
        {
            var step = await Step.Run("fetch_stores_step", context, async () =>
            {
                var request = Http.CreateRequest("GET", $"{baseUrl}/api/v1/stores?page=1&pageSize=10")
                                  .WithHeader("Accept", "application/json");

                return await Http.Send(httpClient, request);
            });
            return step;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(2))
        .WithLoadSimulations(Simulation.KeepConstant(copies: 5, during: TimeSpan.FromSeconds(10)));

        // 2. Scenario: Place Order Concurrency (Incident #1)
        var placeOrderScenario = Scenario.Create("place_order_concurrency", async context =>
        {
            var step = await Step.Run("place_order_step", context, async () =>
            {
                var jsonBody = "{" +
                               $"\"customerId\": {customer.Id}," +
                               $"\"storeId\": {product.StoreId}," +
                               $"\"addressId\": {address.Id}," +
                               "\"paymentMethod\": 0," +
                               $"\"lines\": [({$"\"productId\": {product.Id}, \"quantity\": 1"})]" +
                               "}";

                // Fix format of lines json array: [ { "productId": X, "quantity": 1 } ]
                jsonBody = "{" +
                           $"\"customerId\": {customer.Id}," +
                           $"\"storeId\": {product.StoreId}," +
                           $"\"addressId\": {address.Id}," +
                           "\"paymentMethod\": 0," +
                           $"\"lines\": [ {{\"productId\": {product.Id}, \"quantity\": 1}} ]" +
                           "}";

                var request = Http.CreateRequest("POST", $"{baseUrl}/api/v1/orders")
                                  .WithHeader("Accept", "application/json")
                                  .WithHeader("Authorization", $"Bearer {tokenString}")
                                  .WithBody(new StringContent(jsonBody, Encoding.UTF8, "application/json"));

                return await Http.Send(httpClient, request);
            });
            return step;
        })
        .WithWarmUpDuration(TimeSpan.FromSeconds(0))
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 50, during: TimeSpan.FromSeconds(5))
        );

        // Register and run scenarios
        Console.WriteLine("\nAvailable scenarios to run:");
        Console.WriteLine("1. public_read (Fetch stores, constant rate for 10s)");
        Console.WriteLine("2. place_order (50 orders fired at the exact same instant)");
        Console.WriteLine("3. otp_concurrency (Attack OTP guess-limit and double-consume)");
        Console.WriteLine("4. refresh_token_concurrency (Attack refresh token rotation)");
        Console.WriteLine("5. place_order_idempotency (Attack place-order idempotency)");
        Console.WriteLine("6. deadlock_concurrency (Attack consistent lock ordering / induce deadlock)");
        Console.WriteLine("7. custom_three_customer_flow (Custom flow: A -> C -> B with overlapping products)");
        Console.WriteLine("Specify target scenario index (1-7) as an argument to run a specific test (defaults to 1):\n");

        var selection = "1";
        if (args.Length > 0)
        {
            selection = args[0];
        }

        switch (selection)
        {
            case "7":
                Console.WriteLine("Starting CUSTOM THREE-CUSTOMER FLOW test (Incident #1)...");
                await RunThreeCustomerFlowTest(baseUrl, httpClient, jwtKey, jwtIssuer, jwtAudience, dbOptions);
                break;
            case "6":
                Console.WriteLine("Starting DEADLOCK CONCURRENCY tests (Incident #6)...");
                await RunDeadlockConcurrencyTest(baseUrl, httpClient, customer, address.Id, tokenString, dbOptions);
                break;
            case "5":
                Console.WriteLine("Starting PLACE ORDER IDEMPOTENCY tests (Incident #5)...");
                await RunIdempotencyConcurrencyTest(baseUrl, httpClient, customer, product, tokenString, dbOptions);
                break;
            case "4":
                Console.WriteLine("Starting REFRESH TOKEN CONCURRENCY tests (Incident #4)...");
                await RunRefreshTokenConcurrencyTest(baseUrl, httpClient, customer.UserId, tokenString, dbOptions);
                break;
            case "3":
                Console.WriteLine("Starting OTP CONCURRENCY tests (Incident #3)...");
                await RunOtpConcurrencyTest(baseUrl, httpClient);
                break;
            case "2":
                Console.WriteLine($"Starting PLACE ORDER concurrency test on Product ID {product.Id} (Incident #1 / #5)...");
                NBomberRunner.RegisterScenarios(placeOrderScenario).Run();
                
                // Let's query final state from the database
                using (var dbCheck = new WasilDbContext(dbOptions))
                {
                    var finalProduct = await dbCheck.Products.FindAsync(product.Id);
                    var ordersCount = await dbCheck.OrderLines.CountAsync(ol => ol.ProductId == product.Id);
                    Console.WriteLine("\n=================================================");
                    Console.WriteLine(" POST-EXPERIMENT DATABASE STATE REPORT");
                    Console.WriteLine("=================================================");
                    Console.WriteLine($"Product Name       : {finalProduct?.Name}");
                    Console.WriteLine($"Initial Stock      : {product.StockQuantity}");
                    Console.WriteLine($"Final Stock Quantity: {finalProduct?.StockQuantity}");
                    Console.WriteLine($"Successful Orders  : {ordersCount}");
                    Console.WriteLine("=================================================\n");
                }
                break;
            case "1":
            default:
                Console.WriteLine("Starting PUBLIC READ baseline test...");
                NBomberRunner.RegisterScenarios(fetchStoresScenario).Run();
                break;
        }
    }

    private static async Task RunOtpConcurrencyTest(string baseUrl, HttpClient httpClient)
    {
        var testPhone = "9991234567";

        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING OTP GUESS-LIMIT BYPASS TEST");
        Console.WriteLine("=================================================");

        // 1. Request fresh OTP
        var requestOtpRes = await httpClient.PostAsJsonAsync($"{baseUrl}/api/auth/customer/request-otp", new { phone = testPhone });
        if (!requestOtpRes.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error: Failed to request OTP. Status: {requestOtpRes.StatusCode}");
            return;
        }
        Console.WriteLine("OTP requested successfully. Default code is '123456'.");

        // 2. Fire 30 concurrent WRONG guesses
        int wrongGuessesCount = 30;
        var wrongGuessTasks = Enumerable.Range(0, wrongGuessesCount).Select(async i =>
        {
            try
            {
                var res = await httpClient.PostAsJsonAsync($"{baseUrl}/api/auth/customer/verify-otp", new
                {
                    phone = testPhone,
                    code = "999999" // Incorrect code
                });
                return res.StatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Request failed: {ex.Message}");
                return System.Net.HttpStatusCode.InternalServerError;
            }
        }).ToList();

        var wrongGuessResults = await Task.WhenAll(wrongGuessTasks);

        int unauthorizedCount = wrongGuessResults.Count(s => s == System.Net.HttpStatusCode.Unauthorized);
        int tooManyRequestsCount = wrongGuessResults.Count(s => (int)s == 429);
        int otherErrors = wrongGuessResults.Count(s => s != System.Net.HttpStatusCode.Unauthorized && (int)s != 429);

        Console.WriteLine($"Total wrong guesses sent : {wrongGuessesCount}");
        Console.WriteLine($"401 Unauthorized (Checked) : {unauthorizedCount}");
        Console.WriteLine($"429 Too Many Attempts    : {tooManyRequestsCount}");
        Console.WriteLine($"Other Status Codes       : {otherErrors}");
        Console.WriteLine("-------------------------------------------------");
        if (unauthorizedCount > 3)
        {
            Console.WriteLine("VULNERABLE: Guess-limit bypass detected! More than 3 wrong attempts were checked.");
        }
        else
        {
            Console.WriteLine("SECURE: Guess-limit enforced successfully. Exactly 3 wrong attempts were checked.");
        }

        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING OTP DOUBLE-CONSUME TEST");
        Console.WriteLine("=================================================");

        var doubleConsumePhone = "9998888888";

        // Request fresh OTP for new phone number
        var req2 = await httpClient.PostAsJsonAsync($"{baseUrl}/api/auth/customer/request-otp", new { phone = doubleConsumePhone });
        if (!req2.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error: Failed to request OTP. Status: {req2.StatusCode}");
            return;
        }
        Console.WriteLine("OTP requested successfully for double-consume test.");

        // 2. Fire 5 concurrent CORRECT verification requests
        int correctAttemptsCount = 5;
        var correctTasks = Enumerable.Range(0, correctAttemptsCount).Select(async i =>
        {
            try
            {
                var res = await httpClient.PostAsJsonAsync($"{baseUrl}/api/auth/customer/verify-otp", new
                {
                    phone = doubleConsumePhone,
                    code = "123456" // Correct code
                });
                return res.StatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Request failed: {ex.Message}");
                return System.Net.HttpStatusCode.InternalServerError;
            }
        }).ToList();

        var correctResults = await Task.WhenAll(correctTasks);

        int successCount = correctResults.Count(s => s == System.Net.HttpStatusCode.OK);
        int failedCount = correctResults.Count(s => s != System.Net.HttpStatusCode.OK);

        Console.WriteLine($"Total correct requests sent: {correctAttemptsCount}");
        Console.WriteLine($"200 OK (Successful logins): {successCount}");
        Console.WriteLine($"Failed / Blocked logins    : {failedCount}");
        Console.WriteLine("-------------------------------------------------");
        if (successCount > 1)
        {
            Console.WriteLine("VULNERABLE: Double-consume bypass detected! More than 1 login succeeded.");
        }
        else
        {
            Console.WriteLine("SECURE: Double-consume prevented. Exactly 1 login succeeded.");
        }
        Console.WriteLine("=================================================\n");
    }

    private static async Task RunRefreshTokenConcurrencyTest(string baseUrl, HttpClient httpClient, Guid userId, string tokenString, DbContextOptions<WasilDbContext> dbOptions)
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING REFRESH-TOKEN ROTATION CONCURRENCY TEST");
        Console.WriteLine("=================================================");

        // 1. Generate and seed a new active RefreshToken directly in the DB
        var tokenValue = Guid.NewGuid().ToString("N");
        using (var db = new WasilDbContext(dbOptions))
        {
            var testRefreshToken = new RefreshToken
            {
                Token = tokenValue,
                UserId = userId,
                CreatedOn = DateTime.UtcNow,
                ExpiresOn = DateTime.UtcNow.AddMinutes(30)
            };
            db.RefreshTokens.Add(testRefreshToken);
            await db.SaveChangesAsync();
        }
        Console.WriteLine($"Seeded active RefreshToken: {tokenValue}");

        // 2. Fire 2 concurrent refresh requests
        var refreshPayload = new
        {
            token = tokenString,
            refreshToken = tokenValue
        };

        Console.WriteLine("Firing 2 concurrent refresh requests...");
        var tasks = Enumerable.Range(0, 2).Select(async i =>
        {
            try
            {
                var res = await httpClient.PostAsJsonAsync($"{baseUrl}/api/auth/refresh", refreshPayload);
                var content = await res.Content.ReadAsStringAsync();
                return new { StatusCode = res.StatusCode, Content = content };
            }
            catch (Exception ex)
            {
                return new { StatusCode = System.Net.HttpStatusCode.InternalServerError, Content = ex.Message };
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);

        int successCount = results.Count(r => r.StatusCode == System.Net.HttpStatusCode.OK);
        int unauthorizedCount = results.Count(r => r.StatusCode == System.Net.HttpStatusCode.Unauthorized);

        Console.WriteLine("\n--- RESPONSES ---");
        for (int i = 0; i < results.Length; i++)
        {
            Console.WriteLine($"Request {i + 1}: Status = {results[i].StatusCode}, Response = {results[i].Content}");
        }
        Console.WriteLine("-----------------");

        // 3. Query final database state
        using (var dbCheck = new WasilDbContext(dbOptions))
        {
            var activeTokens = await dbCheck.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedOn == null)
                .ToListAsync();

            Console.WriteLine($"Active RefreshTokens in DB: {activeTokens.Count}");
            foreach (var t in activeTokens)
            {
                Console.WriteLine($" - Active Token: {t.Token} (Expires: {t.ExpiresOn})");
            }
            Console.WriteLine("-------------------------------------------------");

            if (successCount == 2 && activeTokens.Count == 2)
            {
                Console.WriteLine("VULNERABLE: Double-issue detected! Both requests succeeded, creating two separate active token chains.");
            }
            else if (successCount == 1 && activeTokens.Count == 0)
            {
                Console.WriteLine("VULNERABLE: Misfired revocation! One request succeeded, but the concurrent request triggered reuse detection and revoked all sessions.");
            }
            else if (successCount == 2 && activeTokens.Count == 1)
            {
                Console.WriteLine("SECURE: Grace period handled concurrent refresh! Both succeeded but only 1 active token exists in DB.");
            }
            else
            {
                Console.WriteLine("REPORT: Concurrency test completed. Check logic.");
            }
        }
        Console.WriteLine("=================================================\n");
    }

    private static async Task RunIdempotencyConcurrencyTest(string baseUrl, HttpClient httpClient, Customer customer, Product product, string tokenString, DbContextOptions<WasilDbContext> dbOptions)
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING IDEMPOTENCY CONCURRENCY TEST");
        Console.WriteLine("=================================================");

        var idempotencyKey = Guid.NewGuid().ToString();

        int addressId = 1;
        using (var db = new WasilDbContext(dbOptions))
        {
            // Clear prior orders for clean test output
            var priorOrders = await db.Orders.Where(o => o.CustomerId == customer.Id).ToListAsync();
            foreach (var o in priorOrders)
            {
                var lines = await db.OrderLines.Where(ol => ol.OrderId == o.Id).ToListAsync();
                db.OrderLines.RemoveRange(lines);
                var hist = await db.OrderStatusHistories.Where(h => h.OrderId == o.Id).ToListAsync();
                db.OrderStatusHistories.RemoveRange(hist);
            }
            db.Orders.RemoveRange(priorOrders);
            await db.SaveChangesAsync();
            var address = await db.Addresses.FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
            if (address == null)
            {
                address = new Address
                {
                    CustomerId = customer.Id,
                    Street = "Test Street 1",
                    City = "Ramallah",
                    ZipCode = "12345",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Addresses.Add(address);
                await db.SaveChangesAsync();
            }
            addressId = address.Id;
        }
        Console.WriteLine($"Using valid AddressId: {addressId} for Customer ID: {customer.Id}");

        // 1. Save that exact request body
        var requestPayload = new
        {
            customerId = customer.Id,
            storeId = product.StoreId,
            addressId = addressId,
            paymentMethod = 0, // Cash
            lines = new[]
            {
                new { productId = product.Id, quantity = 1 }
            }
        };

        // 2. Fire 10 concurrent requests at the exact same instant using that key
        int concurrentRequestsCount = 10;
        Console.WriteLine($"Firing {concurrentRequestsCount} concurrent Place Order requests with Idempotency-Key: {idempotencyKey}...");

        var tasks = Enumerable.Range(0, concurrentRequestsCount).Select(async i =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenString);
                request.Headers.Add("Idempotency-Key", idempotencyKey);
                request.Content = JsonContent.Create(requestPayload);

                var res = await httpClient.SendAsync(request);
                var content = await res.Content.ReadAsStringAsync();
                return new { StatusCode = res.StatusCode, Content = content };
            }
            catch (Exception ex)
            {
                return new { StatusCode = System.Net.HttpStatusCode.InternalServerError, Content = ex.Message };
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);

        int successCount = results.Count(r => r.StatusCode == System.Net.HttpStatusCode.Created || r.StatusCode == System.Net.HttpStatusCode.OK);
        int conflictCount = results.Count(r => r.StatusCode == System.Net.HttpStatusCode.Conflict);
        int otherCount = results.Count(r => r.StatusCode != System.Net.HttpStatusCode.Created && r.StatusCode != System.Net.HttpStatusCode.OK && r.StatusCode != System.Net.HttpStatusCode.Conflict);

        Console.WriteLine("\n--- CONCURRENT RESPONSES ---");
        for (int i = 0; i < results.Length; i++)
        {
            Console.WriteLine($"Request {i + 1}: Status = {results[i].StatusCode}, Response = {results[i].Content}");
        }
        Console.WriteLine("----------------------------");

        // 3. Separately, send it once again a few seconds later
        Console.WriteLine("Waiting 3 seconds before sending delayed 11th retry request...");
        await Task.Delay(3000);

        Console.WriteLine("Sending delayed 11th retry request...");
        string delayedResponseContent = "";
        System.Net.HttpStatusCode delayedStatus = System.Net.HttpStatusCode.InternalServerError;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenString);
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Content = JsonContent.Create(requestPayload);

            var res = await httpClient.SendAsync(request);
            delayedStatus = res.StatusCode;
            delayedResponseContent = await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Delayed request failed: {ex.Message}");
        }

        Console.WriteLine($"Delayed Request: Status = {delayedStatus}, ResponseLength = {delayedResponseContent.Length}");

        // 4. Query DB state to count orders created
        using (var dbCheck = new WasilDbContext(dbOptions))
        {
            var orders = await dbCheck.Orders
                .Where(o => o.CustomerId == customer.Id && o.StoreId == product.StoreId)
                .ToListAsync();

            Console.WriteLine($"\nOrders in Database for Customer: {orders.Count}");
            foreach (var o in orders)
            {
                Console.WriteLine($" - Order ID: {o.Id}, Code: {o.OrderCode}, Status: {o.Status}");
            }
            Console.WriteLine("-------------------------------------------------");

            if (orders.Count > 1)
            {
                Console.WriteLine($"VULNERABLE: Idempotency failed! Created {orders.Count} duplicate orders for the same request.");
            }
            else if (orders.Count == 1 && successCount + (delayedStatus == System.Net.HttpStatusCode.Created || delayedStatus == System.Net.HttpStatusCode.OK ? 1 : 0) == 11)
            {
                Console.WriteLine("SECURE: Idempotency enforced! Exactly 1 order created, and all 11 requests received success responses.");
            }
            else
            {
                Console.WriteLine("REPORT: Idempotency run completed. Check details.");
            }
        }
        Console.WriteLine("=================================================\n");
    }

    private static async Task RunDeadlockConcurrencyTest(string baseUrl, HttpClient httpClient, Customer customer, int addressId, string tokenString, DbContextOptions<WasilDbContext> dbOptions)
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING DEADLOCK CONCURRENCY TEST");
        Console.WriteLine("=================================================");

        Product product1, product2;
        using (var db = new WasilDbContext(dbOptions))
        {
            var store = await db.Stores.FirstOrDefaultAsync();
            if (store == null)
            {
                Console.WriteLine("Error: No store found.");
                return;
            }

            var products = await db.Products.Where(p => p.StoreId == store.Id && !p.IsDeleted).Take(2).ToListAsync();
            while (products.Count < 2)
            {
                var newP = new Product
                {
                    StoreId = store.Id,
                    Name = $"Deadlock Test Product {products.Count + 1}",
                    Price = 10.00m,
                    StockQuantity = 1000,
                    Availability = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Products.Add(newP);
                await db.SaveChangesAsync();
                products = await db.Products.Where(p => p.StoreId == store.Id && !p.IsDeleted).Take(2).ToListAsync();
            }

            product1 = products[0];
            product2 = products[1];

            // Reset stock quantities
            product1.StockQuantity = 1000;
            product2.StockQuantity = 1000;
            db.Entry(product1).State = EntityState.Modified;
            db.Entry(product2).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }

        Console.WriteLine($"Product 1 ID: {product1.Id}, Product 2 ID: {product2.Id}");

        int iterations = 20;
        int failureCount = 0;
        int successCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            Console.WriteLine($"Iteration {i + 1}/{iterations}...");

            var payloadA = new
            {
                customerId = customer.Id,
                storeId = product1.StoreId,
                addressId = addressId,
                paymentMethod = 0, // Cash
                lines = new[]
                {
                    new { productId = product1.Id, quantity = 1 },
                    new { productId = product2.Id, quantity = 1 }
                }
            };

            var payloadB = new
            {
                customerId = customer.Id,
                storeId = product1.StoreId,
                addressId = addressId,
                paymentMethod = 0, // Cash
                lines = new[]
                {
                    new { productId = product2.Id, quantity = 1 },
                    new { productId = product1.Id, quantity = 1 }
                }
            };

            var taskA = Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenString);
                    request.Content = JsonContent.Create(payloadA);
                    var res = await httpClient.SendAsync(request);
                    var content = await res.Content.ReadAsStringAsync();
                    return new { StatusCode = res.StatusCode, Content = content };
                }
                catch (Exception ex)
                {
                    return new { StatusCode = System.Net.HttpStatusCode.InternalServerError, Content = ex.Message };
                }
            });

            var taskB = Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenString);
                    request.Content = JsonContent.Create(payloadB);
                    var res = await httpClient.SendAsync(request);
                    var content = await res.Content.ReadAsStringAsync();
                    return new { StatusCode = res.StatusCode, Content = content };
                }
                catch (Exception ex)
                {
                    return new { StatusCode = System.Net.HttpStatusCode.InternalServerError, Content = ex.Message };
                }
            });

            var results = await Task.WhenAll(taskA, taskB);

            foreach (var r in results)
            {
                if (r.StatusCode == System.Net.HttpStatusCode.Created || r.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                    Console.WriteLine($"Request failed: Status = {r.StatusCode}, Response = {r.Content}");
                }
            }
        }

        Console.WriteLine($"\nDeadlock Test Completed: Successes = {successCount}, Failures/Deadlocks = {failureCount}");
        Console.WriteLine("=================================================\n");
    }

    private static async Task RunThreeCustomerFlowTest(string baseUrl, HttpClient httpClient, string jwtKey, string jwtIssuer, string jwtAudience, DbContextOptions<WasilDbContext> dbOptions)
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine(" RUNNING CUSTOM THREE-CUSTOMER FLOW CONCURRENCY TEST (Incident #1)");
        Console.WriteLine("=================================================");

        using var db = new WasilDbContext(dbOptions);

        // 1. Ensure we have a store
        var store = await db.Stores.FirstOrDefaultAsync();
        if (store == null)
        {
            store = new Store
            {
                StoreName = "Concurrency Test Store",
                StoreLocation = "Test City",
                Status = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Stores.Add(store);
            await db.SaveChangesAsync();
        }

        // 2. Ensure we have a Category
        var category = await db.Categories.FirstOrDefaultAsync();
        if (category == null)
        {
            category = new Category
            {
                Name = "Test Category",
                Description = "Category for tests",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        // 3. Ensure we have 5 specific products in this store
        var products = new List<Product>();
        for (int i = 1; i <= 5; i++)
        {
            var pName = $"Concurrency Product {i}";
            var p = await db.Products.FirstOrDefaultAsync(p => p.StoreId == store.Id && p.Name == pName);
            if (p == null)
            {
                p = new Product
                {
                    StoreId = store.Id,
                    CategoryId = category.Id,
                    Name = pName,
                    Price = 10.00m + i,
                    StockQuantity = i == 1 || i == 4 ? 2 : 5, // Product 1 and 4 have 2 stock. Others have 5.
                    Availability = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Products.Add(p);
            }
            else
            {
                // Reset stock for clean test runs
                p.StockQuantity = i == 1 || i == 4 ? 2 : 5;
                p.Availability = true;
                db.Entry(p).State = EntityState.Modified;
            }
            products.Add(p);
        }
        await db.SaveChangesAsync();

        // 4. Ensure we have 3 customers (Customer A, Customer B, Customer C) and users
        var customers = new List<Customer>();
        var names = new[] { ("Customer", "A"), ("Customer", "B"), ("Customer", "C") };
        foreach (var (first, last) in names)
        {
            var email = $"customer.{last.ToLower()}@concurrency.test";
            var phone = $"999000000{last}";
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email,
                    Phone = phone,
                    PasswordHash = "AQAAAAEAACcQAAAAEP...", // Dummy hash
                    Role = Role.Customer
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(); // save user to get ID
            }

            var customer = await db.Customers.FirstOrDefaultAsync(c => c.UserId == user.Id);
            if (customer == null)
            {
                customer = new Customer
                {
                    UserId = user.Id,
                    FirstName = first,
                    LastName = last,
                    Email = email,
                    PhoneNumber = phone,
                    Gender = "Other",
                    Location = "Concurrency City"
                };
                db.Customers.Add(customer);
                await db.SaveChangesAsync();
            }

            // Ensure address exists
            var address = await db.Addresses.FirstOrDefaultAsync(a => a.CustomerId == customer.Id);
            if (address == null)
            {
                address = new Address
                {
                    CustomerId = customer.Id,
                    Street = "Concurrency St",
                    City = "Concurrency City",
                    ZipCode = "12345",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Addresses.Add(address);
                await db.SaveChangesAsync();
            }

            customers.Add(customer);
        }

        var custA = customers[0]; // Customer A
        var custB = customers[1]; // Customer B
        var custC = customers[2]; // Customer C

        // Generate tokens
        var tokenA = GenerateTokenString(custA.UserId, jwtKey, jwtIssuer, jwtAudience);
        var tokenB = GenerateTokenString(custB.UserId, jwtKey, jwtIssuer, jwtAudience);
        var tokenC = GenerateTokenString(custC.UserId, jwtKey, jwtIssuer, jwtAudience);

        // Fetch addresses for orders
        var addrA = await db.Addresses.FirstAsync(a => a.CustomerId == custA.Id);
        var addrB = await db.Addresses.FirstAsync(a => a.CustomerId == custB.Id);
        var addrC = await db.Addresses.FirstAsync(a => a.CustomerId == custC.Id);

        // Define product IDs for mapping
        var p1 = products[0];
        var p2 = products[1];
        var p3 = products[2];
        var p4 = products[3];
        var p5 = products[4];

        // Warm up API server and EF Core context queries to eliminate JIT cold-start latency
        Console.WriteLine("Warming up API server and EF Core queries...");
        try
        {
            // Perform a quick read request to initialize the EF Core model cache
            await httpClient.GetAsync($"{baseUrl}/api/v1/stores?page=1&pageSize=1");
            // Also hit the order endpoint with an empty request to JIT compile the post route
            using var warmupContent = new StringContent("{}", Encoding.UTF8, "application/json");
            await httpClient.PostAsync($"{baseUrl}/api/v1/orders", warmupContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warm-up warning: {ex.Message}");
        }
        Console.WriteLine("Warm-up complete. Starting test run.");

        // Output visual setup before test
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    EXPERIMENT LAYOUT & INITIAL STATE                      ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Store ID: {store.Id,-63} ║");
        Console.WriteLine("║                                                                           ║");
        Console.WriteLine("║ [INITIAL STOCKS]                                                          ║");
        Console.WriteLine($"║   - Product 1 ({p1.Name,-21} ID: {p1.Id,-6}): Stock = {p1.StockQuantity,-2}                     ║");
        Console.WriteLine($"║   - Product 2 ({p2.Name,-21} ID: {p2.Id,-6}): Stock = {p2.StockQuantity,-2}                     ║");
        Console.WriteLine($"║   - Product 3 ({p3.Name,-21} ID: {p3.Id,-6}): Stock = {p3.StockQuantity,-2}                     ║");
        Console.WriteLine($"║   - Product 4 ({p4.Name,-21} ID: {p4.Id,-6}): Stock = {p4.StockQuantity,-2}                     ║");
        Console.WriteLine($"║   - Product 5 ({p5.Name,-21} ID: {p5.Id,-6}): Stock = {p5.StockQuantity,-2}                     ║");
        Console.WriteLine("║                                                                           ║");
        Console.WriteLine("║ [CUSTOMERS & DEMANDS]                                                     ║");
        Console.WriteLine($"║   1. Customer A (ID: {custA.Id,-6}) demands: Product 1, Product 2, Product 3      ║");
        Console.WriteLine($"║   2. Customer C (ID: {custC.Id,-6}) demands: Product 1, Product 4                 ║");
        Console.WriteLine($"║   3. Customer B (ID: {custB.Id,-6}) demands: Product 4, Product 5, Product 1      ║");
        Console.WriteLine("║                                                                           ║");
        Console.WriteLine("║ [FLOW TIMELINE]                                                           ║");
        Console.WriteLine("║   [Customer A] ────────► Send Order Request                               ║");
        Console.WriteLine("║       │                                                                   ║");
        Console.WriteLine("║       ▼ (delay 100ms)                                                     ║");
        Console.WriteLine("║   [Customer C] ────────► Send Order Request                               ║");
        Console.WriteLine("║       │                                                                   ║");
        Console.WriteLine("║       ▼ (delay 100ms)                                                     ║");
        Console.WriteLine("║   [Customer B] ────────► Send Order Request                               ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════╝\n");

        // Payloads
        var payloadA = new
        {
            customerId = custA.Id,
            storeId = store.Id,
            addressId = addrA.Id,
            paymentMethod = 0,
            lines = new[]
            {
                new { productId = p1.Id, quantity = 1 },
                new { productId = p2.Id, quantity = 1 },
                new { productId = p3.Id, quantity = 1 }
            }
        };

        var payloadC = new
        {
            customerId = custC.Id,
            storeId = store.Id,
            addressId = addrC.Id,
            paymentMethod = 0,
            lines = new[]
            {
                new { productId = p1.Id, quantity = 1 },
                new { productId = p4.Id, quantity = 1 }
            }
        };

        var payloadB = new
        {
            customerId = custB.Id,
            storeId = store.Id,
            addressId = addrB.Id,
            paymentMethod = 0,
            lines = new[]
            {
                new { productId = p4.Id, quantity = 1 },
                new { productId = p5.Id, quantity = 1 },
                new { productId = p1.Id, quantity = 1 }
            }
        };

        Console.WriteLine("Executing flow sequentially with 100ms dispatch delay...");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Dispatch Customer A
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Dispatching Order for Customer A...");
        var taskA = Task.Run(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);
            req.Content = JsonContent.Create(payloadA);
            var res = await httpClient.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            return new { Customer = "Customer A", StatusCode = res.StatusCode, Content = content, Time = stopwatch.ElapsedMilliseconds };
        });

        await Task.Delay(100);

        // Dispatch Customer C
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Dispatching Order for Customer C...");
        var taskC = Task.Run(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenC);
            req.Content = JsonContent.Create(payloadC);
            var res = await httpClient.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            return new { Customer = "Customer C", StatusCode = res.StatusCode, Content = content, Time = stopwatch.ElapsedMilliseconds };
        });

        await Task.Delay(100);

        // Dispatch Customer B
        Console.WriteLine($"[{stopwatch.ElapsedMilliseconds}ms] Dispatching Order for Customer B...");
        var taskB = Task.Run(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/orders");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenB);
            req.Content = JsonContent.Create(payloadB);
            var res = await httpClient.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            return new { Customer = "Customer B", StatusCode = res.StatusCode, Content = content, Time = stopwatch.ElapsedMilliseconds };
        });

        var results = await Task.WhenAll(taskA, taskC, taskB);

        Console.WriteLine("\n--- DISPATCHED RESPONSES ---");
        foreach (var r in results.OrderBy(x => x.Time))
        {
            Console.WriteLine($"[{r.Time}ms] {r.Customer} Response: Status = {r.StatusCode}, Body = {r.Content}");
        }
        Console.WriteLine("----------------------------");

        // 5. Query final state from the database
        using (var dbCheck = new WasilDbContext(dbOptions))
        {
            var finalProducts = await dbCheck.Products.Where(p => p.StoreId == store.Id && p.Name.StartsWith("Concurrency Product")).ToListAsync();
            var finalP1 = finalProducts.FirstOrDefault(p => p.Name == p1.Name);
            var finalP2 = finalProducts.FirstOrDefault(p => p.Name == p2.Name);
            var finalP3 = finalProducts.FirstOrDefault(p => p.Name == p3.Name);
            var finalP4 = finalProducts.FirstOrDefault(p => p.Name == p4.Name);
            var finalP5 = finalProducts.FirstOrDefault(p => p.Name == p5.Name);

            Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                        POST-EXPERIMENT REPORT                             ║");
            Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║ [FINAL STOCKS]                                                            ║");
            Console.WriteLine($"║   - Product 1: Initial = 2, Final Stock = {finalP1?.StockQuantity,-2}                              ║");
            Console.WriteLine($"║   - Product 2: Initial = 5, Final Stock = {finalP2?.StockQuantity,-2}                              ║");
            Console.WriteLine($"║   - Product 3: Initial = 5, Final Stock = {finalP3?.StockQuantity,-2}                              ║");
            Console.WriteLine($"║   - Product 4: Initial = 2, Final Stock = {finalP4?.StockQuantity,-2}                              ║");
            Console.WriteLine($"║   - Product 5: Initial = 5, Final Stock = {finalP5?.StockQuantity,-2}                              ║");
            Console.WriteLine("║                                                                           ║");
            Console.WriteLine("║ [ORDER PROCESSING OUTCOME]                                                ║");
            foreach (var r in results.OrderBy(x => x.Customer))
            {
                var outcome = r.StatusCode == System.Net.HttpStatusCode.Created || r.StatusCode == System.Net.HttpStatusCode.OK
                    ? "SUCCESS (Order Placed)      "
                    : $"FAILED (Error {r.StatusCode})";
                Console.WriteLine($"║   - {r.Customer}: {outcome,-44} ║");
            }
            Console.WriteLine("║                                                                           ║");
            Console.WriteLine("║ [VERDICT / ASSESSMENT]                                                    ║");
            
            bool hasNegativeStock = finalProducts.Any(p => p.StockQuantity < 0);
            int successfulOrdersCount = results.Count(r => r.StatusCode == System.Net.HttpStatusCode.Created || r.StatusCode == System.Net.HttpStatusCode.OK);
            
            if (hasNegativeStock)
            {
                Console.WriteLine("║   CRITICAL: Stock went negative! Database is inconsistent.                 ║");
            }
            else if (successfulOrdersCount == 3)
            {
                Console.WriteLine("║   VULNERABLE: Oversold stock detected! 3 orders succeeded but P1 stock=2.  ║");
            }
            else
            {
                Console.WriteLine("║   SECURE: Concurrency locks successfully serialised the transactions.      ║");
                Console.WriteLine("║   Product stock was correctly decremented and no overselling occurred.     ║");
            }
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════╝\n");
        }
    }

    private static string GenerateTokenString(Guid userId, string jwtKey, string jwtIssuer, string jwtAudience)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, "Customer")
        };
        var jwtToken = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(jwtToken);
    }
}