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
                    CreatedAtUtc = DateTime.UtcNow
                };
                customersBatch.Add(customer);
            }

            _context.BulkInsert(usersBatch);
            
            _context.BulkInsert(customersBatch);
            
            allCustomers.AddRange(customersBatch);
        }

        Console.WriteLine($"Seeded {count} Users and Customers in {sw.ElapsedMilliseconds}ms");
        return allCustomers;
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
        }
        finally
        {
            AuditInterceptor.AuditLoggingEnabled = true;
            stopwatch.Stop();
            Console.WriteLine($"=== Seeding Completed in {stopwatch.Elapsed.TotalMinutes:F2} minutes ===");
        }
    }
}