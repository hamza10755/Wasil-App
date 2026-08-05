namespace Wasil.Data.Interfaces;


public interface IAuthPolicyService
{
    Task<bool> CanAccessCustomerDataAsync(string targetCustomerId);
    Task<bool> CanAccessStoreDataAsync(int targetStoreId);
}