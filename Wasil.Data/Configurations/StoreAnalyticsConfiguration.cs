using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wasil.Data.Entities;

namespace Wasil.Data.Configurations;

public class StoreAnalyticsConfiguration : IEntityTypeConfiguration<StoreAnalytics>
{
    public void Configure(EntityTypeBuilder<StoreAnalytics> builder)
    {
        builder.ToTable("StoreAnalytics");
        builder.HasKey(x => x.Id);
        
        builder.HasIndex(x => x.StoreId).IsUnique();
        
        builder.Property(x => x.TotalRevenue)
            .HasPrecision(18, 2)
            .IsRequired();
            
        builder.Property(x => x.LastUpdatedUtc)
            .IsRequired();
    }
}