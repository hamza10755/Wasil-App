using System.Linq;
using Riok.Mapperly.Abstractions;
using Wasil.Data.Entities;
using Wasil.Service.DTOs;

namespace Wasil.Service.Mappers;

[Mapper]
public partial class WasilMapper
{
    public partial AddressDto MapAddressToDto(Address address);

    public partial Customer MapCreateCustomerDtoToCustomer(CreateCustomerDto dto);

    public OrderDetailDto MapOrderToDetailDto(Order order)
    {
        var dto = MapOrderToDetailDtoInternal(order);
        dto.CustomerName = $"{order.Customer?.FirstName} {order.Customer?.LastName}".Trim();
        var firstAddress = order.Customer?.Addresses?.FirstOrDefault();
        dto.DeliveryAddress = firstAddress != null ? $"{firstAddress.Street}, {firstAddress.City}, {firstAddress.ZipCode}" : "N/A";
        return dto;
    }

    [MapProperty(new[] { nameof(Order.Store), nameof(Store.StoreName) }, new[] { nameof(OrderDetailDto.StoreName) })]
    [MapProperty(nameof(Order.OrderLines), nameof(OrderDetailDto.Lines))]
    [MapProperty(nameof(Order.StatusHistories), nameof(OrderDetailDto.StatusHistory))]
    private partial OrderDetailDto MapOrderToDetailDtoInternal(Order order);

    public partial OrderLineDetailDto MapOrderLineToDto(OrderLine orderLine);

    public partial OrderStatusHistoryDto MapStatusHistoryToDto(OrderStatusHistory history);

    private string MapStatus(Wasil.Data.Enums.OrderStatus status) => status.ToString();
    
    private string MapPaymentMethod(Wasil.Data.Enums.PaymentMethod paymentMethod) => paymentMethod.ToString();
}
