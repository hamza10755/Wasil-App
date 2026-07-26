using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class StoreHourConfiguration : IEntityTypeConfiguration<StoreHour>
{
    public void Configure(EntityTypeBuilder<StoreHour> builder)
    {
        builder.HasKey(sh => sh.Id);
        builder.Property(sh => sh.Id).HasColumnName("hourId");

        builder.Property(sh => sh.DaysOfWeek).HasMaxLength(20);
    }
}
