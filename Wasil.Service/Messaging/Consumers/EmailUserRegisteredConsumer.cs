using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using Wasil.Service.Messaging.Events;

namespace Wasil.Service.Messaging.Consumers;

public class EmailUserRegisteredConsumer : IConsumer<UserRegisteredEvent>
{
    private readonly ILogger<EmailUserRegisteredConsumer> _logger;

    public EmailUserRegisteredConsumer(ILogger<EmailUserRegisteredConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Email service: Sending welcome email to {Email} (User ID: {UserId})", message.Email, message.UserId);
        return Task.CompletedTask;
    }
}
