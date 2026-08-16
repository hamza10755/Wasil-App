using System;
using System.Collections.Generic;
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

            var dlxExchange = "order.confirmation.dlx";
            var dlqQueue = "order.confirmation.dlq";

            _channel.ExchangeDeclare(exchange: dlxExchange, type: ExchangeType.Fanout, durable: true);
            _channel.QueueDeclare(
                queue: dlqQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
            _channel.QueueBind(queue: dlqQueue, exchange: dlxExchange, routingKey: string.Empty);

            var queueName = "order.confirmation.work-queue";
            var queueArgs = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", dlxExchange }
            };

            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs
            );

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);

                int attemptCount = 1;
                var headers = ea.BasicProperties.Headers;
                if (headers != null && headers.TryGetValue("x-attempt-count", out var countObj))
                {
                    if (countObj is int countInt)
                    {
                        attemptCount = countInt;
                    }
                    else if (countObj is byte[] countBytes)
                    {
                        attemptCount = int.Parse(Encoding.UTF8.GetString(countBytes));
                    }
                }

                Task.Run(async () =>
                {
                    Guid messageId = Guid.Empty;
                    try
                    {
                        var message = JsonSerializer.Deserialize<SendOrderConfirmationMessage>(messageJson);
                        if (message != null)
                        {
                            messageId = message.MessageId;
                            _logger.LogInformation("Processing Confirmation Message: {MessageId}, Order: {OrderId}, Sequence: {SequenceNumber} (Attempt: {Attempt})", 
                                message.MessageId, message.OrderId, message.SequenceNumber, attemptCount);

                            var delay = message.SequenceNumber % 3 == 1 ? 3000 : 200;
                            _logger.LogInformation("Confirmation Message {MessageId}: Simulating processing delay of {Delay}ms...", message.MessageId, delay);
                            await Task.Delay(delay, stoppingToken);

                            _logger.LogInformation("Successfully completed processing for Confirmation Message {MessageId}.", message.MessageId);
                        }

                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        if (attemptCount >= 3)
                        {
                            _logger.LogCritical(ex, "Confirmation Message {MessageId} failed after {Attempts} attempts. Routing to DLQ.", messageId, attemptCount);
                            _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                        }
                        else
                        {
                            var nextAttempt = attemptCount + 1;
                            _logger.LogWarning("Confirmation Message {MessageId} failed on attempt {Attempts}. Republishing with attempt {NextAttempt}...", messageId, attemptCount, nextAttempt);
                            
                            var nextProps = _channel.CreateBasicProperties();
                            nextProps.Persistent = ea.BasicProperties.Persistent;
                            nextProps.ContentType = ea.BasicProperties.ContentType;
                            nextProps.Headers = new Dictionary<string, object>
                            {
                                { "x-attempt-count", nextAttempt }
                            };

                            _channel.BasicPublish(
                                exchange: string.Empty,
                                routingKey: queueName,
                                basicProperties: nextProps,
                                body: body
                            );

                            _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                        }
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
