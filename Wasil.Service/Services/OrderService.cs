using System;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Wasil.Data.Entities;
using Wasil.Service.Interfaces;
using Wasil.Service.DTOs;

namespace Wasil.Service.Services;

public class OrderService : IOrderService
{
    private readonly WasilDbContext _dbContext;
    
    private static int _orderCounter = 0;

    public OrderService(WasilDbContext dbContext)
    {
        _dbContext = dbContext;
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
        int maxRetries = 3;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var newOrder = new Order
                {
                    OrderCode = Generate12DigitOrderCode(),
                    StoreId = dto.StoreId,
                    CustomerId = dto.CustomerId
                };

                _dbContext.Orders.Add(newOrder);
                
                _dbContext.SaveChanges(); 

                return newOrder;
            }
            catch (DbUpdateException)
            {
                if (i == maxRetries - 1) throw;
                
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new Exception("Failed to generate a unique order code.");
    }
}