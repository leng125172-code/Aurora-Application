using AuroraStruct3D.Cameras.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机设备管理应用服务接口
/// </summary>
public interface ICameraDeviceAppService : IApplicationService
{
    /// <summary>
    /// 获取相机设备分页列表
    /// </summary>
    Task<PagedResultDto<CameraDeviceDto>> GetListAsync(GetCameraListDto input);

    /// <summary>
    /// 获取相机设备详情（含所有参数集）
    /// </summary>
    Task<CameraDeviceDto> GetAsync(Guid id);

    /// <summary>
    /// 创建相机设备
    /// </summary>
    Task<CameraDeviceDto> CreateAsync(CreateCameraDeviceDto input);

    /// <summary>
    /// 更新相机设备基本信息
    /// </summary>
    Task<CameraDeviceDto> UpdateAsync(Guid id, UpdateCameraDeviceDto input);

    /// <summary>
    /// 删除相机设备
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 扫描并同步SDK中的相机设备（自动创建或更新型号/序列号）
    /// </summary>
    /// <returns>检测到的相机数量</returns>
    Task<int> ScanCamerasAsync();

    /// <summary>
    /// 打开指定相机（调用SDK建立连接）
    /// </summary>
    Task OpenCameraAsync(Guid id);

    /// <summary>
    /// 关闭指定相机
    /// </summary>
    Task CloseCameraAsync(Guid id);

    /// <summary>
    /// 激活指定参数集并将参数写入相机硬件
    /// </summary>
    Task ApplyParameterSetAsync(Guid id, ApplyCameraParameterSetDto input);

    // ─── 手动控制：硬件信息 ─────────────────────────────────────────────────

    /// <summary>
    /// 获取相机硬件设备信息（型号/序列号/固件版本/温度等）
    /// </summary>
    Task<CameraDeviceInfoDto> GetDeviceInfoAsync(Guid id);

    // ─── 手动控制：图像旋转角度（软件端旋转）────────────────────────────────

    /// <summary>
    /// 获取相机软件端图像旋转角度（0/90/180/270）
    /// </summary>
    Task<int> GetImageRotationAngleAsync(Guid id);

    /// <summary>
    /// 设置相机软件端图像旋转角度（仅在手动或检修模式下允许）
    /// </summary>
    Task UpdateImageRotationAngleAsync(Guid id, SetCameraRotationAngleDto input);

    // ─── 手动控制：快照与预览 ───────────────────────────────────────────────

    /// <summary>
    /// 单帧快照（返回 Base64 JPEG data URI）
    /// </summary>
    Task<CameraSnapshotDto> TakeSnapshotAsync(Guid id);

    /// <summary>
    /// 开始相机实时预览（SignalR 推帧 + 可选 RTP/MJPEG UDP）
    /// </summary>
    Task StartPreviewAsync(Guid id, StartCameraPreviewDto input);

    /// <summary>
    /// 停止相机实时预览
    /// </summary>
    Task StopPreviewAsync(Guid id);

    /// <summary>
    /// 发送软件触发信号
    /// </summary>
    Task DoSoftwareTriggerAsync(Guid id);

    /// <summary>
    /// 触发单次自动曝光（ExposureAutoOncePulse 命令节点）
    /// </summary>
    Task DoExposureAutoOncePulseAsync(Guid id);

    /// <summary>
    /// 获取 RTP/MJPEG UDP 推流端点信息
    /// </summary>
    Task<CameraRtpEndpointDto> GetRtpEndpointAsync(Guid id);

    // ─── 通用 GenICam 节点读写 ───────────────────────────────────────────────

    /// <summary>
    /// 读取单个 GenICam 节点值（返回字符串表示；浮点数用 InvariantCulture 序列化）
    /// </summary>
    Task<GenICamNodeResultDto> GetGenICamParamAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromQuery] GenICamNodeGetInput input
    );

    /// <summary>
    /// 批量读取多个 GenICam 节点值（并行读取，单个失败不影响其他节点）
    /// </summary>
    Task<GenICamBatchGetResultDto> BatchGetGenICamParamsAsync(Guid id, GenICamBatchGetInput input);

    /// <summary>
    /// 写入 GenICam 节点值。同时兼容单节点格式和批量格式（通过 nodes 字段区分）。
    /// </summary>
    Task SetGenICamParamAsync(Guid id, GenICamNodeSetInput input);

    /// <summary>
    /// 执行 GenICam 命令节点
    /// </summary>
    Task ExecuteGenICamCommandAsync(Guid id, [Microsoft.AspNetCore.Mvc.FromBody] string nodeName);

    // ─── GenICam 动态 NodeMap（前端按节点信息动态生成 UI 用）──────────────────

    /// <summary>
    /// 获取相机当前缓存的 GenICam NodeMap 与依赖图。
    /// 若相机刚打开尚未完成预跑，会返回 IsXmlLoaded=false（Categories/AllNodes 可能为空）。
    /// </summary>
    Task<CameraNodeMapDto> GetNodeMapAsync(Guid id);

    /// <summary>
    /// 强制重新枚举 GenICam NodeMap 并重新探测选择器依赖（覆盖原缓存）。
    /// 必须在相机已打开且未在采集时调用。
    /// </summary>
    Task<CameraNodeMapDto> RefreshNodeMapAsync(Guid id);

    /// <summary>
    /// 批量读取若干 GenICam 节点的当前值与动态访问模式（不刷新 NodeMap，只查值）。
    /// 用于在选择器节点变更后局部刷新依赖节点。
    /// </summary>
    Task<GenICamBatchGetResultDto> ReadNodesAsync(Guid id, GenICamBatchGetInput input);

    /// <summary>
    /// 一次性获取相机当前完整运行状态快照（NodeMap + 触发模式 + 预览/采集状态 + RTP 端点）。
    /// 前端在页面刷新、路由返回或 SignalR 重连后调用，用于一次往返恢复 UI 状态。
    /// </summary>
    Task<CameraSnapshotStateDto> GetCameraSnapshotStateAsync(Guid id);

    /// <summary>
    /// 分页查询指定相机的操作日志，按发生时间倒序排列。
    /// </summary>
    Task<PagedResultDto<CameraOperationLogDto>> GetLogsAsync(GetCameraLogListDto input);
}

/// <summary>
/// 相机参数集管理应用服务接口
/// </summary>
public interface ICameraParameterSetAppService : IApplicationService
{
    /// <summary>
    /// 获取指定相机的所有参数集
    /// </summary>
    Task<List<CameraParameterSetDto>> GetListByCameraAsync(Guid cameraDeviceId);

    /// <summary>
    /// 获取参数集详情
    /// </summary>
    Task<CameraParameterSetDto> GetAsync(Guid id);

    /// <summary>
    /// 创建参数集
    /// </summary>
    Task<CameraParameterSetDto> CreateAsync(CreateCameraParameterSetDto input);

    /// <summary>
    /// 更新参数集（全量替换参数列表）
    /// </summary>
    Task<CameraParameterSetDto> UpdateAsync(Guid id, UpdateCameraParameterSetDto input);

    /// <summary>
    /// 删除参数集
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 从相机当前硬件状态读取所有参数值，保存为新的参数集
    /// </summary>
    /// <param name="cameraDeviceId">相机设备ID</param>
    /// <param name="parameterSetName">新参数集的名称</param>
    Task<CameraParameterSetDto> CaptureCurrentParamsAsync(
        Guid cameraDeviceId,
        string parameterSetName
    );

    /// <summary>
    /// 克隆参数集（复制一份，供修改后另存）
    /// </summary>
    Task<CameraParameterSetDto> CloneAsync(Guid id, string newName);
}
