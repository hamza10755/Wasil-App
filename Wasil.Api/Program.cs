using Microsoft.EntityFrameworkCore;
using Wasil.Data.Entities;
using Wasil.Data.Interceptors;
using Wasil.Service.Interfaces;
using Wasil.Service.Services;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => 
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();

builder.Services.AddDbContext<WasilDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(new AuditInterceptor()));

builder.Services.AddEndpointsApiExplorer();
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<DatabaseSeeder>();

var app = builder.Build();

if (args.Contains("--reseed"))
{
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        seeder.SeedAll();
    }
    return;
}

app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/api/test/stores", (WasilDbContext db) => 
    db.Stores.Take(10).ToList());

app.MapGet("/api/test/customers", (WasilDbContext db) => 
    db.Customers.Take(10).ToList());

app.MapGet("/api/test/products", (WasilDbContext db) => 
    db.Products.Take(1000).ToList());

app.MapDelete("/api/test/customers/{id:int}", (int id, WasilDbContext db) =>
{
    var customer = db.Customers.Find(id);
    if (customer == null) return Results.NotFound($"Customer {id} not found.");
    db.Customers.Remove(customer);
    db.SaveChanges();
    return Results.Ok($"Customer {id} has been soft-deleted. Check the AuditTrail table!");
});

app.MapGet("/api/test/audit", (WasilDbContext db) => 
    db.AuditTrails.OrderByDescending(a => a.TimestampUtc).Take(20).ToList());

app.MapGet("/api/test/counts", (WasilDbContext db) => new
{
    Stores = db.Stores.Count(),
    Customers = db.Customers.Count(),
    Products = db.Products.Count(),
    Categories = db.Categories.Count(),
    Addresses = db.Addresses.Count(),
    OrderStatusHistories = db.OrderStatusHistories.Count(),
    AuditTrails = db.AuditTrails.Count()
});

app.MapControllers();

app.Run();

