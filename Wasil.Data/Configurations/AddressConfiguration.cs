using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Address");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("addressId");

        builder.Property(a => a.Street).HasMaxLength(255).IsRequired();
        builder.Property(a => a.City).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ZipCode).HasMaxLength(20).IsRequired();
        builder.Property(a => a.IsDefault).IsRequired();
        builder.Property(a => a.CustomerId).HasColumnName("customerId");

        // Relationships
        builder.HasOne(a => a.Customer)
               .WithMany()
               .HasForeignKey(a => a.CustomerId)
               .OnDelete(DeleteBehavior.Restrict);

        // Business Rule: exactly one can be the default per customer
        builder.HasIndex(a => new { a.CustomerId, a.IsDefault })
               .IsUnique()
               .HasFilter("[IsDefault] = 1");
    }
}
