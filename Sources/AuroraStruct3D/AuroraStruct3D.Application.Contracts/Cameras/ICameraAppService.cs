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
