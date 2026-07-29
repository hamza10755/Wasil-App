using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Wasil.Data.Enums;
using Wasil.Service.DTOs;
using Wasil.Service.Interfaces;

namespace Wasil.Api.Controllers;

[Route("api/v1/orders")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public IActionResult PlaceOrder([FromBody] CreateOrderDto dto)
    {
        try
        {
            var newOrder = _orderService.PlaceOrder(dto);
            return StatusCode(201, newOrder);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    public IActionResult GetOrderDetails(int id)
    {
        try
        {
            var details = _orderService.GetOrderDetails(id);
            return Ok(details);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("customer/{customerId:int}")]
    public IActionResult GetCustomerOrderHistory(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var history = _orderService.GetCustomerOrderHistory(customerId, page, pageSize);
        return Ok(history);
    }

    [HttpPut("{id:int}/status")]
    public IActionResult UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        try
        {
            _orderService.UpdateOrderStatus(id, request.Status);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("dashboard/{storeId:int}")]
    public IActionResult GetStoreDashboard(int storeId)
    {
        try
        {
            var dashboard = _orderService.GetStoreDashboard(storeId);
            return Ok(dashboard);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}