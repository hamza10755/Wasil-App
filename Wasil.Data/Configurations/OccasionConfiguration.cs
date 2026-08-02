using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class OccasionConfiguration : IEntityTypeConfiguration<Occasion>
{
    public void Configure(EntityTypeBuilder<Occasion> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("occasionId");

        builder.Property(o => o.OccasionType).HasMaxLength(50);
    }
}
