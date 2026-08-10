using System;

namespace Wasil.Data.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Role { get; }
    int? StoreId { get; }
}
