using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wasil.Data.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Wasil.Data.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    public static bool AuditLoggingEnabled { get; set; } = true;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, 
        InterceptionResult<int> result)
    {
        UpdateEntitiesAndCreateAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        UpdateEntitiesAndCreateAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateEntitiesAndCreateAudit(DbContext? context)
    {
        if (context == null) return;

        Guid? userId = null;
        try
        {
            var serviceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;
            var currentUserType = Type.GetType("Wasil.Data.Interfaces.ICurrentUser, Wasil.Service");
            if (currentUserType != null)
            {
                var currentUserService = serviceProvider.GetService(currentUserType);
                if (currentUserService != null)
                {
                    var userIdProp = currentUserService.GetType().GetProperty("UserId");
                    if (userIdProp != null)
                    {
                        userId = (Guid?)userIdProp.GetValue(currentUserService);
                    }
                }
            }
        }
        catch
        {
            // Fallback if the service cannot be resolved
        }

        var currentTime = DateTime.UtcNow;
        var auditEntries = new List<AuditTrail>();

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            bool isAuditEntity = entry.Entity is AuditTrail;

            string entityName = entry.Entity.GetType().Name;
            string entityId = entry.Property("Id").CurrentValue?.ToString() ?? "0";
            string action = string.Empty;
            var changesDict = new Dictionary<string, object?>();

            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = currentTime;
                    entry.Entity.IsDeleted = false;
                    action = "Insert";

                    if (!isAuditEntity && AuditLoggingEnabled)
                    {
                        foreach (var prop in entry.Properties)
                        {
                            changesDict[prop.Metadata.Name] = new { Old = (object?)null, New = prop.CurrentValue };
                        }
                    }
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = currentTime;
                    action = "Update";

                    if (entry.Entity.IsDeleted && entry.Property(nameof(BaseEntity.IsDeleted)).IsModified)
                    {
                        action = "SoftDelete";
                        entry.Entity.DeletedAtUtc = currentTime;
                    }

                    if (!isAuditEntity && AuditLoggingEnabled)
                    {
                        foreach (var prop in entry.Properties)
                        {
                            if (prop.IsModified)
                            {
                                changesDict[prop.Metadata.Name] = new { Old = prop.OriginalValue, New = prop.CurrentValue };
                            }
                        }
                    }
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAtUtc = currentTime;
                    entry.Entity.UpdatedAtUtc = currentTime;
                    action = "SoftDelete";

                    if (!isAuditEntity && AuditLoggingEnabled)
                    {
                        foreach (var prop in entry.Properties)
                        {
                            changesDict[prop.Metadata.Name] = new { Old = prop.OriginalValue, New = prop.CurrentValue };
                        }
                    }
                    break;
            }

            if (!isAuditEntity && AuditLoggingEnabled && changesDict.Count > 0 && !string.IsNullOrEmpty(action))
            {
                auditEntries.Add(new AuditTrail
                {
                    EntityName = entityName,
                    EntityId = entityId,
                    Action = action,
                    ChangesJson = JsonSerializer.Serialize(changesDict),
                    TimestampUtc = currentTime,
                    UserId = userId
                });
            }
        }

        if (auditEntries.Count > 0)
        {
            context.Set<AuditTrail>().AddRange(auditEntries);
        }
    }
}