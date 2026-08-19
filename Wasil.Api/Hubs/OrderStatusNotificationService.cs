using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wasil.Api.Hubs;
using Wasil.Service.Messaging;
using Wasil.Service.Messaging.Events;

namespace Wasil.Api.Hubs;

public class OrderStatusNotificationService : BackgroundService
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IHubContext<OrderHub> _hubContext;
    private readonly ILogger<OrderStatusNotificationService> _logger;
    private IModel? _channel;

    public OrderStatusNotificationService(
        RabbitMqConnectionManager connectionManager,
        IHubContext<OrderHub> hubContext,
        ILogger<OrderStatusNotificationService> logger)
    {
        _connectionManager = connectionManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order Status Notification Background Service starting...");

        try
        {
            var connection = _connectionManager.GetConnection();
            _channel = connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: "order.fanout",
                type: ExchangeType.Fanout,
                durable: true
            );

            var queueName = $"order-status-notification-service-{Guid.NewGuid():N}";
            _channel.QueueDeclare(
                queue: queueName,
                durable: false,
                exclusive: true,
                autoDelete: true,
                arguments: null
            );

            _channel.QueueBind(
                queue: queueName,
                exchange: "order.fanout",
                routingKey: string.Empty
            );

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);
                _logger.LogInformation("Notification Service received raw message: {Message}", messageJson);

                try
                {
                    using var doc = JsonDocument.Parse(messageJson);
                    var root = doc.RootElement;
                    var eventName = root.GetProperty("EventName").GetString();

                    if (eventName == "OrderStatusChanged")
                    {
                        var ev = JsonSerializer.Deserialize<OrderStatusChangedEvent>(messageJson);
                        if (ev != null)
                        {
                            _logger.LogInformation("Pushing OrderStatusChanged update for Order {OrderId} (Status: {Status}) to SignalR groups.", ev.OrderId, ev.NewStatus);
                            
                            Task.Run(async () =>
                            {
                                await _hubContext.Clients.Group($"Order_{ev.OrderId}")
                                    .SendAsync("OrderStatusChanged", new { status = ev.NewStatus });

                                await _hubContext.Clients.Group($"Store_{ev.StoreId}")
                                    .SendAsync("OrderStatusChanged", new { orderId = ev.OrderId, status = ev.NewStatus });
                            });
                        }
                    }
                    else if (eventName == "OrderPlaced")
                    {
                        var ev = JsonSerializer.Deserialize<OrderPlacedEvent>(messageJson);
                        if (ev != null)
                        {
                            _logger.LogInformation("Pushing OrderPlaced update for Order {OrderId} to Store group {StoreId} via SignalR.", ev.OrderId, ev.StoreId);

                            Task.Run(async () =>
                            {
                                await _hubContext.Clients.Group($"Store_{ev.StoreId}")
                                    .SendAsync("OrderPlaced", new { orderId = ev.OrderId, status = "Pending" });
                            });
                        }
                    }

                    _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing notification message.");
                    _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicConsume(
                queue: queueName,
                autoAck: false,
                consumer: consumer
            );
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Order Status Notification Service.");
        }

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            _channel.Close();
            _channel.Dispose();
        }

        return base.StopAsync(cancellationToken);
    }
}
