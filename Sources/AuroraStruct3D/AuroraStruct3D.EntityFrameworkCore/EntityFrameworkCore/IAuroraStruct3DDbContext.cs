using AuroraStruct3D.Cameras;
using AuroraStruct3D.Motors;
using AuroraStruct3D.Projectors;
using Microsoft.EntityFrameworkCore;

namespace AuroraStruct3D.EntityFrameworkCore
{
    [ConnectionStringName("Default")]
    public interface IAuroraStruct3DDbContext : IEfCoreDbContext
    {
        /// <summary>相机设备集合</summary>
        DbSet<CameraDevice> CameraDevices { get; }

        /// <summary>相机参数集集合</summary>
        DbSet<CameraParameterSet> CameraParameterSets { get; }

        /// <summary>相机参数项集合</summary>
        DbSet<CameraParameter> CameraParameters { get; }

        // ── 电机模块 ──────────────────────────────────────────────────────────────

        /// <summary>电机轴集合</summary>
        DbSet<MotorAxis> MotorAxes { get; }

        /// <summary>运动配置集合</summary>
        DbSet<MotorMotionConfig> MotorMotionConfigs { get; }

        /// <summary>故障记录集合</summary>
        DbSet<MotorFaultRecord> MotorFaultRecords { get; }

        /// <summary>PR路径配置集合（雷赛iCL-RS专用）</summary>
        DbSet<MotorPrPath> MotorPrPaths { get; }

        // ── DLP 投影机模块 ────────────────────────────────────────────────────

        /// <summary>DLP 投影机设备集合</summary>
        DbSet<ProjectorDevice> ProjectorDevices { get; }

        /// <summary>DLP 投影机操作日志集合</summary>
        DbSet<ProjectorOperationLog> ProjectorOperationLogs { get; }
    }
}
