using AuroraStruct3D.AI;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Motors;
using AuroraStruct3D.ProductModels;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projects;
using AuroraStruct3D.SerialPorts;
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

        /// <summary>相机操作日志集合</summary>
        DbSet<CameraOperationLog> CameraOperationLogs { get; }

        // ── 电机模块 ──────────────────────────────────────────────────────────────

        /// <summary>电机轴集合</summary>
        DbSet<MotorAxis> MotorAxes { get; }

        /// <summary>运动配置集合</summary>
        DbSet<MotorMotionConfig> MotorMotionConfigs { get; }

        /// <summary>故障记录集合</summary>
        DbSet<MotorFaultRecord> MotorFaultRecords { get; }

        /// <summary>PR路径配置集合（雷赛iCL-RS专用）</summary>
        DbSet<MotorPrPath> MotorPrPaths { get; }

        /// <summary>电机操作日志集合</summary>
        DbSet<MotorOperationLog> MotorOperationLogs { get; }

        // ── DLP 投影仪模块 ────────────────────────────────────────────────────

        /// <summary>DLP 投影仪设备集合</summary>
        DbSet<ProjectorDevice> ProjectorDevices { get; }

        /// <summary>DLP 投影仪操作日志集合</summary>
        DbSet<ProjectorOperationLog> ProjectorOperationLogs { get; }

        // ── 设备状态管理模块 ──────────────────────────────────────────────────────

        /// <summary>设备状态切换日志集合</summary>
        DbSet<DeviceStateLog> DeviceStateLogs { get; }

        /// <summary>设备故障记录集合</summary>
        DbSet<DeviceFault> DeviceFaults { get; }

        // ── 产品三维数模模块 ──────────────────────────────────────────────────────

        /// <summary>产品三维数模集合</summary>
        DbSet<ProductModel> ProductModels { get; }

        /// <summary>产品三维数模操作日志集合</summary>
        DbSet<ProductModelOperationLog> ProductModelOperationLogs { get; }

        // ── AI 模型模块 ──────────────────────────────────────────────────────

        /// <summary>AI 模型集合</summary>
        DbSet<AiModel> AiModels { get; }

        /// <summary>AI 模型文件集合</summary>
        DbSet<AiModelFile> AiModelFiles { get; }

        /// <summary>AI 模型标识集合</summary>
        DbSet<AiModelIdentifier> AiModelIdentifiers { get; }

        /// <summary>AI 模型标识关联集合</summary>
        DbSet<AiModelIdentifierLink> AiModelIdentifierLinks { get; }

        /// <summary>AI 模型操作日志集合</summary>
        DbSet<AiModelOperationLog> AiModelOperationLogs { get; }

        // ── 串口通讯模块 ──────────────────────────────────────────────────────

        /// <summary>串口操作日志集合</summary>
        DbSet<SerialPortOperationLog> SerialPortOperationLogs { get; }

        // ── 项目管理模块 ──────────────────────────────────────────────────────

        /// <summary>项目主表集合</summary>
        DbSet<ProjectInfo> ProjectInfos { get; }
    }
}
