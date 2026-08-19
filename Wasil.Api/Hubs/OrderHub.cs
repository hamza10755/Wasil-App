using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Wasil.Data.Entities;
using Wasil.Data.Interfaces;

namespace Wasil.Api.Hubs;

[Authorize]
public class OrderHub : Hub
{
    private readonly WasilDbContext _context;
    private readonly ICurrentUser _currentUser;

    public OrderHub(WasilDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task WatchOrder(long orderId)
    {
        var subClaim = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                       ?? Context.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (subClaim == null || !Guid.TryParse(subClaim, out var userId))
        {
            return;
        }

        var order = await _context.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order != null && order.Customer != null && order.Customer.UserId == userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Order_{orderId}");
        }
    }

    public async Task WatchStore(long storeId)
    {
        var subClaim = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                       ?? Context.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (subClaim == null || !Guid.TryParse(subClaim, out var userId))
        {
            return;
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user != null && user.Role == Wasil.Data.Enums.Role.Partner && user.StoreId == storeId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Store_{storeId}");
        }
    }
}
