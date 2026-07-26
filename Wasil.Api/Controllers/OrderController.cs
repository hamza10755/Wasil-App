using Microsoft.AspNetCore.Mvc;
using Wasil.Service.DTOs;
using Wasil.Service.Interfaces;

namespace Wasil.Api.Controllers;

[Route("api/orders")]
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
}