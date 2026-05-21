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

    // ─── 手动控制：图像参数 ─────────────────────────────────────────────────

    /// <summary>
    /// 获取图像采集参数（ROI/位深/翻转/Binning/Gamma/对比度/亮度/帧率）
    /// </summary>
    Task<CameraImageParamsDto> GetImageParamsAsync(Guid id);

    /// <summary>
    /// 设置图像采集参数
    /// </summary>
    Task<CameraImageParamsDto> SetImageParamsAsync(Guid id, SetCameraImageParamsDto input);

    // ─── 手动控制：采集参数 ─────────────────────────────────────────────────

    /// <summary>
    /// 获取采集参数（AE/曝光/增益）
    /// </summary>
    Task<CameraAcquisitionParamsDto> GetAcquisitionParamsAsync(Guid id);

    /// <summary>
    /// 设置采集参数
    /// </summary>
    Task<CameraAcquisitionParamsDto> SetAcquisitionParamsAsync(
        Guid id,
        SetCameraAcquisitionParamsDto input
    );

    // ─── 手动控制：触发参数 ─────────────────────────────────────────────────

    /// <summary>
    /// 获取触发参数（触发模式/边沿/延迟/输出端口）
    /// </summary>
    Task<CameraTriggerParamsDto> GetTriggerParamsAsync(Guid id);

    /// <summary>
    /// 设置触发参数
    /// </summary>
    Task<CameraTriggerParamsDto> SetTriggerParamsAsync(Guid id, SetCameraTriggerParamsDto input);

    // ─── 手动控制：自定义参数 ───────────────────────────────────────────────

    /// <summary>
    /// 获取自定义参数（WB通道增益/饱和度/色温/LED/触发计数）
    /// </summary>
    Task<CameraCustomParamsDto> GetCustomParamsAsync(Guid id);

    /// <summary>
    /// 设置自定义参数
    /// </summary>
    Task<CameraCustomParamsDto> SetCustomParamsAsync(Guid id, SetCameraCustomParamsDto input);

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
    /// 获取 RTP/MJPEG UDP 推流端点信息
    /// </summary>
    Task<CameraRtpEndpointDto> GetRtpEndpointAsync(Guid id);

    // ─── 手动控制：用户配置文件 ─────────────────────────────────────────────

    /// <summary>
    /// 加载用户配置文件（需在停止采集后调用）
    /// </summary>
    Task LoadUserProfileAsync(Guid id, CameraUserProfileDto input);

    /// <summary>
    /// 保存用户配置文件
    /// </summary>
    Task SaveUserProfileAsync(Guid id, CameraUserProfileDto input);
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
