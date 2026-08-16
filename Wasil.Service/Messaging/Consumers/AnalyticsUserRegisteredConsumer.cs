using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using Wasil.Service.Messaging.Events;

namespace Wasil.Service.Messaging.Consumers;

public class AnalyticsUserRegisteredConsumer : IConsumer<UserRegisteredEvent>
{
    private readonly ILogger<AnalyticsUserRegisteredConsumer> _logger;

    public AnalyticsUserRegisteredConsumer(ILogger<AnalyticsUserRegisteredConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Analytics service: Logging registration event for {FullName} (User ID: {UserId})", message.FullName, message.UserId);
        return Task.CompletedTask;
    }
}
