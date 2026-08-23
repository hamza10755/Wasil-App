using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Wasil.Service.Messaging;

public class RabbitMqConnectionManager : IDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private IConnection? _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public RabbitMqConnectionManager(IConfiguration configuration, ILogger<RabbitMqConnectionManager> logger)
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

    public IConnection GetConnection()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RabbitMqConnectionManager));
        }

        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        lock (_lock)
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _logger.LogInformation("Establishing single persistent connection to RabbitMQ...");
            _connection = _connectionFactory.CreateConnection();
            return _connection;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_connection is { IsOpen: true })
                {
                    _connection.Close();
                    _connection.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing shared RabbitMQ connection.");
            }
        }
    }
}
