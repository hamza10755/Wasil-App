using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class ProductModifierConfiguration : IEntityTypeConfiguration<ProductModifier>
{
    public void Configure(EntityTypeBuilder<ProductModifier> builder)
    {
        builder.HasKey(pm => pm.Id);
        builder.Property(pm => pm.Id).HasColumnName("modifierId");

        builder.Property(pm => pm.ModifierName).HasMaxLength(100);
    }
}
