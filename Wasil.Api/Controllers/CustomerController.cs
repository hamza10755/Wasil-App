using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Wasil.Service.DTOs;
using Wasil.Service.Interfaces;

namespace Wasil.Api.Controllers;

[Route("api/v1/customers")]
[ApiController]
public class CustomerController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomerController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpPost]
    public IActionResult CreateCustomer([FromBody] CreateCustomerDto dto)
    {
        var created = _customerService.CreateCustomer(dto);
        return StatusCode(201, created);
    }

    [HttpPut("{id:int}")]
    public IActionResult UpdateCustomer(int id, [FromBody] UpdateCustomerDto dto)
    {
        var updated = _customerService.UpdateCustomer(id, dto);
        return Ok(updated);
    }

    [HttpPost("{customerId:int}/addresses")]
    public IActionResult AddAddress(int customerId, [FromBody] CreateAddressDto dto)
    {
        var address = _customerService.AddAddress(customerId, dto);
        return StatusCode(201, address);
    }

    [HttpGet("{customerId:int}/addresses")]
    public IActionResult ListAddresses(int customerId)
    {
        var addresses = _customerService.ListAddresses(customerId);
        return Ok(addresses);
    }
}
