using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FirebaseAdmin.Messaging;
using Wasil.Data.Entities;
using Wasil.Service.Interfaces;

namespace Wasil.Service.Services;

public class NotificationEngine : INotificationEngine
{
    private readonly WasilDbContext _dbContext;
    private readonly ISocketNotificationService _socketService;
    private readonly ILogger<NotificationEngine> _logger;

    public NotificationEngine(
        WasilDbContext dbContext,
        ISocketNotificationService socketService,
        ILogger<NotificationEngine> logger)
    {
        _dbContext = dbContext;
        _socketService = socketService;
        _logger = logger;
    }

    public Task SendStoreReportAsync(int storeId, string reportSummary)
    {
        _logger.LogInformation("Notification Engine: Sent sales report to Store {StoreId}. Summary: {Summary}", storeId, reportSummary);
        return Task.CompletedTask;
    }

    public async Task SendOrderStatusUpdateAsync(long customerId, long orderId, long storeId, string newStatus, bool isMarketing = false)
    {
        var customer = await _dbContext.Customers
            .FirstOrDefaultAsync(c => c.Id == (int)customerId);

        if (customer == null)
        {
            _logger.LogWarning("[Router] Customer {CustomerId} not found for Order {OrderId}. Notification routing aborted.", customerId, orderId);
            return;
        }

        if (isMarketing)
        {
            if (!customer.MarketingNotificationsEnabled)
            {
                _logger.LogInformation("[Router] Marketing notification skipped for User {UserId} because marketing notifications are disabled.", customer.UserId);
                return;
            }

            try
            {
                var customerTimeZone = TimeZoneInfo.FindSystemTimeZoneById(customer.Timezone);
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, customerTimeZone);
                if (localTime.Hour >= 22 || localTime.Hour < 8)
                {
                    _logger.LogInformation("[Router] Marketing notification skipped for User {UserId} due to local quiet hours enforcement ({LocalTime:HH:mm} in timezone {Timezone}).", customer.UserId, localTime, customer.Timezone);
                    return;
                }
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("[Router] Timezone '{Timezone}' is invalid or not found on this system. Skipping quiet hours enforcement.", customer.Timezone);
            }
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var activeConnection = await _dbContext.UserConnections
            .FirstOrDefaultAsync(c => c.UserId == customer.UserId && c.LastSeenAtUtc >= cutoff);

        if (activeConnection != null)
        {
            _logger.LogInformation("[Router] Active SignalR connection found for User {UserId}. Routing status update for Order {OrderId} via WebSockets.", customer.UserId, orderId);
            await _socketService.SendOrderStatusUpdateAsync(orderId, storeId, newStatus);
        }
        else
        {
            _logger.LogInformation("[Router] No active SignalR connection found for User {UserId}. Routing status update for Order {OrderId} via Push Notifications.", customer.UserId, orderId);
            
            var activeTokens = await _dbContext.DeviceTokens
                .Where(dt => dt.UserId == customer.UserId && dt.IsActive)
                .Select(dt => dt.Token)
                .ToListAsync();

            if (!activeTokens.Any())
            {
                _logger.LogInformation("[Router] No active device tokens registered for User {UserId}. Push notification skipped.", customer.UserId);
                return;
            }

            var title = $"Order #{orderId} Status Update";
            var body = $"Your order is now: {newStatus}";

            foreach (var token in activeTokens)
            {
                try
                {
                    var message = new Message
                    {
                        Token = token,
                        Notification = new Notification
                        {
                            Title = title,
                            Body = body
                        }
                    };

                    var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                    _logger.LogInformation("[Router] Push notification successfully sent to token {Token}. Message ID: {MessageId}", token[..Math.Min(10, token.Length)] + "...", response);
                }
                catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered || ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
                {
                    _logger.LogWarning(ex, "[Router] Dead Firebase token detected: {Token}. Deactivating token in database.", token[..Math.Min(10, token.Length)] + "...");
                    
                    var dbToken = await _dbContext.DeviceTokens.FirstOrDefaultAsync(dt => dt.Token == token);
                    if (dbToken != null)
                    {
                        dbToken.IsActive = false;
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Router] Failed to send push notification to token {Token} due to transient error.", token[..Math.Min(10, token.Length)] + "...");
                    throw;
                }
            }
        }
    }
}
