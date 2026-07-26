using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("storeId");

        builder.Property(s => s.StoreName).HasMaxLength(15);
        builder.Property(s => s.StoreLocation).HasMaxLength(255);
        builder.Property(s => s.Status).HasDefaultValue(true);
    }
}
