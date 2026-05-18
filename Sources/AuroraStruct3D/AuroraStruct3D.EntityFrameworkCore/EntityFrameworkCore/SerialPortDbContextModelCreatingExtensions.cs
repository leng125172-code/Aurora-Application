using AuroraStruct3D.SerialPorts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

/// <summary>
/// 串口通讯配置模块数据库配置扩展
/// </summary>
public static class SerialPortDbContextModelCreatingExtensions
{
    private const string TablePrefix = AuroraStruct3DDbProperties.DbTablePrefix;

    /// <summary>
    /// 配置串口通讯相关数据库表结构
    /// </summary>
    public static void ConfigureSerialPort(this ModelBuilder builder)
    {
        // ── 串口通讯配置表 ─────────────────────────────────────────────────────────
        builder.Entity<SerialPortConfig>(b =>
        {
            b.ToTable($"{TablePrefix}SerialPortConfigs");
            b.ConfigureByConvention();

            b.Property(x => x.DisplayName)
                .IsRequired()
                .HasMaxLength(SerialPortConsts.MaxDisplayNameLength);

            b.Property(x => x.PortName)
                .IsRequired()
                .HasMaxLength(SerialPortConsts.MaxPortNameLength);

            b.Property(x => x.Description)
                .HasMaxLength(SerialPortConsts.MaxDescriptionLength);

            // 枚举以整数形式存储，便于跨平台兼容
            b.Property(x => x.Parity).HasConversion<int>();
            b.Property(x => x.StopBits).HasConversion<int>();
            b.Property(x => x.Handshake).HasConversion<int>();

            // 同一系统串口名只能有一条配置记录
            b.HasIndex(x => x.PortName).IsUnique();
            b.HasIndex(x => x.IsEnabled);
        });
    }
}
