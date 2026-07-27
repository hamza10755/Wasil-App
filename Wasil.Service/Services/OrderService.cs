using System;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;
using Wasil.Service.Interfaces;
using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;

namespace Wasil.Service.Services;

public class OrderService : IOrderService
{
    private readonly WasilDbContext _dbContext;
    private readonly ILogger<OrderService> _logger;
    
    private static int _orderCounter = 0;

    public OrderService(WasilDbContext dbContext, ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    private string Generate12DigitOrderCode()
    {
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
        int maxRetries = 3;

        for (int i = 0; i < maxRetries; i++)
        {
            var code = Generate12DigitOrderCode();
            try
            {
                var newOrder = new Order
                {
                    OrderCode = code,
                    StoreId = dto.StoreId,
                    CustomerId = dto.CustomerId
                };

                _dbContext.Orders.Add(newOrder);
                _dbContext.SaveChanges(); 

                _logger.LogInformation("Successfully placed Order ID {OrderId} with Code {OrderCode}.", newOrder.Id, newOrder.OrderCode);
                return newOrder;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Unique constraint collision or error placing order with code {OrderCode}. Attempt {Attempt} of {MaxRetries}.", code, i + 1, maxRetries);
                
                if (i == maxRetries - 1)
                {
                    _logger.LogError("Failed to place order after {MaxRetries} attempts due to database exception.", maxRetries);
                    throw;
                }
                
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new Exception("Failed to generate a unique order code.");
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

        var defaultAddress = order.Customer.Addresses.FirstOrDefault(a => a.IsDefault);
        var deliveryAddress = defaultAddress != null 
            ? $"{defaultAddress.Street}, {defaultAddress.City}, {defaultAddress.ZipCode}" 
            : "N/A";

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
            StoreName = order.Store.StoreName,
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
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

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
                OrderCode = o.OrderCode,
                Date = o.CreatedAtUtc,
                Status = o.Status.ToString(),
                StoreName = o.Store.StoreName,
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
}