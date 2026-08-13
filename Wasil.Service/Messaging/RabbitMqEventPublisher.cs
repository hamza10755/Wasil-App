using System;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Wasil.Service.Messaging;

public class RabbitMqEventPublisher : IDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _isDisposed;

    public RabbitMqEventPublisher(IConfiguration configuration, ILogger<RabbitMqEventPublisher> logger)
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

    private void EnsureChannel()
    {
        if (_channel is not null) 
            return;

        _connectionLock.Wait();
        try
        {
            if (_channel is not null) 
                return;

            _logger.LogInformation("Connecting to RabbitMQ...");
            _connection = _connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

            _logger.LogInformation("Declaring durable fanout exchange: order.fanout");
            _channel.ExchangeDeclare(
                exchange: "order.fanout",
                type: ExchangeType.Fanout,
                durable: true
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ or declare exchange.");
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

        if (_connection is not null)
        {
            _connection.Close();
            _connection.Dispose();
        }

        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
