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
        builder.Property(a => a.CustomerId).HasColumnName("customerId");

        builder.HasOne(a => a.Customer)
               .WithMany(c => c.Addresses)
               .HasForeignKey(a => a.CustomerId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
