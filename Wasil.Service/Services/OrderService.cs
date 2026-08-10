using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;
using Wasil.Data.Enums;
using Wasil.Service.Interfaces;
using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;

namespace Wasil.Service.Services;

public class OrderService : IOrderService
{
    private readonly WasilDbContext _dbContext;
    private readonly ILogger<OrderService> _logger;
    
    private static int _orderCounter = 0;
    public static bool ForceCollisionForTesting { get; set; } = 
        Environment.GetEnvironmentVariable("ASPNETCORE_FORCE_COLLISION") == "true";
    private static int _collisionCounter = 0;

    private static readonly Dictionary<OrderStatus, List<OrderStatus>> AllowedTransitions = new()
    {
        { OrderStatus.Pending, new() { OrderStatus.Accepted, OrderStatus.Cancelled } },
        { OrderStatus.Accepted, new() { OrderStatus.Preparing, OrderStatus.Cancelled } },
        { OrderStatus.Preparing, new() { OrderStatus.OutForDelivery } },
        { OrderStatus.OutForDelivery, new() { OrderStatus.Delivered } },
        { OrderStatus.Delivered, new() },
        { OrderStatus.Cancelled, new() }
    };

    private readonly INotificationEngine _notificationEngine;

    public OrderService(WasilDbContext dbContext, ILogger<OrderService> logger, INotificationEngine notificationEngine)
    {
        _dbContext = dbContext;
        _logger = logger;
        _notificationEngine = notificationEngine;
    }

    private string Generate12DigitOrderCode()
    {
        if (ForceCollisionForTesting)
        {
            int val = Interlocked.Increment(ref _collisionCounter);
            if (val <= 2)
            {
                return "2026COLLID99";
            }
        }

        string yyMm = DateTime.UtcNow.ToString("yyMM");

        int randomValue = new Random().Next(0, 100000);
        string middleDigits = randomValue.ToString("D5");

        int nextValue = Interlocked.Increment(ref _orderCounter);
        int counterValue = Math.Abs(nextValue) % 1000;
        string lastThree = counterValue.ToString("D3");

        return $"{yyMm}{middleDigits}{lastThree}";
    }

