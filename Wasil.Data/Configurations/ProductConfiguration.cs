using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("productId");

        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.Availability).HasDefaultValue(true);
        builder.Property(p => p.Price).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.StockQuantity).HasDefaultValue(0);

        builder.HasOne(p => p.Store)
               .WithMany(s => s.Products)
               .HasForeignKey(p => p.StoreId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Category)
               .WithMany(c => c.Products)
               .HasForeignKey(p => p.CategoryId)
               .IsRequired()
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.StoreId, p.Name })
               .IsUnique();
    }
}
