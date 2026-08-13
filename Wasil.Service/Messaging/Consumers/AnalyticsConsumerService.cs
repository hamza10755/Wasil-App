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

public class AnalyticsConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnalyticsConsumerService> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection? _connection;
    private IModel? _channel;

    public AnalyticsConsumerService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AnalyticsConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var rabbitSection = configuration.GetSection("RabbitMQ");
        var hostName = rabbitSection["HostName"] ?? "localhost";
        var userName = rabbitSection["UserName"] ?? "guest";
        var password = rabbitSection["Password"] ?? "guest";

        _connectionFactory = new ConnectionFactory
        {
            HostName = hostName,
            UserName = userName,
            Password = password,
            AutomaticRecoveryEnabled = true
        };
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analytics Consumer Service starting...");

        try
        {
            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

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
                            await UpdateAnalyticsIdempotentAsync(orderPlacedEvent, stoppingToken);
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

    private async Task UpdateAnalyticsIdempotentAsync(OrderPlacedEvent ev, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WasilDbContext>();

        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var processedMessage = new ProcessedMessage
            {
                MessageId = ev.MessageId,
                ProcessedAtUtc = DateTime.UtcNow
            };
            dbContext.ProcessedMessages.Add(processedMessage);
            await dbContext.SaveChangesAsync(cancellationToken);

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

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            
            _logger.LogInformation("Successfully processed message {MessageId} and updated analytics for Store {StoreId}.", ev.MessageId, ev.StoreId);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning("Duplicate message detected: {MessageId}. Skipping processing.", ev.MessageId);
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback transaction on duplicate message.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing message {MessageId}.", ev.MessageId);
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback transaction on error.");
            }
            throw;
        }
    }

    private bool IsUniqueConstraintViolation(DbUpdateException dbUpdateEx)
    {
        if (dbUpdateEx.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        }
        return false;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            _channel.Close();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            _connection.Close();
            _connection.Dispose();
        }

        return base.StopAsync(cancellationToken);
    }
}