    public Order PlaceOrder(CreateOrderDto dto)
    {
        _logger.LogInformation("Attempting to place order for Customer {CustomerId} at Store {StoreId}.", dto.CustomerId, dto.StoreId);

        if (dto.Lines == null || !dto.Lines.Any())
        {
            throw new ArgumentException("Order must contain at least one line.");
        }

        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        return executionStrategy.Execute(() =>
        {
            var hasExistingTransaction = _dbContext.Database.CurrentTransaction != null;
            var transaction = hasExistingTransaction ? null : _dbContext.Database.BeginTransaction();
            try
            {
                var customerExists = _dbContext.Customers.Any(c => c.Id == dto.CustomerId);
                if (!customerExists)
                    throw new ArgumentException($"Customer with ID {dto.CustomerId} does not exist.");

                var storeExists = _dbContext.Stores.Any(s => s.Id == dto.StoreId);
                if (!storeExists)
                    throw new ArgumentException($"Store with ID {dto.StoreId} does not exist.");

                var address = _dbContext.Addresses.FirstOrDefault(a => a.Id == dto.AddressId && a.CustomerId == dto.CustomerId);
                if (address == null)
                    throw new ArgumentException($"Address with ID {dto.AddressId} does not belong to Customer {dto.CustomerId}.");

                var productIds = dto.Lines.Select(l => l.ProductId).Distinct().ToList();
                var sortedProductIds = productIds.OrderBy(id => id).ToList();
                var products = _dbContext.Products
                    .FromSqlRaw($"SELECT * FROM Product WITH (UPDLOCK, ROWLOCK) WHERE productId IN ({string.Join(",", sortedProductIds)})")
                    .ToDictionary(p => p.Id);

                decimal subtotal = 0;
                var orderLines = new List<OrderLine>();

                foreach (var lineDto in dto.Lines)
                {
                    if (!products.TryGetValue(lineDto.ProductId, out var product))
                    {
                        throw new ArgumentException($"Product with ID {lineDto.ProductId} does not exist.");
                    }

                    if (product.IsDeleted)
                    {
                        throw new InvalidOperationException($"Product '{product.Name}' is deleted and cannot be ordered.");
                    }

                    if (product.StoreId != dto.StoreId)
                    {
                        throw new ArgumentException($"Product '{product.Name}' (ID {product.Id}) does not belong to Store {dto.StoreId}.");
                    }

                    if (product.StockQuantity < lineDto.Quantity)
                    {
                        throw new InvalidOperationException($"Insufficient stock for product '{product.Name}'. Requested: {lineDto.Quantity}, Available: {product.StockQuantity}.");
                    }

                    product.StockQuantity -= lineDto.Quantity;

                    var lineTotal = product.Price * lineDto.Quantity;
                    subtotal += lineTotal;

                    orderLines.Add(new OrderLine
                    {
                        ProductId = product.Id,
                        ProductName = product.Name ?? "Unknown Product",
                        UnitPrice = product.Price,
                        Quantity = lineDto.Quantity,
                        TotalPrice = lineTotal
                    });
                }

                decimal deliveryFee = 3.00m;
                decimal total = subtotal + deliveryFee;

                string code = string.Empty;
                int maxRetries = 3;
                bool saved = false;
                Order? newOrder = null;

                for (int i = 0; i < maxRetries && !saved; i++)
                {
                    code = Generate12DigitOrderCode();
                    if (newOrder == null)
                    {
                        newOrder = new Order
                        {
                            OrderCode = code,
                            CustomerId = dto.CustomerId,
                            StoreId = dto.StoreId,
                            PaymentMethod = dto.PaymentMethod,
                            Subtotal = subtotal,
                            DeliveryFee = deliveryFee,
                            Total = total,
                            Status = OrderStatus.Pending,
                            OrderLines = orderLines
                        };
                        _dbContext.Orders.Add(newOrder);
                    }
                    else
                    {
                        newOrder.OrderCode = code;
                    }

                    try
                    {
                        _dbContext.SaveChanges();
                        saved = true;
                    }
                    catch (DbUpdateException ex) when (i < maxRetries - 1 && IsUniqueConstraintViolation(ex))
                    {
                        _logger.LogWarning("Order code collision detected for code {OrderCode}. Retrying...", code);
                    }
                }

                if (!saved || newOrder == null)
                {
                    throw new Exception("Failed to generate a unique order code after maximum retries.");
                }

                var history = new OrderStatusHistory
                {
                    OrderId = newOrder.Id,
                    OldStatus = OrderStatus.Pending.ToString(),
                    NewStatus = OrderStatus.Pending.ToString(),
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.OrderStatusHistories.Add(history);
                _dbContext.SaveChanges();

                if (transaction != null)
                {
                    transaction.Commit();
                }
                else
                {
                    _dbContext.SaveChanges();
                }

                _logger.LogInformation("Successfully placed Order ID {OrderId} with Code {OrderCode}.", newOrder.Id, newOrder.OrderCode);
                return newOrder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing order. Transaction rolled back.");
                if (transaction != null)
                {
                    transaction.Rollback();
                }
                throw;
            }
        });
    }

    private bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        }
        return false;
    }

    public OrderDetailDto GetOrderDetails(int orderId)
    {
        var order = _dbContext.Orders
            .Include(o => o.Customer)
                .ThenInclude(c => c.Addresses)
            .Include(o => o.Store)
            .Include(o => o.OrderLines)
            .Include(o => o.StatusHistories)
            .FirstOrDefault(o => o.Id == orderId);

        if (order == null)
            throw new KeyNotFoundException($"Order with ID {orderId} not found.");

        var firstAddress = order.Customer.Addresses.FirstOrDefault();
        string deliveryAddress;
        if (firstAddress != null)
            deliveryAddress = $"{firstAddress.Street}, {firstAddress.City}, {firstAddress.ZipCode}";
        else
            deliveryAddress = "N/A";
        
        return new OrderDetailDto
        {
            Id = order.Id,
            OrderCode = order.OrderCode,
            Status = order.Status.ToString(),
            PaymentMethod = order.PaymentMethod.ToString(),
            Subtotal = order.Subtotal,
            DeliveryFee = order.DeliveryFee,
            Total = order.Total,
            CreatedAtUtc = order.CreatedAtUtc,
            CustomerName = $"{order.Customer.FirstName} {order.Customer.LastName}",
            StoreName = order.Store.StoreName ?? string.Empty,
            DeliveryAddress = deliveryAddress,
            Lines = order.OrderLines.Select(ol => new OrderLineDetailDto
            {
                Id = ol.Id,
                ProductId = ol.ProductId,
                ProductName = ol.ProductName,
                UnitPrice = ol.UnitPrice,
                Quantity = ol.Quantity,
                TotalPrice = ol.TotalPrice
            }).ToList(),
            StatusHistory = order.StatusHistories.Select(sh => new OrderStatusHistoryDto
            {
                OldStatus = sh.OldStatus.ToString(),
                NewStatus = sh.NewStatus.ToString(),
                TimestampUtc = sh.TimestampUtc
            }).ToList()
        };
    }

    public PagedResultDto<CustomerOrderHistoryDto> GetCustomerOrderHistory(int customerId, int page, int pageSize)
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = 10;

        var query = _dbContext.Orders
            .Where(o => o.CustomerId == customerId);

        var totalCount = query.Count();

        var items = query
            .Include(o => o.Store)
            .Include(o => o.OrderLines)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new CustomerOrderHistoryDto
            {
                Id = o.Id,
                OrderCode = o.OrderCode,
                Date = o.CreatedAtUtc,
                Status = o.Status.ToString(),
                StoreName = o.Store.StoreName ?? string.Empty,
                LineCount = o.OrderLines.Count,
                Total = o.Total
            })
            .ToList();

        return new PagedResultDto<CustomerOrderHistoryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public void UpdateOrderStatus(int orderId, OrderStatus newStatus)
    {
        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        executionStrategy.Execute(() =>
        {
            using var transaction = _dbContext.Database.BeginTransaction();
            try
            {
                var order = _dbContext.Orders
                    .Include(o => o.OrderLines)
                    .ThenInclude(ol => ol.Product)
                    .FirstOrDefault(o => o.Id == orderId);

                if (order == null)
                {
                    throw new KeyNotFoundException($"Order with ID {orderId} not found.");
                }

                var currentStatus = order.Status;

                if (!AllowedTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
                {
                    string allowedList;
                    if (allowed != null && allowed.Count > 0)
                    {
                        allowedList = string.Join(", ", allowed.Select(a => a.ToString()));
                    }
                    else
                    {
                        allowedList = "None";
                    }
                    throw new InvalidOperationException($"Illegal order status transition from '{currentStatus}' to '{newStatus}'. Allowed transitions: {allowedList}.");
                }

                if (newStatus == OrderStatus.Cancelled)
                {
                    foreach (var line in order.OrderLines)
                    {
                        if (line.Product != null)
                        {
                            line.Product.StockQuantity += line.Quantity;
                        }
                    }
                }

                order.Status = newStatus;

                var history = new OrderStatusHistory
                {
                    OrderId = order.Id,
                    OldStatus = currentStatus.ToString(),
                    NewStatus = newStatus.ToString(),
                    TimestampUtc = DateTime.UtcNow
                };
                _dbContext.OrderStatusHistories.Add(history);
                _dbContext.SaveChanges();

                transaction.Commit();
                _logger.LogInformation("Order {OrderId} status successfully updated to {Status}.", orderId, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for Order {OrderId}.", orderId);
                transaction.Rollback();
                throw;
            }
        });
    }

    public StoreDashboardDto GetStoreDashboard(int storeId)
    {
        var storeExists = _dbContext.Stores.Any(s => s.Id == storeId);
        if (!storeExists)
        {
            throw new KeyNotFoundException($"Store with ID {storeId} not found.");
        }

        var utcNow = DateTime.UtcNow;
        var todayUtc = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
        var thirtyDaysAgoUtc = utcNow.AddDays(-30);

        var ordersQuery = _dbContext.Orders
            .Where(o => o.StoreId == storeId);

        var todayStats = ordersQuery
            .Where(o => o.CreatedAtUtc >= todayUtc && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => 1)
            .Select(g => new { Count = g.Count(), Revenue = g.Sum(o => o.Total) })
            .FirstOrDefault();

        var last30DaysStats = ordersQuery
            .Where(o => o.CreatedAtUtc >= thirtyDaysAgoUtc && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => 1)
            .Select(g => new { AvgValue = g.Average(o => o.Total) })
            .FirstOrDefault();

        var topProducts = _dbContext.OrderLines
            .Where(ol => ol.Order.StoreId == storeId && ol.Order.CreatedAtUtc >= thirtyDaysAgoUtc && ol.Order.Status != OrderStatus.Cancelled)
            .GroupBy(ol => new { ol.ProductId, ol.ProductName })
            .Select(g => new TopProductDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                QuantitySold = g.Sum(ol => ol.Quantity)
            })
            .OrderByDescending(tp => tp.QuantitySold)
            .Take(5)
            .ToList();

        var activeStatuses = new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.OutForDelivery };
        var activeCounts = ordersQuery
            .Where(o => activeStatuses.Contains(o.Status))
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Status.ToString(), x => x.Count);

        foreach (var status in activeStatuses)
        {
            var key = status.ToString();
            if (!activeCounts.ContainsKey(key))
            {
                activeCounts[key] = 0;
            }
        }

        return new StoreDashboardDto
        {
            TodayOrderCount = todayStats?.Count ?? 0,
            TodayRevenue = todayStats?.Revenue ?? 0,
            AvgOrderValueLast30Days = last30DaysStats?.AvgValue ?? 0,
            TopProductsLast30Days = topProducts,
            ActiveStatusCounts = activeCounts
        };
     }

    public async Task CancelStalePendingOrdersAsync()
{
    var cutoff = DateTime.UtcNow.AddMinutes(-30);
    
    var staleOrderIds = await _dbContext.Orders
        .Where(o => o.Status == OrderStatus.Pending && o.CreatedAtUtc < cutoff)
        .Select(o => o.Id)
        .ToListAsync();

    if (!staleOrderIds.Any())
    {
        return;
    }

    var executionStrategy = _dbContext.Database.CreateExecutionStrategy();

    foreach (var orderId in staleOrderIds)
    {
        await executionStrategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var order = await _dbContext.Orders
                    .FromSqlRaw("SELECT * FROM [Order] WITH (UPDLOCK, ROWLOCK) WHERE orderId = {0}", orderId)
                    .Include(o => o.OrderLines)
                    .ThenInclude(ol => ol.Product)
                    .FirstOrDefaultAsync();

                if (order == null || order.Status != OrderStatus.Pending)
                {
                    await transaction.RollbackAsync();
                    return;
                }

                foreach (var line in order.OrderLines)
                {
                    if (line.Product != null)
                    {
                        line.Product.StockQuantity += line.Quantity;
                    }
                }

                order.Status = OrderStatus.Cancelled;

                var history = new OrderStatusHistory
                {
                    OrderId = order.Id,
                    OldStatus = OrderStatus.Pending.ToString(),
                    NewStatus = OrderStatus.Cancelled.ToString(),
                    TimestampUtc = DateTime.UtcNow
                };
                
                _dbContext.OrderStatusHistories.Add(history);
                await _dbContext.SaveChangesAsync();

                await transaction.CommitAsync();
                _logger.LogInformation("System background job successfully cancelled stale pending Order ID {OrderId}.", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during system background cancellation of Order ID {OrderId}.", orderId);
                await transaction.RollbackAsync();
                
                foreach (var entry in _dbContext.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged))
                {
                    entry.State = EntityState.Detached;
                }
                
                throw;
            }
        });
    }
}

    public async Task GenerateNightlySalesReportAsync(Hangfire.Server.PerformContext? performContext)
    {
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        var startOfYesterday = yesterday;
        var endOfYesterday = yesterday.AddDays(1).AddTicks(-1);

        var stores = await _dbContext.Stores.ToListAsync();

        foreach (var store in stores)
        {
            var storeOrders = await _dbContext.Orders
                .Where(o => o.StoreId == store.Id && o.Status == OrderStatus.Delivered && o.CreatedAtUtc >= startOfYesterday && o.CreatedAtUtc <= endOfYesterday)
                .Include(o => o.OrderLines)
                .ToListAsync();

            var topProduct = storeOrders
                .SelectMany(o => o.OrderLines)
                .GroupBy(ol => ol.ProductName)
                .Select(g => new { Name = g.Key, Quantity = g.Sum(ol => ol.Quantity) })
                .OrderByDescending(x => x.Quantity)
                .FirstOrDefault();

            string topProductName = topProduct?.Name ?? "N/A";

            var report = new DailyReport
            {
                StoreId = store.Id,
                ReportDate = yesterday,
                OrderCount = storeOrders.Count,
                TotalRevenue = storeOrders.Sum(o => o.Total),
                TopSellingProductName = topProductName
            };

            var existingReport = await _dbContext.DailyReports
                .FirstOrDefaultAsync(r => r.StoreId == store.Id && r.ReportDate == yesterday);

            if (existingReport != null)
            {
                existingReport.OrderCount = report.OrderCount;
                existingReport.TotalRevenue = report.TotalRevenue;
                existingReport.TopSellingProductName = report.TopSellingProductName;
            }
            else
            {
                _dbContext.DailyReports.Add(report);
            }
        }

        await _dbContext.SaveChangesAsync();

        if (performContext != null)
        {
            Hangfire.BackgroundJob.ContinueWith<IOrderService>(
                performContext.BackgroundJob.Id,
                x => x.SendStoreReportsForDateAsync(yesterday)
            );
        }
    }

    public async Task SendStoreReportsForDateAsync(DateTime date)
    {
        var reports = await _dbContext.DailyReports
            .Where(r => r.ReportDate.Date == date.Date)
            .ToListAsync();

        foreach (var report in reports)
        {
            var summary = $"Date: {report.ReportDate:yyyy-MM-dd}, Orders: {report.OrderCount}, Revenue: {report.TotalRevenue:C}, Top Product: {report.TopSellingProductName}";
            await _notificationEngine.SendStoreReportAsync(report.StoreId, summary);
        }
    }
}