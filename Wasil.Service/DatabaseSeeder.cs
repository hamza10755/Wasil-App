using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bogus;
using Wasil.Data;
using Wasil.Data.Entities;
using Wasil.Data.Enums;
using Wasil.Data.Interceptors;
using EFCore.BulkExtensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Wasil.Service.Services;

public class DatabaseSeeder
{
    private readonly WasilDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;

    public DatabaseSeeder(WasilDbContext context, IPasswordHasher<User> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    private List<Store> SeedStores(int count)
    {
        var sw = Stopwatch.StartNew();
        var faker = new Faker<Store>()
            .RuleFor(s => s.StoreName, f => f.Company.CompanyName())
            .RuleFor(s => s.StoreLocation, f => f.Address.City())
            .RuleFor(s => s.Status, f => true)
            .RuleFor(s => s.CreatedAtUtc, f => f.Date.Past(1));

        var stores = faker.Generate(count);
        _context.BulkInsert(stores, new BulkConfig { SetOutputIdentity = true });

        Console.WriteLine($"Seeded {count} Stores in {sw.ElapsedMilliseconds}ms");
        return stores;
    }

    private List<Category> SeedCategories(int count)
    {
        var sw = Stopwatch.StartNew();
        var faker = new Faker<Category>()
            .RuleFor(c => c.Name, f => f.Commerce.Categories(1)[0] + " " + f.UniqueIndex)
            .RuleFor(c => c.Description, f => f.Lorem.Sentence())
            .RuleFor(c => c.CreatedAtUtc, f => f.Date.Past(1));

        var categories = faker.Generate(count);
        categories = categories.GroupBy(c => c.Name).Select(g => g.First()).ToList();

        _context.BulkInsert(categories, new BulkConfig { SetOutputIdentity = true });

        Console.WriteLine($"Seeded {categories.Count} Categories in {sw.ElapsedMilliseconds}ms");
        return categories;
    }

    private void SeedProducts(List<Store> stores, List<Category> categories, int targetCount)
    {
        var sw = Stopwatch.StartNew();
        int productsPerStore = targetCount / stores.Count;
        var allProducts = new List<Product>();

        foreach (var store in stores)
        {
            var productFaker = new Faker<Product>()
                .RuleFor(p => p.Name, f => f.Commerce.ProductName() + " " + f.Random.AlphaNumeric(4))
                .RuleFor(p => p.Price, f => decimal.Parse(f.Commerce.Price(2, 500)))
                .RuleFor(p => p.StockQuantity, f => f.Random.Bool(0.1f) ? 0 : f.Random.Number(1, 150))
                .RuleFor(p => p.StoreId, _ => store.Id)
                .RuleFor(p => p.CategoryId, f => f.PickRandom(categories).Id)
                .RuleFor(p => p.CreatedAtUtc, f => f.Date.Past(1));

            var storeProducts = productFaker.Generate(productsPerStore);
            allProducts.AddRange(storeProducts);

            if (allProducts.Count >= 5000)
            {
                _context.BulkInsert(allProducts);
                allProducts.Clear();
            }
        }

        if (allProducts.Count > 0)
        {
            _context.BulkInsert(allProducts);
        }

        Console.WriteLine($"Seeded Products in {sw.ElapsedMilliseconds}ms");
    }

    private List<Customer> SeedCustomers(int count)
    {
        var sw = Stopwatch.StartNew();
        var allCustomers = new List<Customer>();
        var generatedPhones = new HashSet<string>();
        var faker = new Faker();
        
        int chunkSize = 5000;
        for (int i = 0; i < count; i += chunkSize)
        {
            int currentBatchSize = Math.Min(chunkSize, count - i);
            var usersBatch = new List<User>(currentBatchSize);
            var customersBatch = new List<Customer>(currentBatchSize);

            for (int j = 0; j < currentBatchSize; j++)
            {
                var userId = Guid.NewGuid();
                var firstName = faker.Name.FirstName();
                var lastName = faker.Name.LastName();
                
                string uniquePhone;
                do
                {
                    uniquePhone = "079" + faker.Random.Number(1000000, 9999999);
                } while (!generatedPhones.Add(uniquePhone));

                var uniqueEmail = $"{firstName.ToLower()}_{lastName.ToLower()}_{Guid.NewGuid().ToString().Substring(0, 4)}@example.com";

                var user = new User
                {
                    Id = userId,
                    Phone = uniquePhone,
                    Email = uniqueEmail,
                    Role = Role.Customer,
                    PasswordHash = null,
                    CreatedAtUtc = DateTime.UtcNow
                };
                usersBatch.Add(user);

                var customer = new Customer
                {
                    UserId = userId,
                    FirstName = firstName,
                    LastName = lastName,
                    CreatedAtUtc = DateTime.UtcNow,
                    MarketingNotificationsEnabled = false,
                    Timezone = "UTC"
                };
                customersBatch.Add(customer);
            }

            _context.BulkInsert(usersBatch);
            _context.BulkInsert(customersBatch, new BulkConfig { SetOutputIdentity = true });
            allCustomers.AddRange(customersBatch);
        }

        Console.WriteLine($"Seeded {count} Users and Customers in {sw.ElapsedMilliseconds}ms");
        return allCustomers;
    }

    private List<Address> SeedAddresses(List<Customer> customers)
    {
        var sw = Stopwatch.StartNew();
        var faker = new Faker();
        var addresses = new List<Address>();

        foreach (var customer in customers)
        {
            int count = faker.Random.Number(1, 2);
            for (int k = 0; k < count; k++)
            {
                addresses.Add(new Address
                {
                    CustomerId = customer.Id,
                    Street = faker.Address.StreetAddress(),
                    City = faker.Address.City(),
                    ZipCode = faker.Address.ZipCode(),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        _context.BulkInsert(addresses, new BulkConfig { SetOutputIdentity = true });
        Console.WriteLine($"Seeded {addresses.Count} Addresses in {sw.ElapsedMilliseconds}ms");
        return addresses;
    }

    private void SeedOrdersAndLines(List<Customer> customers, List<Store> stores, int totalOrders)
    {
        var sw = Stopwatch.StartNew();
        var faker = new Faker();

        Console.WriteLine("Loading seeded products for order generation...");
        var products = _context.Products.AsNoTracking().ToList();
        var productsByStore = products.GroupBy(p => p.StoreId).ToDictionary(g => g.Key, g => g.ToList());

        var orderStatuses = new[] 
        { 
            OrderStatus.Delivered, OrderStatus.Delivered, OrderStatus.Delivered, 
            OrderStatus.Delivered, OrderStatus.Delivered, OrderStatus.Delivered, 
            OrderStatus.Cancelled, OrderStatus.Cancelled, OrderStatus.Preparing, OrderStatus.Pending 
        };

        var counters = new Dictionary<string, int>();
        var generatedCodes = new HashSet<string>();
        int batchSize = 25000;
        int seededCount = 0;


        while (seededCount < totalOrders)
        {
            int currentBatch = Math.Min(batchSize, totalOrders - seededCount);
            var orders = new List<Order>(currentBatch);
            var linesMap = new List<List<OrderLine>>(currentBatch);
            var historyMap = new List<List<OrderStatusHistory>>(currentBatch);

            for (int k = 0; k < currentBatch; k++)
            {
                int customerIdx = (int)(Math.Pow(faker.Random.Double(), 2) * customers.Count);
                var customer = customers[customerIdx];

                var store = faker.PickRandom(stores);
                if (!productsByStore.TryGetValue(store.Id, out var storeProducts) || !storeProducts.Any())
                {
                    k--; // Retry
                    continue;
                }

                var orderDate = DateTime.UtcNow.AddDays(-faker.Random.Number(0, 365));
                var first4 = $"{orderDate:yyMM}";
                if (!counters.TryGetValue(first4, out var seq)) seq = 0;
                counters[first4] = (seq + 1) % 1000;

                string orderCode;
                do
                {
                    var middle5 = faker.Random.Number(10000, 99999).ToString("D5");
                    orderCode = $"{first4}{middle5}{seq:D3}";
                } while (!generatedCodes.Add(orderCode));


                var order = new Order
                {
                    CustomerId = customer.Id,
                    StoreId = store.Id,
                    OrderCode = orderCode,
                    Status = faker.PickRandom(orderStatuses),
                    PaymentMethod = faker.Random.Bool(0.7f) ? PaymentMethod.Cash : PaymentMethod.Card,
                    CreatedAtUtc = orderDate,
                    DeliveryFee = 2.50m
                };

                int lineCount = faker.Random.Number(1, 8);
                var selectedProducts = faker.PickRandom(storeProducts, lineCount).Distinct().ToList();
                var orderLines = new List<OrderLine>();
                decimal subtotal = 0;

                foreach (var prod in selectedProducts)
                {
                    int qty = faker.Random.Number(1, 4);
                    var lineTotalPrice = prod.Price * qty;
                    subtotal += lineTotalPrice;

                    orderLines.Add(new OrderLine
                    {
                        ProductId = prod.Id,
                        ProductName = prod.Name,
                        UnitPrice = prod.Price,
                        Quantity = qty,
                        TotalPrice = lineTotalPrice,
                        CreatedAtUtc = orderDate
                    });
                }

                order.Subtotal = subtotal;
                order.Total = subtotal + order.DeliveryFee;

                orders.Add(order);
                linesMap.Add(orderLines);

                var histories = new List<OrderStatusHistory>();
                histories.Add(new OrderStatusHistory
                {
                    OldStatus = "None",
                    NewStatus = OrderStatus.Pending.ToString(),
                    TimestampUtc = orderDate,
                    CreatedAtUtc = orderDate
                });

                if (order.Status == OrderStatus.Cancelled)
                {
                    histories.Add(new OrderStatusHistory
                    {
                        OldStatus = OrderStatus.Pending.ToString(),
                        NewStatus = OrderStatus.Cancelled.ToString(),
                        TimestampUtc = orderDate.AddMinutes(faker.Random.Number(2, 5)),
                        CreatedAtUtc = orderDate
                    });
                }
                else if (order.Status == OrderStatus.Delivered)
                {
                    histories.Add(new OrderStatusHistory
                    {
                        OldStatus = OrderStatus.Pending.ToString(),
                        NewStatus = OrderStatus.Accepted.ToString(),
                        TimestampUtc = orderDate.AddMinutes(5),
                        CreatedAtUtc = orderDate
                    });
                    histories.Add(new OrderStatusHistory
                    {
                        OldStatus = OrderStatus.Accepted.ToString(),
                        NewStatus = OrderStatus.Preparing.ToString(),
                        TimestampUtc = orderDate.AddMinutes(15),
                        CreatedAtUtc = orderDate
                    });
                    histories.Add(new OrderStatusHistory
                    {
                        OldStatus = OrderStatus.Preparing.ToString(),
                        NewStatus = OrderStatus.OutForDelivery.ToString(),
                        TimestampUtc = orderDate.AddMinutes(35),
                        CreatedAtUtc = orderDate
                    });
                    histories.Add(new OrderStatusHistory
                    {
                        OldStatus = OrderStatus.OutForDelivery.ToString(),
                        NewStatus = OrderStatus.Delivered.ToString(),
                        TimestampUtc = orderDate.AddMinutes(50),
                        CreatedAtUtc = orderDate
                    });
                }
                else
                {
                    if (order.Status >= OrderStatus.Accepted)
                    {
                        histories.Add(new OrderStatusHistory
                        {
                            OldStatus = OrderStatus.Pending.ToString(),
                            NewStatus = OrderStatus.Accepted.ToString(),
                            TimestampUtc = orderDate.AddMinutes(5),
                            CreatedAtUtc = orderDate
                        });
                    }
                    if (order.Status >= OrderStatus.Preparing)
                    {
                        histories.Add(new OrderStatusHistory
                        {
                            OldStatus = OrderStatus.Accepted.ToString(),
                            NewStatus = OrderStatus.Preparing.ToString(),
                            TimestampUtc = orderDate.AddMinutes(15),
                            CreatedAtUtc = orderDate
                        });
                    }
                }

                historyMap.Add(histories);
            }

            _context.BulkInsert(orders, new BulkConfig { SetOutputIdentity = true });

            var allLines = new List<OrderLine>();
            var allHistories = new List<OrderStatusHistory>();

            for (int k = 0; k < orders.Count; k++)
            {
                var orderId = orders[k].Id;
                foreach (var line in linesMap[k])
                {
                    line.OrderId = orderId;
                    allLines.Add(line);
                }
                foreach (var history in historyMap[k])
                {
                    history.OrderId = orderId;
                    allHistories.Add(history);
                }
            }

            _context.BulkInsert(allLines);
            _context.BulkInsert(allHistories);

            seededCount += currentBatch;
            Console.WriteLine($"Progress: Seeded {seededCount}/{totalOrders} Orders...");
        }

        Console.WriteLine($"Successfully seeded {totalOrders} Orders & OrderLines in {sw.Elapsed.TotalSeconds:F2} seconds.");
    }

    public void SeedAdminUser()
    {
        if (_context.Users.Any(u => u.Role == Role.Admin))
        {
            return;
        }

        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@wasil.com",
            Role = Role.Admin,
            CreatedAtUtc = DateTime.UtcNow
        };

        admin.PasswordHash = _passwordHasher.HashPassword(admin, "AdminSecurePassword123!");

        _context.Users.Add(admin);
        _context.SaveChanges();
        Console.WriteLine("Seeded Default Admin User (admin@wasil.com).");
    }

    private void ClearDatabase()
    {
        Console.WriteLine("Clearing existing database records...");
        _context.Database.ExecuteSqlRaw("DELETE FROM [DailyReport]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [IdempotentRequests]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [AuditTrail]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [RefreshTokens]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [OrderLineModifier]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [OrderLine]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [OrderStatusHistory]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Order]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [productModifiers]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Product]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Category]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Address]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Customer]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Users]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Break]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Occasion]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [StoreHours]");
        _context.Database.ExecuteSqlRaw("DELETE FROM [Store]");
    }

    public void SeedAll()
    {
        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine("=== Starting Database Seeding ===");

        AuditInterceptor.AuditLoggingEnabled = false;

        try
        {
            ClearDatabase();
            SeedAdminUser();

            var stores = SeedStores(50);
            var categories = SeedCategories(30);
            SeedProducts(stores, categories, 40000);
            var customers = SeedCustomers(20000);
            var addresses = SeedAddresses(customers);
            SeedOrdersAndLines(customers, stores, 300000);
        }
        finally
        {
            AuditInterceptor.AuditLoggingEnabled = true;
            stopwatch.Stop();
            Console.WriteLine($"=== Seeding Completed in {stopwatch.Elapsed.TotalMinutes:F2} minutes ===");
        }
    }
}