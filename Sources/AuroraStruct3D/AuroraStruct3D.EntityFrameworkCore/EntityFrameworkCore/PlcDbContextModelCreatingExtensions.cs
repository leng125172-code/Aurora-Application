using AuroraStruct3D.Plcs;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

public static class PlcDbContextModelCreatingExtensions
{
    public static void ConfigurePlc(this ModelBuilder builder)
    {
        builder.Entity<PlcDevice>(b =>
        {
            b.ToTable("AbpProPlcDevices");
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(PlcConsts.MaxNameLength);
            b.Property(x => x.DriverId).IsRequired().HasMaxLength(PlcConsts.MaxDriverIdLength);
            b.Property(x => x.EndpointUrl).IsRequired().HasMaxLength(PlcConsts.MaxEndpointLength);
            b.Property(x => x.UserName).HasMaxLength(PlcConsts.MaxUserNameLength);
            b.Property(x => x.SecurityPolicy)
                .IsRequired()
                .HasMaxLength(PlcConsts.MaxSecurityPolicyLength);
            b.Property(x => x.LastError).HasMaxLength(PlcConsts.MaxErrorLength);
            b.HasIndex(x => x.Name).IsUnique();
            b.HasIndex(x => new { x.DriverId, x.EndpointUrl });
        });

        builder.Entity<PlcTag>(b =>
        {
            b.ToTable("AbpProPlcTags");
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(PlcConsts.MaxCodeLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(PlcConsts.MaxNameLength);
            b.Property(x => x.Address).IsRequired().HasMaxLength(PlcConsts.MaxAddressLength);
            b.Property(x => x.Unit).HasMaxLength(PlcConsts.MaxUnitLength);
            b.Property(x => x.DisplayFormat).HasMaxLength(PlcConsts.MaxFormatLength);
            b.HasOne<PlcDevice>()
                .WithMany()
                .HasForeignKey(x => x.PlcDeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => new { x.PlcDeviceId, x.Address }).IsUnique();
        });

        builder.Entity<PlcOperationLog>(b =>
        {
            b.ToTable("AbpProPlcOperationLogs");
            b.ConfigureByConvention();
            b.Property(x => x.ErrorMessage).HasMaxLength(PlcConsts.MaxErrorLength);
            b.HasOne<PlcDevice>()
                .WithMany()
                .HasForeignKey(x => x.PlcDeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.PlcDeviceId, x.OccurredAt });
        });

        builder.Entity<PlcTrustedCertificate>(b =>
        {
            b.ToTable("AbpProPlcTrustedCertificates");
            b.ConfigureByConvention();
            b.Property(x => x.Thumbprint).IsRequired().HasMaxLength(128);
            b.Property(x => x.Subject).IsRequired().HasMaxLength(512);
            b.HasOne<PlcDevice>()
                .WithMany()
                .HasForeignKey(x => x.PlcDeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.PlcDeviceId, x.Thumbprint }).IsUnique();
        });
    }
}
