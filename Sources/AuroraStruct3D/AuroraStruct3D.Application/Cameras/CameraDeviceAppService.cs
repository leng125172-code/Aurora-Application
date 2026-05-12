using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Tucam;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机设备管理应用服务
/// </summary>
public class CameraDeviceAppService : AuroraStruct3DAppService, ICameraDeviceAppService
{
    private readonly ICameraDeviceRepository _cameraDeviceRepository;
    private readonly ICameraParameterSetRepository _parameterSetRepository;
    private readonly ITucamCameraService _tucamService;

    public CameraDeviceAppService(
        ICameraDeviceRepository cameraDeviceRepository,
        ICameraParameterSetRepository parameterSetRepository,
        ITucamCameraService tucamService
    )
    {
        _cameraDeviceRepository = cameraDeviceRepository;
        _parameterSetRepository = parameterSetRepository;
        _tucamService = tucamService;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CameraDeviceDto>> GetListAsync(GetCameraListDto input)
    {
        long totalCount = await _cameraDeviceRepository.GetCountAsync(input.Filter);
        List<CameraDevice> items = await _cameraDeviceRepository.GetListAsync(
            input.SkipCount,
            input.MaxResultCount,
            input.Filter
        );

        return new PagedResultDto<CameraDeviceDto>(
            totalCount,
            items.Select(x => x.ToDto()).ToList()
        );
    }

    /// <inheritdoc/>
    public async Task<CameraDeviceDto> GetAsync(Guid id)
    {
        CameraDevice? camera = await _cameraDeviceRepository.GetWithParameterSetsAsync(id);
        if (camera == null)
        {
            throw new EntityNotFoundException(typeof(CameraDevice), id);
        }
        return camera.ToDto();
    }

    /// <inheritdoc/>
    public async Task<CameraDeviceDto> CreateAsync(CreateCameraDeviceDto input)
    {
        // 检查物理索引是否已被占用
        CameraDevice? existing = await _cameraDeviceRepository.FindByDeviceIndexAsync(
            input.DeviceIndex
        );
        if (existing != null)
        {
            throw new UserFriendlyException(
                $"设备索引 {input.DeviceIndex} 已被相机 [{existing.Name}] 占用"
            );
        }

        var camera = new CameraDevice(GuidGenerator.Create(), input.Name, input.DeviceIndex);
        camera.SetDescription(input.Description);
        camera.SetEnabled(input.IsEnabled);

        await _cameraDeviceRepository.InsertAsync(camera);
        return camera.ToDto();
    }

    /// <inheritdoc/>
    public async Task<CameraDeviceDto> UpdateAsync(Guid id, UpdateCameraDeviceDto input)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        camera.SetName(input.Name);
        camera.SetDescription(input.Description);
        camera.SetEnabled(input.IsEnabled);

        await _cameraDeviceRepository.UpdateAsync(camera);
        return camera.ToDto();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        // 关闭相机（如果已打开）
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        if (_tucamService.IsCameraOpen(camera.DeviceIndex))
        {
            await _tucamService.CloseCameraAsync(camera.DeviceIndex);
        }

        await _cameraDeviceRepository.DeleteAsync(id);
    }

    /// <inheritdoc/>
    public async Task<int> ScanCamerasAsync()
    {
        int count = await _tucamService.InitializeAsync();

        for (int i = 0; i < count; i++)
        {
            CameraDevice? existing = await _cameraDeviceRepository.FindByDeviceIndexAsync(i);
            if (existing == null)
            {
                // 自动创建数据库记录
                var newCamera = new CameraDevice(GuidGenerator.Create(), $"相机 #{i}", i);
                await _cameraDeviceRepository.InsertAsync(newCamera);
                Logger.LogInformation("自动注册相机设备，索引: {Index}", i);
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public async Task OpenCameraAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);

        await _tucamService.OpenCameraAsync(camera.DeviceIndex);

        // 读取硬件信息并更新数据库
        string model = await _tucamService.GetCameraModelAsync(camera.DeviceIndex);
        camera.UpdateHardwareInfo(model, null);
        camera.SetStatus(CameraStatus.Ready);
        await _cameraDeviceRepository.UpdateAsync(camera);
    }

    /// <inheritdoc/>
    public async Task CloseCameraAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        await _tucamService.CloseCameraAsync(camera.DeviceIndex);

        camera.SetStatus(CameraStatus.Closed);
        await _cameraDeviceRepository.UpdateAsync(camera);
    }

    /// <inheritdoc/>
    public async Task ApplyParameterSetAsync(Guid id, ApplyCameraParameterSetDto input)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);

        if (!_tucamService.IsCameraOpen(camera.DeviceIndex))
        {
            throw new UserFriendlyException("相机未打开，请先打开相机再应用参数集");
        }

        CameraParameterSet? paramSet = await _parameterSetRepository.GetWithParametersAsync(
            input.ParameterSetId
        );
        if (paramSet == null || paramSet.CameraDeviceId != id)
        {
            throw new EntityNotFoundException(typeof(CameraParameterSet), input.ParameterSetId);
        }

        // 将参数集中的所有参数写入相机硬件
        foreach (CameraParameter param in paramSet.Parameters)
        {
            await WriteParameterToHardwareAsync(camera.DeviceIndex, param);
        }

        // 更新激活的参数集ID
        camera.ActivateParameterSet(input.ParameterSetId);
        await _cameraDeviceRepository.UpdateAsync(camera);

        Logger.LogInformation("相机 {Name} 已应用参数集 [{SetName}]", camera.Name, paramSet.Name);
    }

    /// <summary>
    /// 将单个参数项写入相机硬件
    /// </summary>
    private async Task WriteParameterToHardwareAsync(int deviceIndex, CameraParameter param)
    {
        if (param.ParamType == CameraParameterType.Property)
        {
            if (Enum.TryParse<TUCamIdProp>(param.ParamKey, out TUCamIdProp propId))
            {
                double value = param.GetDoubleValue();
                await _tucamService.SetPropertyValueAsync(deviceIndex, propId, value);
            }
            else
            {
                Logger.LogWarning("未识别的属性键: {Key}，已跳过", param.ParamKey);
            }
        }
        else if (param.ParamType == CameraParameterType.Capability)
        {
            if (Enum.TryParse<TUCamIdCapa>(param.ParamKey, out TUCamIdCapa capaId))
            {
                int value = param.GetIntValue();
                await _tucamService.SetCapabilityValueAsync(deviceIndex, capaId, value);
            }
            else
            {
                Logger.LogWarning("未识别的能力键: {Key}，已跳过", param.ParamKey);
            }
        }
    }
}
