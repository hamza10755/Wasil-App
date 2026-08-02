using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("orderId");

        builder.Property(o => o.OrderCode)
               .IsRequired()
               .HasMaxLength(12);
        builder.HasIndex(o => o.OrderCode).IsUnique();

        builder.Property(o => o.Subtotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.DeliveryFee).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(10);

        builder.HasOne(o => o.Store)
               .WithMany(s => s.Orders)
               .HasForeignKey(o => o.StoreId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Customer)
               .WithMany() 
               .HasForeignKey(o => o.CustomerId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Driver)
               .WithMany()
               .HasForeignKey(o => o.DriverId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.OrderLines)
               .WithOne(ol => ol.Order)
               .HasForeignKey(ol => ol.OrderId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.StatusHistories)
               .WithOne(osh => osh.Order)
               .HasForeignKey(osh => osh.OrderId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);
    }
}