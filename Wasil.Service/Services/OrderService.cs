using System;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;
using Wasil.Service.Interfaces;
using Wasil.Service.DTOs;

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
}