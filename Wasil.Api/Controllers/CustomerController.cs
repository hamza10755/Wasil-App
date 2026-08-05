using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wasil.Data.Entities;
using Wasil.Service.DTOs;
using Wasil.Service.Interfaces;

namespace Wasil.Api.Controllers;

[Authorize(Roles = "Admin, Customer")]
[Route("api/v1/customers")]
[ApiController]
public class CustomerController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly WasilDbContext _context;
    private readonly IAuthPolicyService _authPolicyService;

    public CustomerController(
        ICustomerService customerService,
        WasilDbContext context,
        IAuthPolicyService authPolicyService)
    {
        _customerService = customerService;
        _context = context;
        _authPolicyService = authPolicyService;
    }

    [HttpPost]
    [AllowAnonymous]
    public IActionResult CreateCustomer([FromBody] CreateCustomerDto dto)
    {
        var created = _customerService.CreateCustomer(dto);
        return StatusCode(201, created);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id, [FromBody] UpdateCustomerDto dto)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return NotFound();

        if (!await _authPolicyService.CanAccessCustomerDataAsync(customer.UserId.ToString()))
        {
            return Forbid();
        }

        var updated = _customerService.UpdateCustomer(id, dto);
        return Ok(updated);
    }

    [HttpPost("{customerId:int}/addresses")]
    public async Task<IActionResult> AddAddress(int customerId, [FromBody] CreateAddressDto dto)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null) return NotFound();

        if (!await _authPolicyService.CanAccessCustomerDataAsync(customer.UserId.ToString()))
        {
            return Forbid();
        }

        var address = _customerService.AddAddress(customerId, dto);
        return StatusCode(201, address);
    }

    [HttpGet("{customerId:int}/addresses")]
    public async Task<IActionResult> ListAddresses(int customerId)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer == null) return NotFound();

        if (!await _authPolicyService.CanAccessCustomerDataAsync(customer.UserId.ToString()))
        {
            return Forbid();
        }

        var addresses = _customerService.ListAddresses(customerId);
        return Ok(addresses);
    }
}
