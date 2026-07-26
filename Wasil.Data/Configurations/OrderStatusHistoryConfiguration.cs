using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory");

        builder.HasKey(osh => osh.Id);
        builder.Property(osh => osh.Id).HasColumnName("orderStatusHistoryId");

        builder.Property(osh => osh.OldStatus).HasMaxLength(50).IsRequired();
        builder.Property(osh => osh.NewStatus).HasMaxLength(50).IsRequired();
        builder.Property(osh => osh.TimestampUtc).IsRequired();
        builder.Property(osh => osh.OrderId).HasColumnName("orderId");

        // Relationships
        builder.HasOne(osh => osh.Order)
               .WithMany()
               .HasForeignKey(osh => osh.OrderId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
