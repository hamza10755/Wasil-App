using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Wasil.Data.Entities;

public partial class WasilDbContext : DbContext
{
    public WasilDbContext(DbContextOptions<WasilDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Break> Breaks { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<Driver> Drivers { get; set; }

    public virtual DbSet<Occasion> Occasions { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<OrderLine> OrderLines { get; set; }

    public virtual DbSet<OrderLineModifier> OrderLineModifiers { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductModifier> ProductModifiers { get; set; }

    public virtual DbSet<Store> Stores { get; set; }

    public virtual DbSet<StoreHour> StoreHours { get; set; }

    public virtual DbSet<Address> Addresses { get; set; }

    public virtual DbSet<AuditTrail> AuditTrails { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<IdempotentRequest> IdempotentRequests { get; set; }

    public virtual DbSet<DailyReport> DailyReports { get; set; }

    public virtual DbSet<StoreAnalytics> StoreAnalytics { get; set; }

    public virtual DbSet<ProcessedMessage> ProcessedMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WasilDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var notEqual = System.Linq.Expressions.Expression.NotEqual(property, System.Linq.Expressions.Expression.Constant(true));
                var lambda = System.Linq.Expressions.Expression.Lambda(notEqual, parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        modelBuilder.Entity<Break>(entity =>
        {
            entity.ToTable("Break");

            entity.HasIndex(e => e.HourId, "IX_Break_hourId");

            entity.Property(e => e.CloseHours).HasColumnName("closeHours");
            entity.Property(e => e.HourId).HasColumnName("hourId");
            entity.Property(e => e.OpenHours).HasColumnName("openHours");

            entity.HasOne(d => d.Hour).WithMany(p => p.Breaks)
                .HasForeignKey(d => d.HourId)
                .HasConstraintName("FK_Break_StoreHours");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customer");

            entity.Property(e => e.FirstName).HasColumnName("firstName");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.LastName).HasColumnName("lastName");
            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.PhoneNumber).HasColumnName("phoneNumber");
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.ToTable("Driver");

            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PhoneNumber).HasColumnName("phoneNumber");
        });

        modelBuilder.Entity<Occasion>(entity =>
        {
            entity.ToTable("Occasion");

            entity.HasIndex(e => e.HourId, "IX_Occasion_hourId");

            entity.Property(e => e.CloseHours).HasColumnName("closeHours");
            entity.Property(e => e.HourId).HasColumnName("hourId");
            entity.Property(e => e.OccasionType).HasColumnName("occasionType");
            entity.Property(e => e.OpenHours).HasColumnName("openHours");

            entity.HasOne(d => d.Hour).WithMany(p => p.Occasions)
                .HasForeignKey(d => d.HourId)
                .HasConstraintName("FK_Occasion_StoreHours");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Order");

            entity.HasIndex(e => e.CustomerId, "IX_Order_customerId");
            entity.HasIndex(e => e.DriverId, "IX_Order_driverId");
            entity.HasIndex(e => e.StoreId, "IX_Order_storeId");

            entity.Property(e => e.CustomerId).HasColumnName("customerId");
            entity.Property(e => e.DriverId).HasColumnName("driverId");
            entity.Property(e => e.Status).HasColumnName("orderStatus");
            entity.Property(e => e.StoreId).HasColumnName("storeId");

            entity.HasOne(d => d.Customer).WithMany(p => p.Orders)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK_Order_Customer");

            entity.HasOne(d => d.Driver).WithMany(p => p.Orders)
                .HasForeignKey(d => d.DriverId)
                .HasConstraintName("FK_Order_Driver");

            entity.HasOne(d => d.Store).WithMany(p => p.Orders)
                .HasForeignKey(d => d.StoreId)
                .HasConstraintName("FK_Order_Store");
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLine");

            entity.HasIndex(e => e.OrderId, "IX_OrderLine_orderId");
            entity.HasIndex(e => e.ProductId, "IX_OrderLine_productId");

            entity.Property(e => e.ItemNote).HasColumnName("itemNote");
            entity.Property(e => e.OrderId).HasColumnName("orderId");
            entity.Property(e => e.ProductId).HasColumnName("productId");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.TotalPrice).HasColumnName("totalPrice");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderLines)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("FK_OrderLine_Order");

            entity.HasOne(d => d.Product).WithMany(p => p.OrderLines)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_OrderLine_Product");
        });

        modelBuilder.Entity<OrderLineModifier>(entity =>
        {
            entity.ToTable("OrderLineModifier");

            entity.HasIndex(e => e.LineId, "IX_OrderLineModifier_lineId");

            entity.Property(e => e.LineId).HasColumnName("lineId");
            entity.Property(e => e.ModifierName).HasColumnName("modifierName");

            entity.HasOne(d => d.Line).WithMany(p => p.OrderLineModifiers)
                .HasForeignKey(d => d.LineId)
                .HasConstraintName("FK_Modifier_OrderLine");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Product");

            entity.HasIndex(e => e.StoreId, "IX_Product_storeId");

            entity.Property(e => e.Availability).HasColumnName("availability");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.StoreId).HasColumnName("storeId");

            entity.HasOne(d => d.Store).WithMany(p => p.Products)
                .HasForeignKey(d => d.StoreId)
                .HasConstraintName("FK_Product_Store");
        });

        modelBuilder.Entity<ProductModifier>(entity =>
        {
            entity.ToTable("productModifiers");

            entity.HasIndex(e => e.ProductId, "IX_productModifiers_productId");

            entity.Property(e => e.ExtraPrice).HasColumnName("extraPrice");
            entity.Property(e => e.ModifierName).HasColumnName("modifierName");
            entity.Property(e => e.ProductId).HasColumnName("productId");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductModifiers)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_ProductModifier_Product");
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("Store");

            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.StoreLocation).HasColumnName("storeLocation");
            entity.Property(e => e.StoreName).HasColumnName("storeName");
        });

        modelBuilder.Entity<StoreHour>(entity =>
        {
            entity.HasIndex(e => e.StoreId, "IX_StoreHours_storeId");

            entity.Property(e => e.CloseHours).HasColumnName("closeHours");
            entity.Property(e => e.DaysOfWeek).HasColumnName("daysOfWeek");
            entity.Property(e => e.OpenHours).HasColumnName("openHours");
            entity.Property(e => e.StoreId).HasColumnName("storeId");

            entity.HasOne(d => d.Store).WithMany(p => p.StoreHours)
                .HasForeignKey(d => d.StoreId)
                .HasConstraintName("FK_StoreHours_Store");
        });

        modelBuilder.Entity<DailyReport>(entity =>
        {
            entity.ToTable("DailyReport");
            entity.Property(e => e.TotalRevenue).HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(e => e.TopSellingProductName).HasMaxLength(150);
            entity.HasOne(d => d.Store)
                .WithMany()
                .HasForeignKey(d => d.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
