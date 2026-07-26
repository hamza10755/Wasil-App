using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("driverId");

        builder.Property(d => d.PhoneNumber).HasMaxLength(15);
        builder.Property(d => d.Name).HasMaxLength(15);
        builder.Property(d => d.Location).HasMaxLength(255);
    }
}
