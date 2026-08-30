using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class AuditTrailConfiguration : IEntityTypeConfiguration<AuditTrail>
{
    public void Configure(EntityTypeBuilder<AuditTrail> builder)
    {
        builder.ToTable("AuditTrail");

        builder.HasKey(at => at.Id);
        builder.Property(at => at.Id).HasColumnName("auditTrailId");

        builder.Property(at => at.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(at => at.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(at => at.Action).HasMaxLength(50).IsRequired();
        builder.Property(at => at.ChangesJson).IsRequired();
        builder.Property(at => at.TimestampUtc).IsRequired();
        builder.Property(at => at.UserId).IsRequired(false);

        builder.HasIndex(at => at.TimestampUtc)
               .HasDatabaseName("IX_AuditTrail_TimestampUtc_DESC");
    }
}
