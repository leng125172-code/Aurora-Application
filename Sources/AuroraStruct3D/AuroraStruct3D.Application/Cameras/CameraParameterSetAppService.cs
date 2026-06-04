using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Tucam;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机参数集管理应用服务
/// </summary>
[Authorize]
public class CameraParameterSetAppService : AuroraStruct3DAppService, ICameraParameterSetAppService
{
    private readonly ICameraParameterSetRepository _parameterSetRepository;
    private readonly ICameraDeviceRepository _cameraDeviceRepository;
    private readonly ITucamCameraService _tucamService;

    public CameraParameterSetAppService(
        ICameraParameterSetRepository parameterSetRepository,
        ICameraDeviceRepository cameraDeviceRepository,
        ITucamCameraService tucamService
    )
    {
        _parameterSetRepository = parameterSetRepository;
        _cameraDeviceRepository = cameraDeviceRepository;
        _tucamService = tucamService;
    }

    /// <inheritdoc/>
    public async Task<List<CameraParameterSetDto>> GetListByCameraAsync(Guid cameraDeviceId)
    {
        List<CameraParameterSet> sets = await _parameterSetRepository.GetListByCameraAsync(
            cameraDeviceId
        );
        return sets.Select(x => x.ToDto()).ToList();
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSetDto> GetAsync(Guid id)
    {
        CameraParameterSet? set = await _parameterSetRepository.GetWithParametersAsync(id);
        if (set == null)
        {
            throw new EntityNotFoundException(typeof(CameraParameterSet), id);
        }
        return set.ToDto();
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSetDto> CreateAsync(CreateCameraParameterSetDto input)
    {
        // 如果设为默认，先清除该相机其他参数集的默认标记
        if (input.IsDefault)
        {
            await ClearDefaultFlagAsync(input.CameraDeviceId);
        }

        var set = new CameraParameterSet(GuidGenerator.Create(), input.CameraDeviceId, input.Name);
        set.SetDescription(input.Description);
        set.SetDefault(input.IsDefault);
        set.SetSortOrder(input.SortOrder);

        foreach (CreateCameraParameterDto paramDto in input.Parameters)
        {
            set.SetParameter(
                paramDto.ParamKey,
                paramDto.ParamType,
                paramDto.Value,
                paramDto.Description
            );
        }

        await _parameterSetRepository.InsertAsync(set);
        return set.ToDto();
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSetDto> UpdateAsync(Guid id, UpdateCameraParameterSetDto input)
    {
        CameraParameterSet? set = await _parameterSetRepository.GetWithParametersAsync(id);
        if (set == null)
        {
            throw new EntityNotFoundException(typeof(CameraParameterSet), id);
        }

        if (input.IsDefault && !set.IsDefault)
        {
            await ClearDefaultFlagAsync(set.CameraDeviceId);
        }

        set.SetName(input.Name);
        set.SetDescription(input.Description);
        set.SetDefault(input.IsDefault);
        set.SetSortOrder(input.SortOrder);

        // 全量替换参数列表：清除旧的，添加新的
        set.Parameters.Clear();
        foreach (CreateCameraParameterDto paramDto in input.Parameters)
        {
            set.SetParameter(
                paramDto.ParamKey,
                paramDto.ParamType,
                paramDto.Value,
                paramDto.Description
            );
        }

        await _parameterSetRepository.UpdateAsync(set);
        return set.ToDto();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        await _parameterSetRepository.DeleteAsync(id);
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSetDto> CaptureCurrentParamsAsync(
        Guid cameraDeviceId,
        string parameterSetName
    )
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(cameraDeviceId);

        if (!_tucamService.IsCameraOpen(camera.DeviceIndex))
        {
            throw new UserFriendlyException("相机未打开，无法读取当前参数，请先打开相机");
        }

        var set = new CameraParameterSet(GuidGenerator.Create(), cameraDeviceId, parameterSetName);
        set.SetDescription($"从硬件读取于 {Clock.Now:yyyy-MM-dd HH:mm:ss}");

        // 读取常用属性值并存储
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.ExposureTime);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.GlobalGain);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.Gamma);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.Contrast);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.Saturation);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.Sharpness);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.FrameRate);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.BlackLevel);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.Temperature);
        await ReadAndSavePropertyAsync(set, camera.DeviceIndex, TUCamIdProp.ColorTemperature);

        // 读取常用能力值并存储
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.BitOfDepth);
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.Resolution);
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.AutoExposure);
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.Horizontal);
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.Vertical);
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.EnableGamma);
        await ReadAndSaveCapabilityAsync(set, camera.DeviceIndex, TUCamIdCapa.Shutter);

        await _parameterSetRepository.InsertAsync(set);
        Logger.LogInformation(
            "已从相机 {Name} 读取当前参数并保存为参数集 [{SetName}]",
            camera.Name,
            parameterSetName
        );
        return set.ToDto();
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSetDto> CloneAsync(Guid id, string newName)
    {
        CameraParameterSet? source = await _parameterSetRepository.GetWithParametersAsync(id);
        if (source == null)
        {
            throw new EntityNotFoundException(typeof(CameraParameterSet), id);
        }

        var cloned = new CameraParameterSet(GuidGenerator.Create(), source.CameraDeviceId, newName);
        cloned.SetDescription(source.Description);
        cloned.SetDefault(false);
        cloned.SetSortOrder(source.SortOrder + 1);

        foreach (CameraParameter param in source.Parameters)
        {
            cloned.SetParameter(param.ParamKey, param.ParamType, param.Value, param.Description);
        }

        await _parameterSetRepository.InsertAsync(cloned);
        return cloned.ToDto();
    }

    /// <summary>
    /// 读取属性值并保存到参数集
    /// </summary>
    private async Task ReadAndSavePropertyAsync(
        CameraParameterSet set,
        int deviceIndex,
        TUCamIdProp propId
    )
    {
        try
        {
            double value = await _tucamService.GetPropertyValueAsync(deviceIndex, propId);
            set.SetParameter(
                propId.ToString(),
                CameraParameterType.Property,
                value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        catch (Exception ex)
        {
            Logger.LogDebug("读取属性 {Prop} 失败（相机可能不支持）: {Msg}", propId, ex.Message);
        }
    }

    /// <summary>
    /// 读取能力值并保存到参数集
    /// </summary>
    private async Task ReadAndSaveCapabilityAsync(
        CameraParameterSet set,
        int deviceIndex,
        TUCamIdCapa capaId
    )
    {
        try
        {
            int value = await _tucamService.GetCapabilityValueAsync(deviceIndex, capaId);
            set.SetParameter(
                capaId.ToString(),
                CameraParameterType.Capability,
                value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        catch (Exception ex)
        {
            Logger.LogDebug("读取能力 {Capa} 失败（相机可能不支持）: {Msg}", capaId, ex.Message);
        }
    }

    /// <summary>
    /// 清除指定相机下所有参数集的默认标记
    /// </summary>
    private async Task ClearDefaultFlagAsync(Guid cameraDeviceId)
    {
        List<CameraParameterSet> sets = await _parameterSetRepository.GetListByCameraAsync(
            cameraDeviceId
        );
        foreach (CameraParameterSet s in sets.Where(s => s.IsDefault))
        {
            s.SetDefault(false);
            await _parameterSetRepository.UpdateAsync(s);
        }
    }
}
