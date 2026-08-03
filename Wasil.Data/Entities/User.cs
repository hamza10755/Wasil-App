using Wasil.Data.Enums;

namespace Wasil.Data.Entities;


public class User : BaseEntity
{
    public new Guid Id { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; } 
    public Role Role { get; set; }
    
    public Customer? Customer { get; set; }
    public int? StoreId { get; set; }
    public Store? ManagedStore { get; set; } 
}