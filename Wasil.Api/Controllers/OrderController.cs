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

        var newOrder = _orderService.PlaceOrder(dto);
        return StatusCode(201, newOrder);
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