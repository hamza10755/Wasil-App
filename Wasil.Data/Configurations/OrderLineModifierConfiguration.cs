using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class OrderLineModifierConfiguration : IEntityTypeConfiguration<OrderLineModifier>
{
    public void Configure(EntityTypeBuilder<OrderLineModifier> builder)
    {
        builder.HasKey(olm => olm.Id);
        builder.Property(olm => olm.Id).HasColumnName("modifierId");

        builder.Property(olm => olm.ModifierName).HasMaxLength(100);
    }
}
