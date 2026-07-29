using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wasil.Data.Entities;
using Wasil.Service.DTOs;
using Wasil.Service.Interfaces;

namespace Wasil.Service.Services;

public class CustomerService : ICustomerService
{
    private readonly WasilDbContext _dbContext;

    public CustomerService(WasilDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public CustomerDto CreateCustomer(CreateCustomerDto dto)
    {
        if (_dbContext.Customers.Any(c => c.Email == dto.Email))
            throw new InvalidOperationException("Email already exists.");

        var customer = new Customer
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            Gender = dto.Gender,
            Location = dto.Location
        };

        _dbContext.Customers.Add(customer);
        _dbContext.SaveChanges();

        return MapToCustomerDto(customer);
    }

    public CustomerDto UpdateCustomer(int id, UpdateCustomerDto dto)
    {
        var customer = _dbContext.Customers.Find(id);
        if (customer == null)
            throw new KeyNotFoundException($"Customer with ID {id} not found.");

        if (_dbContext.Customers.Any(c => c.Email == dto.Email && c.Id != id))
            throw new InvalidOperationException("Email is already used by another customer.");

        customer.FirstName = dto.FirstName;
        customer.LastName = dto.LastName;
        customer.Email = dto.Email;
        customer.PhoneNumber = dto.PhoneNumber;
        customer.Gender = dto.Gender;
        customer.Location = dto.Location;

        _dbContext.SaveChanges();

        return MapToCustomerDto(customer);
    }

    public AddressDto AddAddress(int customerId, CreateAddressDto dto)
    {
        var customerExists = _dbContext.Customers.Any(c => c.Id == customerId);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer with ID {customerId} not found.");

        var address = new Address
        {
            CustomerId = customerId,
            Street = dto.Street,
            City = dto.City,
            ZipCode = dto.ZipCode
        };

        _dbContext.Addresses.Add(address);
        _dbContext.SaveChanges();

        return MapToAddressDto(address);
    }

    public IEnumerable<AddressDto> ListAddresses(int customerId)
    {
        var customerExists = _dbContext.Customers.Any(c => c.Id == customerId);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer with ID {customerId} not found.");

        return _dbContext.Addresses
            .Where(a => a.CustomerId == customerId)
            .Select(MapToAddressDto)
            .ToList();
    }

    private static CustomerDto MapToCustomerDto(Customer c) => new()
    {
        Id = c.Id,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Email = c.Email,
        PhoneNumber = c.PhoneNumber,
        Gender = c.Gender,
        Location = c.Location
    };

    private static AddressDto MapToAddressDto(Address a) => new()
    {
        Id = a.Id,
        CustomerId = a.CustomerId,
        Street = a.Street,
        City = a.City,
        ZipCode = a.ZipCode
    };
}
