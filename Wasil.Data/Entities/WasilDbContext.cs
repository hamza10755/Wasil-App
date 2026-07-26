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

    public virtual DbSet<Driver> Drivers { get; set; }

    public virtual DbSet<Occasion> Occasions { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<OrderLine> OrderLines { get; set; }

    public virtual DbSet<OrderLineModifier> OrderLineModifiers { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductModifier> ProductModifiers { get; set; }

    public virtual DbSet<Store> Stores { get; set; }

    public virtual DbSet<StoreHour> StoreHours { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Break>(entity =>
        {
            entity.HasKey(e => e.BreakId).HasName("PK__Break__B3B02988251B3CC2");

            entity.ToTable("Break");

            entity.HasIndex(e => e.HourId, "IX_Break_hourId");

            entity.Property(e => e.BreakId)
                .ValueGeneratedNever()
                .HasColumnName("breakId");
            entity.Property(e => e.CloseHours).HasColumnName("closeHours");
            entity.Property(e => e.HourId).HasColumnName("hourId");
            entity.Property(e => e.OpenHours).HasColumnName("openHours");

            entity.HasOne(d => d.Hour).WithMany(p => p.Breaks)
                .HasForeignKey(d => d.HourId)
                .HasConstraintName("FK_Break_StoreHours");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__Customer__B611CB7D3CBDD469");

            entity.ToTable("Customer");

            entity.Property(e => e.CustomerId)
                .ValueGeneratedNever()
                .HasColumnName("customerId");
            entity.Property(e => e.FirstName)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("firstName");
            entity.Property(e => e.Gender)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("gender");
            entity.Property(e => e.LastName)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("lastName");
            entity.Property(e => e.Location)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("location");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("phoneNumber");
        });

        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(e => e.DriverId).HasName("PK__Driver__F1532DF22DAC2A0E");

            entity.ToTable("Driver");

            entity.Property(e => e.DriverId)
                .ValueGeneratedNever()
                .HasColumnName("driverId");
            entity.Property(e => e.Location)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("location");
            entity.Property(e => e.Name)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("phoneNumber");
        });

        modelBuilder.Entity<Occasion>(entity =>
        {
            entity.HasKey(e => e.OccasionId).HasName("PK__Occasion__212F5B1850527280");

            entity.ToTable("Occasion");

            entity.HasIndex(e => e.HourId, "IX_Occasion_hourId");

            entity.Property(e => e.OccasionId)
                .ValueGeneratedNever()
                .HasColumnName("occasionId");
            entity.Property(e => e.CloseHours).HasColumnName("closeHours");
            entity.Property(e => e.HourId).HasColumnName("hourId");
            entity.Property(e => e.OccasionType)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("occasionType");
            entity.Property(e => e.OpenHours).HasColumnName("openHours");

            entity.HasOne(d => d.Hour).WithMany(p => p.Occasions)
                .HasForeignKey(d => d.HourId)
                .HasConstraintName("FK_Occasion_StoreHours");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("PK__Order__0809335DA32407C5");

            entity.ToTable("Order");

            entity.HasIndex(e => e.CustomerId, "IX_Order_customerId");

            entity.HasIndex(e => e.DriverId, "IX_Order_driverId");

            entity.HasIndex(e => e.StoreId, "IX_Order_storeId");

            entity.Property(e => e.OrderId)
                .ValueGeneratedNever()
                .HasColumnName("orderId");
            entity.Property(e => e.CustomerId).HasColumnName("customerId");
            entity.Property(e => e.DriverId).HasColumnName("driverId");
            entity.Property(e => e.OrderStatus)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("orderStatus");
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
            entity.HasKey(e => e.LineId).HasName("PK__OrderLin__32489DA50DAA64DF");

            entity.ToTable("OrderLine");

            entity.HasIndex(e => e.OrderId, "IX_OrderLine_orderId");

            entity.HasIndex(e => e.ProductId, "IX_OrderLine_productId");

            entity.Property(e => e.LineId)
                .ValueGeneratedNever()
                .HasColumnName("lineId");
            entity.Property(e => e.ItemNote)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("itemNote");
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
            entity.HasKey(e => e.ModifierId).HasName("PK__OrderLin__01D595CB36823815");

            entity.ToTable("OrderLineModifier");

            entity.HasIndex(e => e.LineId, "IX_OrderLineModifier_lineId");

            entity.Property(e => e.ModifierId)
                .ValueGeneratedNever()
                .HasColumnName("modifierId");
            entity.Property(e => e.LineId).HasColumnName("lineId");
            entity.Property(e => e.ModifierName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("modifierName");

            entity.HasOne(d => d.Line).WithMany(p => p.OrderLineModifiers)
                .HasForeignKey(d => d.LineId)
                .HasConstraintName("FK_Modifier_OrderLine");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.ProductId).HasName("PK__Product__2D10D16AD76A5887");

            entity.ToTable("Product");

            entity.HasIndex(e => e.StoreId, "IX_Product_storeId");

            entity.Property(e => e.ProductId)
                .ValueGeneratedNever()
                .HasColumnName("productId");
            entity.Property(e => e.Availability)
                .HasDefaultValue(true)
                .HasColumnName("availability");
            entity.Property(e => e.Name)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("name");
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.StoreId).HasColumnName("storeId");

            entity.HasOne(d => d.Store).WithMany(p => p.Products)
                .HasForeignKey(d => d.StoreId)
                .HasConstraintName("FK_Product_Store");
        });

        modelBuilder.Entity<ProductModifier>(entity =>
        {
            entity.HasKey(e => e.ModifierId).HasName("PK__productM__01D595CBCC3813AE");

            entity.ToTable("productModifiers");

            entity.HasIndex(e => e.ProductId, "IX_productModifiers_productId");

            entity.Property(e => e.ModifierId)
                .ValueGeneratedNever()
                .HasColumnName("modifierId");
            entity.Property(e => e.ExtraPrice).HasColumnName("extraPrice");
            entity.Property(e => e.ModifierName)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("modifierName");
            entity.Property(e => e.ProductId).HasColumnName("productId");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductModifiers)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_ProductModifier_Product");
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.HasKey(e => e.StoreId).HasName("PK__Store__1EA7161374DDC1F8");

            entity.ToTable("Store");

            entity.Property(e => e.StoreId)
                .ValueGeneratedNever()
                .HasColumnName("storeId");
            entity.Property(e => e.Status)
                .HasDefaultValue(true)
                .HasColumnName("status");
            entity.Property(e => e.StoreLocation)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("storeLocation");
            entity.Property(e => e.StoreName)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("storeName");
        });

        modelBuilder.Entity<StoreHour>(entity =>
        {
            entity.HasKey(e => e.HourId).HasName("PK__StoreHou__DF98C7DBA379F592");

            entity.HasIndex(e => e.StoreId, "IX_StoreHours_storeId");

            entity.Property(e => e.HourId)
                .ValueGeneratedNever()
                .HasColumnName("hourId");
            entity.Property(e => e.CloseHours).HasColumnName("closeHours");
            entity.Property(e => e.DaysOfWeek)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("daysOfWeek");
            entity.Property(e => e.OpenHours).HasColumnName("openHours");
            entity.Property(e => e.StoreId).HasColumnName("storeId");

            entity.HasOne(d => d.Store).WithMany(p => p.StoreHours)
                .HasForeignKey(d => d.StoreId)
                .HasConstraintName("FK_StoreHours_Store");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
