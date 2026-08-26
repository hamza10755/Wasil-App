using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wasil.Data.Entities;
using Wasil.Service.Interfaces;
using Wasil.Service.Services;
using Wasil.Service.Messaging.Events;

namespace Wasil.Service.Messaging.Consumers;

public class OrderNotificationConsumer : BackgroundService, 
    IIdempotentConsumer<OrderStatusChangedEvent>, 
    IIdempotentConsumer<OrderPlacedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly ILogger<OrderNotificationConsumer> _logger;
    private readonly IdempotentConsumerWrapper<OrderStatusChangedEvent> _orderStatusWrapper;
    private readonly IdempotentConsumerWrapper<OrderPlacedEvent> _orderPlacedWrapper;
    private IModel? _channel;

    public OrderNotificationConsumer(
        IServiceProvider serviceProvider,
        RabbitMqConnectionManager connectionManager,
        ILogger<OrderNotificationConsumer> logger,
        IdempotentConsumerWrapper<OrderStatusChangedEvent> orderStatusWrapper,
        IdempotentConsumerWrapper<OrderPlacedEvent> orderPlacedWrapper)
    {
        _serviceProvider = serviceProvider;
        _connectionManager = connectionManager;
        _logger = logger;
        _orderStatusWrapper = orderStatusWrapper;
        _orderPlacedWrapper = orderPlacedWrapper;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order Notification Consumer Service starting...");

        try
        {
            var connection = _connectionManager.GetConnection();
            _channel = connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: "order.fanout",
                type: ExchangeType.Fanout,
                durable: true
            );

            var queueName = "notifications.router.queue";
            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
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
                _logger.LogInformation("[RouterConsumer] Received message: {Message}", messageJson);

                Task.Run(async () =>
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(messageJson);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("EventName", out var eventNameProp))
                        {
                            var eventName = eventNameProp.GetString();

                            if (eventName == "OrderStatusChanged")
                            {
                                var ev = JsonSerializer.Deserialize<OrderStatusChangedEvent>(messageJson);
                                if (ev != null)
                                {
                                    await _orderStatusWrapper.ExecuteIdempotentAsync(ev.MessageId, ev, this, stoppingToken);
                                }
                            }
                            else if (eventName == "OrderPlaced")
                            {
                                var ev = JsonSerializer.Deserialize<OrderPlacedEvent>(messageJson);
                                if (ev != null)
                                {
                                    await _orderPlacedWrapper.ExecuteIdempotentAsync(ev.MessageId, ev, this, stoppingToken);
                                }
                            }
                        }

                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[RouterConsumer] Error routing message. Requeuing.");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                }, stoppingToken);
            };

            _channel.BasicConsume(
                queue: queueName,
                autoAck: false,
                consumer: consumer
            );
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Order Notification Consumer Service.");
        }

        return Task.CompletedTask;
    }

    public async Task ProcessInTransactionAsync(OrderStatusChangedEvent message, WasilDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RouterConsumer] Processing OrderStatusChangedEvent for Order {OrderId} in transaction.", message.OrderId);

        using var scope = _serviceProvider.CreateScope();
        var socketService = scope.ServiceProvider.GetRequiredService<ISocketNotificationService>();
        var engineLogger = scope.ServiceProvider.GetRequiredService<ILogger<NotificationEngine>>();
        
        var engine = new NotificationEngine(dbContext, socketService, engineLogger);
        await engine.SendOrderStatusUpdateAsync(message.CustomerId, message.OrderId, message.StoreId, message.NewStatus);
    }

    public async Task ProcessInTransactionAsync(OrderPlacedEvent message, WasilDbContext dbContext, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RouterConsumer] Processing OrderPlacedEvent for Store {StoreId} in transaction.", message.StoreId);

        using var scope = _serviceProvider.CreateScope();
        var socketService = scope.ServiceProvider.GetRequiredService<ISocketNotificationService>();

        await socketService.SendOrderPlacedUpdateAsync(message.OrderId, message.StoreId);
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
