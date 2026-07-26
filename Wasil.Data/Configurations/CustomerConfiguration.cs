using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("customerId");

        builder.Property(c => c.PhoneNumber).HasMaxLength(15);
        builder.Property(c => c.FirstName).HasMaxLength(15);
        builder.Property(c => c.LastName).HasMaxLength(15);
        builder.Property(c => c.Gender).HasMaxLength(20);
        builder.Property(c => c.Location).HasMaxLength(255);
        
        builder.Property(c => c.Email)
               .HasColumnName("email")
               .HasMaxLength(150)
               .IsRequired();
               
        builder.HasIndex(c => c.Email)
               .IsUnique();

        builder.HasMany(c => c.Addresses)
               .WithOne(a => a.Customer)
               .HasForeignKey(a => a.CustomerId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
