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

namespace Wasil.Service.Services;

public class DatabaseSeeder
{
    private readonly WasilDbContext _context;

    public DatabaseSeeder(WasilDbContext context)
    {
        _context = context;
    }

    public void SeedAll()
    {
        var stopwatch = Stopwatch.StartNew();
        Console.WriteLine("=== Starting Database Seeding ===");

        AuditInterceptor.AuditLoggingEnabled = false;

        try
        {
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

    private List<Store> SeedStores(int count)
    {
        var sw = Stopwatch.StartNew();
        var faker = new Faker<Store>()
            .RuleFor(s => s.StoreName, f => f.Company.CompanyName())
            .RuleFor(s => s.StoreLocation, f => f.Address.City())
            .RuleFor(s => s.Status, f => true)
            .RuleFor(s => s.CreatedAtUtc, f => f.Date.Past(1));

        var stores = faker.Generate(count);
        _context.BulkInsert(stores);

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

        _context.BulkInsert(categories);

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
        var customers = new List<Customer>();
        
        int chunkSize = 5000;
        for (int i = 0; i < count; i += chunkSize)
        {
            var faker = new Faker<Customer>()
                .RuleFor(c => c.FirstName, f => f.Name.FirstName())
                .RuleFor(c => c.LastName, f => f.Name.LastName())
                .RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.FirstName, c.LastName + f.UniqueIndex))
                .RuleFor(c => c.PhoneNumber, f => f.Phone.PhoneNumber("079#######"))
                .RuleFor(c => c.CreatedAtUtc, f => f.Date.Past(1));

            var batch = faker.Generate(Math.Min(chunkSize, count - i));
            _context.BulkInsert(batch);
        }

        Console.WriteLine($"Seeded {count} Customers in {sw.ElapsedMilliseconds}ms");
        return customers;
    }
}