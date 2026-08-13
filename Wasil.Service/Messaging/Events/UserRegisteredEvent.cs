using System;

namespace Wasil.Service.Messaging.Events;

public record UserRegisteredEvent(
    Guid UserId,
    string Email,
    string FullName,
    DateTime RegisteredAtUtc
);
