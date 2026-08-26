using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Wasil.Service.DTOs;
using Wasil.Data.Entities;
using Wasil.Data.Interfaces;
using FirebaseAdmin.Messaging;



namespace Wasil.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly WasilDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        WasilDbContext context,
        ICurrentUser currentUser,
        ILogger<NotificationsController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceDto dto)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            _logger.LogWarning("[Notifications] Attempted to register device token without authentication.");
            return Unauthorized(new { message = "Unauthorized access." });
        }

        var existingDeviceToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == dto.Token);

        if (existingDeviceToken != null)
        {
            if (existingDeviceToken.UserId != userId.Value)
            {
                _logger.LogInformation(
                    "[Notifications] Reassigning token {Token} from User {OldUserId} to User {NewUserId} due to shared-device login.",
                    dto.Token, existingDeviceToken.UserId, userId.Value);
                
                existingDeviceToken.UserId = userId.Value;
            }

            existingDeviceToken.Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "web" : dto.Platform;
            existingDeviceToken.LastSeenAtUtc = DateTime.UtcNow;
            existingDeviceToken.IsActive = true;
            existingDeviceToken.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var newDeviceToken = new DeviceToken
            {
                Token = dto.Token,
                UserId = userId.Value,
                Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "web" : dto.Platform,
                RegisteredAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.DeviceTokens.Add(newDeviceToken);
            _logger.LogInformation("[Notifications] Registered new device token for User {UserId}.", userId.Value);
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Device token registered successfully." });
    }

    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceDto dto)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            _logger.LogWarning("[Notifications] Attempted to unregister device token without authentication.");
            return Unauthorized(new { message = "Unauthorized access." });
        }

        var existingDeviceToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == dto.Token && dt.UserId == userId.Value);

        if (existingDeviceToken != null)
        {
            existingDeviceToken.IsActive = false;
            existingDeviceToken.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("[Notifications] Unregistered device token for User {UserId}.", userId.Value);
        }

        return Ok(new { message = "Device token unregistered successfully." });
    }

    [HttpGet("connections/active")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetActiveConnections()
    {
        var activeConnections = await _context.DeviceTokens
            .Where(dt => dt.IsActive)
            .GroupBy(dt => dt.UserId)
            .Select(group => new 
            {
                UserId = group.Key,
                ActiveDeviceCount = group.Count(),
                Platforms = group.Select(dt => dt.Platform).Distinct(),
                LastSeen = group.Max(dt => dt.LastSeenAtUtc)
            })
            .ToListAsync();

        return Ok(new
        {
            TotalActiveUsers = activeConnections.Count,
            TotalActiveDevices = activeConnections.Sum(c => c.ActiveDeviceCount),
            Connections = activeConnections
        });
    }

    [HttpPost("test-send")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestSend([FromBody] TestSendNotificationDto dto)
    {
        try
        {
            var message = new FirebaseAdmin.Messaging.Message
            {
                Token = dto.Token,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = dto.Title,
                    Body = dto.Body
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            _logger.LogInformation("[Notifications] Test push notification successfully sent. Firebase MessageID: {MessageId}", response);

            return Ok(new { message = "Notification sent successfully.", firebaseMessageId = response });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Notifications] Failed to send test push notification.");
            return StatusCode(500, new { message = "Failed to send notification via Firebase.", error = ex.Message });
        }
    }
}
