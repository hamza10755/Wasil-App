using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wasil.Data.Entities;

namespace Wasil.Service.Messaging.Consumers;

public interface IIdempotentConsumer<TMessage>
{
    Task ProcessInTransactionAsync(TMessage message, WasilDbContext dbContext, CancellationToken cancellationToken);
}

public class IdempotentConsumerWrapper<TMessage>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IdempotentConsumerWrapper<TMessage>> _logger;

    public IdempotentConsumerWrapper(
        IServiceProvider serviceProvider,
        ILogger<IdempotentConsumerWrapper<TMessage>> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<bool> ExecuteIdempotentAsync(
        Guid messageId, 
        TMessage message, 
        IIdempotentConsumer<TMessage> consumer,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WasilDbContext>();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        
        bool isDuplicate = false;

        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var processedMessage = new ProcessedMessage
                {
                    MessageId = messageId,
                    ProcessedAtUtc = DateTime.UtcNow
                };
                dbContext.ProcessedMessages.Add(processedMessage);
                await dbContext.SaveChangesAsync(cancellationToken);

                await consumer.ProcessInTransactionAsync(message, dbContext, cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                isDuplicate = true;
                _logger.LogWarning("Duplicate message detected in wrapper: {MessageId}. Skipping processing.", messageId);
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback duplicate transaction.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing message {MessageId} in wrapper.", messageId);
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Failed to rollback error transaction.");
                }
                throw;
            }
        });

        return true;
    }

    private bool IsUniqueConstraintViolation(DbUpdateException dbUpdateEx)
    {
        if (dbUpdateEx.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        }
        return false;
    }
}
