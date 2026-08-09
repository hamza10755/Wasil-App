using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Wasil.Data.Entities;
using Wasil.Data.Enums;
using Wasil.Service.DTOs;
using Wasil.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Wasil.Api.Controllers;

[Authorize]
[Route("api/v1/orders")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly WasilDbContext _context;
    private readonly IAuthPolicyService _authPolicyService;

    public OrderController(
        IOrderService orderService,
        WasilDbContext context,
        IAuthPolicyService authPolicyService)
    {
        _orderService = orderService;
        _context = context;
        _authPolicyService = authPolicyService;
    }

    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> PlaceOrder([FromBody] CreateOrderDto dto)
    {
        var customer = await _context.Customers.FindAsync(dto.CustomerId);
        if (customer == null) return NotFound();

        if (!await _authPolicyService.CanAccessCustomerDataAsync(customer.UserId.ToString()))
        {
            return Forbid();
        }

        if (Request.Headers.TryGetValue("Idempotency-Key", out var headerValue) && !string.IsNullOrEmpty(headerValue))
        {
            var key = headerValue.ToString();
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync<IActionResult>(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                var existing = await _context.IdempotentRequests
                    .FromSqlRaw("SELECT * FROM IdempotentRequests WITH (UPDLOCK, ROWLOCK) WHERE IdempotencyKey = {0}", key)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    if (existing.ResponseBody != null)
                    {
                        await transaction.CommitAsync();
                        return new ContentResult
                        {
                            Content = existing.ResponseBody,
                            ContentType = "application/json",
                            StatusCode = existing.StatusCode
                        };
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return Conflict(new { message = "Request already in progress." });
                    }
                }

                var placeholder = new IdempotentRequest
                {
                    IdempotencyKey = key,
                    StatusCode = 0,
                    ResponseBody = null,
                    CreatedAtUtc = DateTime.UtcNow
                };

                try
                {
                    _context.IdempotentRequests.Add(placeholder);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync();

                    using var retryTx = await _context.Database.BeginTransactionAsync();
                    var retryResult = await _context.IdempotentRequests
                        .FromSqlRaw("SELECT * FROM IdempotentRequests WITH (UPDLOCK, ROWLOCK) WHERE IdempotencyKey = {0}", key)
                        .FirstOrDefaultAsync();

                    if (retryResult != null && retryResult.ResponseBody != null)
                    {
                        await retryTx.CommitAsync();
                        return new ContentResult
                        {
                            Content = retryResult.ResponseBody,
                            ContentType = "application/json",
                            StatusCode = retryResult.StatusCode
                        };
                    }

                    await retryTx.RollbackAsync();
                    return Conflict(new { message = "Request already in progress." });
                }

                Order newOrder;
                try
                {
                    newOrder = _orderService.PlaceOrder(dto);
                }
                catch (Exception ex)
                {
                    var errBody = System.Text.Json.JsonSerializer.Serialize(new { message = ex.Message });
                    placeholder.StatusCode = 400;
                    placeholder.ResponseBody = errBody;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return BadRequest(new { message = ex.Message });
                }

                var responseBody = System.Text.Json.JsonSerializer.Serialize(newOrder, new System.Text.Json.JsonSerializerOptions
                {
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });

                placeholder.StatusCode = 201;
                placeholder.ResponseBody = responseBody;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return StatusCode(201, newOrder);
            });
        }

        try
        {
            var newOrder = _orderService.PlaceOrder(dto);
            return StatusCode(201, newOrder);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Customer,Partner,Admin")]
    public async Task<IActionResult> GetOrderDetails(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        var isCustomerAllowed = await _authPolicyService.CanAccessCustomerDataAsync(order.Customer.UserId.ToString());
        var isStoreAllowed = await _authPolicyService.CanAccessStoreDataAsync(order.StoreId);

        if (!isCustomerAllowed && !isStoreAllowed)
        {
            return Forbid();
        }

        var details = _orderService.GetOrderDetails(id);
        return Ok(details);
    }

    [HttpGet("customer/{customerId:int}")]
    [Authorize(Roles = "Customer,Admin")]
    public async Task<IActionResult> GetCustomerOrderHistory(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null) return NotFound();

        if (!await _authPolicyService.CanAccessCustomerDataAsync(customer.UserId.ToString()))
        {
            return Forbid();
        }

        var history = _orderService.GetCustomerOrderHistory(customerId, page, pageSize);
        return Ok(history);
    }

    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Partner,Admin")]
    public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null) return NotFound();

        if (!await _authPolicyService.CanAccessStoreDataAsync(order.StoreId))
        {
            return Forbid();
        }

        _orderService.UpdateOrderStatus(id, request.Status);
        return NoContent();
    }

    [HttpGet("dashboard/{storeId:int}")]
    [Authorize(Roles = "Partner,Admin")]
    public async Task<IActionResult> GetStoreDashboard(int storeId)
    {
        if (!await _authPolicyService.CanAccessStoreDataAsync(storeId))
        {
            return Forbid();
        }

        var dashboard = _orderService.GetStoreDashboard(storeId);
        return Ok(dashboard);
    }
}