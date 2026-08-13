using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Wasil.Service.Messaging.Events;

namespace Wasil.Service.Messaging.Consumers;

public class ConfirmationWorkerService : BackgroundService
{
    private readonly ILogger<ConfirmationWorkerService> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection? _connection;
    private IModel? _channel;

    public ConfirmationWorkerService(
        IConfiguration configuration,
        ILogger<ConfirmationWorkerService> logger)
    {
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
        _logger.LogInformation("Confirmation Worker Service starting...");

        try
        {
            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

            var queueName = "order.confirmation.work-queue";
            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);

                Task.Run(async () =>
                {
                    Guid messageId = Guid.Empty;
                    try
                    {
                        var message = JsonSerializer.Deserialize<SendOrderConfirmationMessage>(messageJson);
                        if (message != null)
                        {
                            messageId = message.MessageId;
                            _logger.LogInformation("Processing Confirmation Message: {MessageId}, Order: {OrderId}, Sequence: {SequenceNumber}", 
                                message.MessageId, message.OrderId, message.SequenceNumber);

                            var delay = message.SequenceNumber % 3 == 1 ? 3000 : 200;
                            _logger.LogInformation("Confirmation Message {MessageId}: Simulating processing delay of {Delay}ms...", message.MessageId, delay);
                            await Task.Delay(delay, stoppingToken);

                            _logger.LogInformation("Successfully completed processing for Confirmation Message {MessageId}.", message.MessageId);
                        }

                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Confirmation Message {MessageId}. Requeuing.", messageId);
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
            _logger.LogCritical(ex, "Fatal error in Confirmation Worker Service.");
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

        if (_connection is not null)
        {
            _connection.Close();
            _connection.Dispose();
        }

        return base.StopAsync(cancellationToken);
    }
}
