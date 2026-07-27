using System.Collections.Generic;
using Wasil.Service.DTOs;

namespace Wasil.Service.Interfaces;

public interface ICustomerService
{
    CustomerDto CreateCustomer(CreateCustomerDto dto);
    CustomerDto UpdateCustomer(int id, UpdateCustomerDto dto);
    AddressDto AddAddress(int customerId, CreateAddressDto dto);
    IEnumerable<AddressDto> ListAddresses(int customerId);
}
