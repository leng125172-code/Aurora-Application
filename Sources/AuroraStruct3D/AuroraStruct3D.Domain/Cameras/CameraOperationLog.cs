using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机操作日志实体，记录每次硬件操作的结果与耗时
/// </summary>
public class CameraOperationLog : Entity<Guid>
{
    /// <summary>关联的相机设备 ID（外键 → CameraDevice）</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>SDK 相机索引（硬件物理位置）</summary>
    public int DeviceIndex { get; private set; }

    /// <summary>操作类型</summary>
    public CameraOperationType OperationType { get; private set; }

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; private set; }

    /// <summary>操作参数摘要（可选，最大 128 字符）</summary>
    public string? ParameterSummary { get; private set; }

    /// <summary>错误信息（仅失败时填写，最大 512 字符）</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>硬件往返耗时（毫秒）</summary>
    public long RoundTripMs { get; private set; }

    /// <summary>EF Core 无参构造函数</summary>
    private CameraOperationLog() { }

    /// <summary>
    /// 创建成功日志
    /// </summary>
    /// <param name="id">日志唯一 ID</param>
    /// <param name="cameraDeviceId">相机设备数据库 ID</param>
    /// <param name="deviceIndex">SDK 相机索引</param>
    /// <param name="operationType">操作类型</param>
    /// <param name="roundTripMs">往返耗时（毫秒）</param>
    /// <param name="parameterSummary">操作参数摘要（可选）</param>
    public static CameraOperationLog Success(
        Guid id,
        Guid cameraDeviceId,
        int deviceIndex,
        CameraOperationType operationType,
        long roundTripMs,
        string? parameterSummary = null
    ) =>
        new()
        {
            Id = id,
            CameraDeviceId = cameraDeviceId,
            DeviceIndex = deviceIndex,
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = true,
            RoundTripMs = roundTripMs,
            ParameterSummary = parameterSummary is null
                ? null
                : parameterSummary.Length <= CameraConsts.MaxOperationParameterSummaryLength
                    ? parameterSummary
                    : parameterSummary[..CameraConsts.MaxOperationParameterSummaryLength],
        };

    /// <summary>
    /// 创建失败日志
    /// </summary>
    /// <param name="id">日志唯一 ID</param>
    /// <param name="cameraDeviceId">相机设备数据库 ID</param>
    /// <param name="deviceIndex">SDK 相机索引</param>
    /// <param name="operationType">操作类型</param>
    /// <param name="errorMessage">错误信息</param>
    /// <param name="roundTripMs">往返耗时（毫秒）</param>
    /// <param name="parameterSummary">操作参数摘要（可选）</param>
    public static CameraOperationLog Failure(
        Guid id,
        Guid cameraDeviceId,
        int deviceIndex,
        CameraOperationType operationType,
        string errorMessage,
        long roundTripMs,
        string? parameterSummary = null
    ) =>
        new()
        {
            Id = id,
            CameraDeviceId = cameraDeviceId,
            DeviceIndex = deviceIndex,
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = false,
            RoundTripMs = roundTripMs,
            ErrorMessage = errorMessage.Length <= CameraConsts.MaxOperationLogErrorMessageLength
                ? errorMessage
                : errorMessage[..CameraConsts.MaxOperationLogErrorMessageLength],
            ParameterSummary = parameterSummary is null
                ? null
                : parameterSummary.Length <= CameraConsts.MaxOperationParameterSummaryLength
                    ? parameterSummary
                    : parameterSummary[..CameraConsts.MaxOperationParameterSummaryLength],
        };
}
