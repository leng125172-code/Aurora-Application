namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机模块常量定义
/// </summary>
public static class CameraConsts
{
    /// <summary>相机设备名称最大长度</summary>
    public const int MaxNameLength = 128;

    /// <summary>相机序列号最大长度</summary>
    public const int MaxSerialNumberLength = 64;

    /// <summary>相机描述最大长度</summary>
    public const int MaxDescriptionLength = 512;

    /// <summary>参数集名称最大长度</summary>
    public const int MaxParameterSetNameLength = 128;

    /// <summary>参数键名最大长度</summary>
    public const int MaxParameterKeyLength = 64;

    /// <summary>参数值最大长度（存储为字符串）</summary>
    public const int MaxParameterValueLength = 256;

    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";

    /// <summary>操作日志参数摘要最大长度</summary>
    public const int MaxOperationParameterSummaryLength = 128;

    /// <summary>操作日志错误信息最大长度</summary>
    public const int MaxOperationLogErrorMessageLength = 512;
}

/// <summary>
/// 相机参数类型枚举
/// </summary>
public enum CameraParameterType
{
    /// <summary>浮点属性（使用 TUCAM_Prop_GetValue/SetValue）</summary>
    Property = 0,

    /// <summary>整数能力（使用 TUCAM_Capa_GetValue/SetValue）</summary>
    Capability = 1,
}

/// <summary>
/// 相机状态枚举
/// </summary>
public enum CameraStatus
{
    /// <summary>未知/未连接</summary>
    Unknown = 0,

    /// <summary>已连接并就绪</summary>
    Ready = 1,

    /// <summary>采集中</summary>
    Capturing = 2,

    /// <summary>错误状态</summary>
    Error = 3,

    /// <summary>已关闭</summary>
    Closed = 4,
}

/// <summary>
/// 相机操作类型枚举（用于操作日志）
/// </summary>
public enum CameraOperationType
{
    /// <summary>SDK 初始化</summary>
    Initialize = 0,

    /// <summary>SDK 反初始化</summary>
    Uninitialize = 1,

    /// <summary>打开相机</summary>
    Open = 2,

    /// <summary>关闭相机</summary>
    Close = 3,

    /// <summary>启动采集</summary>
    StartCapture = 4,

    /// <summary>停止采集</summary>
    StopCapture = 5,
}
