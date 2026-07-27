using Microsoft.AspNetCore.Mvc;
using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;
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
        return Ok(newOrder);
    }

    [HttpGet("{id:int}")]
    public IActionResult GetOrderDetails(int id)
    {
        try
        {
            var details = _orderService.GetOrderDetails(id);
            return Ok(details);
        }
        catch (System.Collections.Generic.KeyNotFoundException)
        {
            return NotFound($"Order with ID {id} not found.");
        }
    }

    [HttpGet("customer/{customerId:int}")]
    public IActionResult GetCustomerOrderHistory(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var history = _orderService.GetCustomerOrderHistory(customerId, page, pageSize);
        return Ok(history);
    }
}