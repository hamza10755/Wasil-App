using Microsoft.EntityFrameworkCore;
using Wasil.Data;
using Wasil.Data.Entities;
using Wasil.Data.Interfaces;

public interface IAuthPolicyService
{
    Task<bool> CanAccessCustomerDataAsync(string targetCustomerId);
    Task<bool> CanAccessStoreDataAsync(int targetStoreId);
}

public class AuthPolicyService : IAuthPolicyService
{
    private readonly WasilDbContext _context;
    private readonly ICurrentUser _currentUser;

    public AuthPolicyService(WasilDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public Task<bool> CanAccessCustomerDataAsync(string targetCustomerId)
    {
        if (_currentUser.Role == "Admin")
            return Task.FromResult(true);

        if (_currentUser.Role == "Customer")
        {
            return Task.FromResult(_currentUser.UserId?.ToString() == targetCustomerId);
        }

        return Task.FromResult(false);
    }

    public async Task<bool> CanAccessStoreDataAsync(int targetStoreId)
    {
        if (_currentUser.Role == "Admin")
            return true;

        if (_currentUser.Role == "Partner" && _currentUser.UserId.HasValue)
        {
            var freshUser = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id.ToString() == _currentUser.UserId.Value.ToString());

            return freshUser != null && 
                   freshUser.Role.ToString() == "Partner" && 
                   freshUser.StoreId == targetStoreId;
        }

        return false;
    }
}