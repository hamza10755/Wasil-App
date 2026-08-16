using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessage");
        builder.HasKey(x => x.Id);
        
        builder.HasIndex(x => x.MessageId).IsUnique();
        
        builder.Property(x => x.ProcessedAtUtc)
            .IsRequired();
    }
}
