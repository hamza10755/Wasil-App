using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Wasil.Service.Services;

public class CacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheService> _logger;
    private readonly ConcurrentDictionary<string, (int Hits, int Misses)> _counters;

    public CacheService(IConnectionMultiplexer redis, ILogger<CacheService> logger)
    {
        _redis = redis;
        _logger = logger;
        _counters = new ConcurrentDictionary<string, (int Hits, int Misses)>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<T?> GetAsync<T>(string key, string cacheName)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(key);

            if (value.HasValue)
            {
                IncrementHit(cacheName);
                _logger.LogInformation("Cache HIT for key: {Key} in cache: {CacheName}", key, cacheName);
                return JsonSerializer.Deserialize<T>((string)value!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis connection/read failure on GetAsync for key: {Key}", key);
        }

        IncrementMiss(cacheName);
        _logger.LogInformation("Cache MISS for key: {Key} in cache: {CacheName}", key, cacheName);
        return default;
    }

    public async Task SetAsync<T>(string key, string cacheName, T value, TimeSpan ttl)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value);
            await db.StringSetAsync(key, json, ttl);
            _logger.LogInformation("Cache SET for key: {Key} in cache: {CacheName} with TTL: {Ttl}", key, cacheName, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis connection/write failure on SetAsync for key: {Key}", key);
        }
    }

    public async Task DeleteAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis connection/delete failure on key: {Key}", key);
        }
    }

    private void IncrementHit(string cacheName)
    {
        _counters.AddOrUpdate(cacheName, (1, 0), (key, current) => (current.Hits + 1, current.Misses));
    }

    private void IncrementMiss(string cacheName)
    {
        _counters.AddOrUpdate(cacheName, (0, 1), (key, current) => (current.Hits, current.Misses + 1));
    }

    public ConcurrentDictionary<string, (int Hits, int Misses)> GetCounters()
    {
        return _counters;
    }
}
