using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class BreakConfiguration : IEntityTypeConfiguration<Break>
{
    public void Configure(EntityTypeBuilder<Break> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("breakId");
    }
}
