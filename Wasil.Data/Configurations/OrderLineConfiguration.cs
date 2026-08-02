using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.HasKey(ol => ol.Id);
        builder.Property(ol => ol.Id).HasColumnName("lineId");

        builder.Property(ol => ol.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(ol => ol.TotalPrice).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(ol => ol.ProductName).HasMaxLength(100).IsRequired();
        builder.Property(ol => ol.ItemNote).HasMaxLength(500); // Kept the 500 length one

        builder.HasOne(ol => ol.Order)
               .WithMany(o => o.OrderLines)
               .HasForeignKey(ol => ol.OrderId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ol => ol.Product)
               .WithMany() 
               .HasForeignKey(ol => ol.ProductId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);
    }
}