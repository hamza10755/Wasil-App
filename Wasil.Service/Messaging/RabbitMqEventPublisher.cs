using System;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Wasil.Service.Messaging;

public class RabbitMqEventPublisher : IDisposable
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private IModel? _channel;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _isDisposed;

    public RabbitMqEventPublisher(RabbitMqConnectionManager connectionManager, ILogger<RabbitMqEventPublisher> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    private void EnsureChannel()
    {
        if (_channel is not null) 
            return;

        _connectionLock.Wait();
        try
        {
            if (_channel is not null) 
                return;

            _logger.LogInformation("Creating channel for RabbitMqEventPublisher...");
            var connection = _connectionManager.GetConnection();
            _channel = connection.CreateModel();

            _logger.LogInformation("Declaring durable fanout exchange: order.fanout");
            _channel.ExchangeDeclare(
                exchange: "order.fanout",
                type: ExchangeType.Fanout,
                durable: true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create channel or declare exchange.");
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void Publish<T>(Guid messageId, T message)
    {
        EnsureChannel();

        if (_channel == null)
        {
            throw new InvalidOperationException("RabbitMQ channel is not initialized.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = messageId.ToString();
        properties.ContentType = "application/json";

        _logger.LogInformation("Publishing event to order.fanout: MessageId = {MessageId}", messageId);
        
        _channel.BasicPublish(
            exchange: "order.fanout",
            routingKey: string.Empty,
            mandatory: true,
            basicProperties: properties,
            body: body
        );
    }

    public void PublishToQueue<T>(string queueName, Guid messageId, T message, int attemptCount = 1)
    {
        EnsureChannel();

        if (_channel == null)
        {
            throw new InvalidOperationException("RabbitMQ channel is not initialized.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = messageId.ToString();
        properties.ContentType = "application/json";
        properties.Headers = new Dictionary<string, object>
        {
            { "x-attempt-count", attemptCount }
        };

        _logger.LogInformation("Publishing message directly to queue {QueueName}: MessageId = {MessageId}", queueName, messageId);
        
        _channel.BasicPublish(
            exchange: string.Empty,
            routingKey: queueName,
            basicProperties: properties,
            body: body
        );
    }

    public void Dispose()
    {
        if (_isDisposed) 
            return;
        _isDisposed = true;

        if (_channel is not null)
        {
            _channel.Close();
            _channel.Dispose();
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
