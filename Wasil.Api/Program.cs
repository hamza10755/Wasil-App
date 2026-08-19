using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Wasil.Api.Filters;
using Wasil.Service.Validators;
using Microsoft.EntityFrameworkCore;
using Wasil.Data.Entities;
using Wasil.Data.Interceptors;
using Wasil.Data.Interfaces;
using Wasil.Service.Interfaces;
using Wasil.Service.Services;
using Wasil.Api.Middleware;
using Serilog;
using Hangfire;
using Hangfire.SqlServer;
using Wasil.Service.Messaging;
using Wasil.Service.Messaging.Consumers;
using MassTransit;




var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => 
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidationFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.AddValidatorsFromAssemblyContaining<CreateCustomerDtoValidator>();

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

builder.Services.AddDbContext<WasilDbContext>(options =>
    options.UseSqlServer(
               builder.Configuration.GetConnectionString("DefaultConnection"),
               sqlOptions => sqlOptions.EnableRetryOnFailure())
           .AddInterceptors(new AuditInterceptor()));

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT Audience is not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs/orders"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var cache = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;

            if (jti != null && cache.TryGetValue($"blocklist:{jti}", out _))
            {
                context.Fail("Token has been revoked.");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddSignalR();
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"))); // Match your connection string name

builder.Services.AddHangfireServer();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddMemoryCache();

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IAuthPolicyService, AuthPolicyService>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<INotificationEngine, NotificationEngine>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddHostedService<Wasil.Service.Services.SystemHeartbeatService>();
builder.Services.AddSingleton<RabbitMqConnectionManager>();
builder.Services.AddSingleton<RabbitMqEventPublisher>();
builder.Services.AddSingleton(typeof(Wasil.Service.Messaging.Consumers.IdempotentConsumerWrapper<>));
builder.Services.AddHostedService<AnalyticsConsumerService>();
builder.Services.AddHostedService<ConfirmationWorkerService>();
builder.Services.AddHostedService<Wasil.Service.Services.OutboxRelayService>();
builder.Services.AddHostedService<Wasil.Service.Services.OutboxCleanupService>();
builder.Services.AddHostedService<Wasil.Api.Hubs.OrderStatusNotificationService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<EmailUserRegisteredConsumer>();
    x.AddConsumer<AnalyticsUserRegisteredConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitSection = context.GetRequiredService<IConfiguration>().GetSection("RabbitMQ");
        var host = rabbitSection["HostName"] ?? "localhost";
        var user = rabbitSection["UserName"] ?? "guest";
        var pass = rabbitSection["Password"] ?? "guest";

        cfg.Host(host, "/", h =>
        {
            h.Username(user);
            h.Password(pass);
        });

        cfg.ConfigureEndpoints(context);
    });
});


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WasilDbContext>();
    dbContext.Database.Migrate();
}

// Enable Global Exception Middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

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
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

RecurringJob.AddOrUpdate<IOrderService>(
    "cancel-stale-orders",
    x => x.CancelStalePendingOrdersAsync(),
    "*/5 * * * *"
);

RecurringJob.AddOrUpdate<ITokenService>(
    "hygiene-cleanup",
    x => x.CleanupRefreshTokensAsync(),
    "0 3 * * *"
);

RecurringJob.AddOrUpdate<IOrderService>(
    "nightly-sales-report",
    x => x.GenerateNightlySalesReportAsync(null),
    "0 2 * * *"
);

app.MapGet("/api/test/stores", (WasilDbContext db) => 
    db.Stores.Take(10).ToList());

app.MapGet("/api/test/customers", (WasilDbContext db) => 
    db.Customers.Take(10).ToList());

app.MapGet("/api/test/products", (WasilDbContext db) => 
    db.Products.Take(1000).ToList());

app.MapGet("/api/test/debug-auth/{customerId:int}", async (int customerId, WasilDbContext db, System.Security.Claims.ClaimsPrincipal user) =>
{
    var currentUserId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    var currentUserRole = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
    
    return Results.Ok(new
    {
        TokenUserId = currentUserId,
        TokenRole = currentUserRole,
        CustomerExistsInDb = customer != null,
        CustomerDbUserId = customer?.UserId.ToString(),
        IsMatch = currentUserId != null && currentUserId.Equals(customer?.UserId.ToString(), StringComparison.OrdinalIgnoreCase)
    });
});

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

app.UseWebSockets();

app.Map("/ws/echo", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[1024 * 4];
        var receiveResult = await webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);

        while (!receiveResult.CloseStatus.HasValue)
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer, 0, receiveResult.Count),
                receiveResult.MessageType,
                receiveResult.EndOfMessage,
                System.Threading.CancellationToken.None);

            receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), System.Threading.CancellationToken.None);
        }

        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            System.Threading.CancellationToken.None);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapHub<Wasil.Api.Hubs.OrderHub>("/hubs/orders");

app.MapControllers();

app.Run();
