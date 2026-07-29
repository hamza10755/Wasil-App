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
        var newOrder = _orderService.PlaceOrder(dto);
        return StatusCode(201, newOrder);
    }

    [HttpGet("{id:int}")]
    public IActionResult GetOrderDetails(int id)
    {
        var details = _orderService.GetOrderDetails(id);
        return Ok(details);
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
        _orderService.UpdateOrderStatus(id, request.Status);
        return NoContent();
    }

    [HttpGet("dashboard/{storeId:int}")]
    public IActionResult GetStoreDashboard(int storeId)
    {
        var dashboard = _orderService.GetStoreDashboard(storeId);
        return Ok(dashboard);
    }
}