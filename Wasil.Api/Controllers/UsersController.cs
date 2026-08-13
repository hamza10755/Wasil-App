using System;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Wasil.Service.Messaging.Events;

namespace Wasil.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;

    public UsersController(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser([FromBody] RegisterUserRequest request)
    {
        var userId = Guid.NewGuid();
        
        var registrationEvent = new UserRegisteredEvent(
            UserId: userId,
            Email: request.Email,
            FullName: request.FullName,
            RegisteredAtUtc: DateTime.UtcNow
        );

        await _publishEndpoint.Publish(registrationEvent);

        return Ok(new { Message = "User registered successfully, and event broadcasted.", UserId = userId });
    }
}

public record RegisterUserRequest(string Email, string FullName);
