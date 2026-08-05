using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Phone)
            .HasMaxLength(20);
        builder.HasIndex(u => u.Phone)
            .IsUnique()
            .HasFilter("[Phone] IS NOT NULL");
        
        builder.Property(u => u.Email)
            .HasMaxLength(100);
        builder.HasIndex(u => u.Email) 
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);
        
        builder.Property(u => u.Role)
            .IsRequired();

        builder.HasOne(u => u.ManagedStore)
            .WithMany()
            .HasForeignKey(u => u.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Customer)
            .WithOne(c => c.User)
            .HasForeignKey<Customer>(c => c.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}