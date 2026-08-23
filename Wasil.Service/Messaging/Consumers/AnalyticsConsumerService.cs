using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wasil.Data.Entities;
using Wasil.Service.Messaging.Events;

namespace Wasil.Service.Messaging.Consumers;

public class AnalyticsConsumerService : BackgroundService, IIdempotentConsumer<OrderPlacedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnalyticsConsumerService> _logger;
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly IdempotentConsumerWrapper<OrderPlacedEvent> _idempotentWrapper;
    private IModel? _channel;

    public AnalyticsConsumerService(
        IServiceProvider serviceProvider,
        RabbitMqConnectionManager connectionManager,
        ILogger<AnalyticsConsumerService> logger,
        IdempotentConsumerWrapper<OrderPlacedEvent> idempotentWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _connectionManager = connectionManager;
        _idempotentWrapper = idempotentWrapper;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analytics Consumer Service starting...");

        try
        {
            var connection = _connectionManager.GetConnection();
            _channel = connection.CreateModel();

            _channel.ExchangeDeclare(
                exchange: "order.fanout",
                type: ExchangeType.Fanout,
                durable: true
            );

            var queueName = "analytics.order-placed.queue";
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
                _logger.LogInformation("Received message from queue: {Message}", messageJson);

                Task.Run(async () =>
                {
                    try
                    {
                        var orderPlacedEvent = JsonSerializer.Deserialize<OrderPlacedEvent>(messageJson);
                        if (orderPlacedEvent != null)
                        {
                            await _idempotentWrapper.ExecuteIdempotentAsync(
                                orderPlacedEvent.MessageId, 
                                orderPlacedEvent, 
                                this, 
                                stoppingToken
                            );
                        }

                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message from queue. Requeuing message.");
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
            _logger.LogCritical(ex, "Fatal error in Analytics Consumer Service.");
        }

        return Task.CompletedTask;
    }

    public async Task ProcessInTransactionAsync(OrderPlacedEvent ev, WasilDbContext dbContext, CancellationToken cancellationToken)
    {
        var analytics = await dbContext.StoreAnalytics
            .FirstOrDefaultAsync(x => x.StoreId == ev.StoreId, cancellationToken);

        if (analytics == null)
        {
            analytics = new StoreAnalytics
            {
                StoreId = ev.StoreId,
                TotalOrders = 1,
                TotalRevenue = ev.TotalAmount,
                LastUpdatedUtc = DateTime.UtcNow
            };
            dbContext.StoreAnalytics.Add(analytics);
        }
        else
        {
            analytics.TotalOrders += 1;
            analytics.TotalRevenue += ev.TotalAmount;
            analytics.LastUpdatedUtc = DateTime.UtcNow;
        }

        _logger.LogInformation("Analytics business logic: Incrementing store {StoreId} order count.", ev.StoreId);
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
