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
        try
        {
            var created = _customerService.CreateCustomer(dto);
            return StatusCode(201, created);
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

    [HttpPut("{id:int}")]
    public IActionResult UpdateCustomer(int id, [FromBody] UpdateCustomerDto dto)
    {
        try
        {
            var updated = _customerService.UpdateCustomer(id, dto);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
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

    [HttpPost("{customerId:int}/addresses")]
    public IActionResult AddAddress(int customerId, [FromBody] CreateAddressDto dto)
    {
        try
        {
            var address = _customerService.AddAddress(customerId, dto);
            return StatusCode(201, address);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("{customerId:int}/addresses")]
    public IActionResult ListAddresses(int customerId)
    {
        try
        {
            var addresses = _customerService.ListAddresses(customerId);
            return Ok(addresses);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
