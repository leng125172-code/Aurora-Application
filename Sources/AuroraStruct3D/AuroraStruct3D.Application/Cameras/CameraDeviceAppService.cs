using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Tucam;
using AuroraStruct3D.Tucam.Interop;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机设备管理及手动控制应用服务。
/// 所有硬件写操作要求设备运行模式为手动或检修模式。
/// </summary>
public class CameraDeviceAppService : AuroraStruct3DAppService, ICameraDeviceAppService
{
    private readonly ICameraDeviceRepository _cameraDeviceRepository;
    private readonly ICameraParameterSetRepository _parameterSetRepository;
    private readonly ITucamCameraService _tucamService;
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly ICameraStreamingService? _streamingService;

    public CameraDeviceAppService(
        ICameraDeviceRepository cameraDeviceRepository,
        ICameraParameterSetRepository parameterSetRepository,
        ITucamCameraService tucamService,
        IDeviceStateManager deviceStateManager,
        ICameraStreamingService? streamingService = null
    )
    {
        _cameraDeviceRepository = cameraDeviceRepository;
        _parameterSetRepository = parameterSetRepository;
        _tucamService = tucamService;
        _deviceStateManager = deviceStateManager;
        _streamingService = streamingService;
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

    // ─── 手动控制：硬件信息 ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraDeviceInfoDto> GetDeviceInfoAsync(Guid id)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        string model = await _tucamService.GetCameraModelAsync(idx);
        int fpgaTemp = await _tucamService.GetDeviceNumericInfoAsync(
            idx,
            TUCamIdInfo.FpgaTemperature
        );
        double sensorTemp = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.Temperature);
        int currentWidth = await _tucamService.GetDeviceNumericInfoAsync(
            idx,
            TUCamIdInfo.CurrentWidth
        );
        int currentHeight = await _tucamService.GetDeviceNumericInfoAsync(
            idx,
            TUCamIdInfo.CurrentHeight
        );

        // 固件/FPGA版本通过 GetValueInfo 字符串读取
        string firmwareVersion = string.Empty;
        string fpgaVersion = string.Empty;
        string serialNumber = string.Empty;

        return new CameraDeviceInfoDto
        {
            Model = model,
            SerialNumber = serialNumber,
            FirmwareVersion = firmwareVersion,
            FpgaVersion = fpgaVersion,
            FpgaTemperature = fpgaTemp,
            SensorTemperature = sensorTemp,
            CurrentWidth = currentWidth,
            CurrentHeight = currentHeight,
        };
    }

    // ─── 手动控制：图像参数 ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraImageParamsDto> GetImageParamsAsync(Guid id)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        TUCamRoiAttr roi = await _tucamService.GetRoiAsync(idx);
        int bitDepth = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.BitOfDepth);
        int horizontal = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.Horizontal);
        int vertical = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.Vertical);
        int binningSum = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.BinningSum);
        int gammaEnabled = await _tucamService.GetCapabilityValueAsync(
            idx,
            TUCamIdCapa.EnableGamma
        );
        double gamma = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.Gamma);
        double contrast = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.Contrast);
        double brightness = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.Brightness);
        double frameRate = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.FrameRate);

        TUCamPropAttr frameRateAttr = await _tucamService.GetPropertyAttrAsync(
            idx,
            TUCamIdProp.FrameRate
        );

        return new CameraImageParamsDto
        {
            RoiEnabled = roi.bEnable != 0,
            RoiHOffset = roi.nHOffset,
            RoiVOffset = roi.nVOffset,
            RoiWidth = roi.nWidth,
            RoiHeight = roi.nHeight,
            PixelDepth = (CameraPixelDepth)bitDepth,
            HorizontalFlip = horizontal != 0,
            VerticalFlip = vertical != 0,
            Binning = (CameraBinningMode)binningSum,
            GammaEnabled = gammaEnabled != 0,
            Gamma = gamma,
            Contrast = contrast,
            Brightness = brightness,
            FrameRate = frameRate,
            FrameRateMax = frameRateAttr.dbValMax,
        };
    }

    /// <inheritdoc/>
    public async Task<CameraImageParamsDto> SetImageParamsAsync(
        Guid id,
        SetCameraImageParamsDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        // ROI 区域（所有ROI字段同时设置）
        if (
            input.RoiEnabled.HasValue
            || input.RoiHOffset.HasValue
            || input.RoiVOffset.HasValue
            || input.RoiWidth.HasValue
            || input.RoiHeight.HasValue
        )
        {
            TUCamRoiAttr roi = await _tucamService.GetRoiAsync(idx);
            if (input.RoiEnabled.HasValue)
                roi.bEnable = input.RoiEnabled.Value ? 1 : 0;
            if (input.RoiHOffset.HasValue)
                roi.nHOffset = input.RoiHOffset.Value;
            if (input.RoiVOffset.HasValue)
                roi.nVOffset = input.RoiVOffset.Value;
            if (input.RoiWidth.HasValue)
                roi.nWidth = input.RoiWidth.Value;
            if (input.RoiHeight.HasValue)
                roi.nHeight = input.RoiHeight.Value;
            await _tucamService.SetRoiAsync(idx, roi);
        }

        if (input.PixelDepth.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.BitOfDepth,
                (int)input.PixelDepth.Value
            );
        }
        if (input.HorizontalFlip.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.Horizontal,
                input.HorizontalFlip.Value ? 1 : 0
            );
        }
        if (input.VerticalFlip.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.Vertical,
                input.VerticalFlip.Value ? 1 : 0
            );
        }
        if (input.Binning.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.BinningSum,
                (int)input.Binning.Value
            );
        }
        if (input.GammaEnabled.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.EnableGamma,
                input.GammaEnabled.Value ? 1 : 0
            );
        }
        if (input.Gamma.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(idx, TUCamIdProp.Gamma, input.Gamma.Value);
        }
        if (input.Contrast.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.Contrast,
                input.Contrast.Value
            );
        }
        if (input.Brightness.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.Brightness,
                input.Brightness.Value
            );
        }
        if (input.FrameRate.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.FrameRate,
                input.FrameRate.Value
            );
        }

        return await GetImageParamsAsync(id);
    }

    // ─── 手动控制：采集参数 ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraAcquisitionParamsDto> GetAcquisitionParamsAsync(Guid id)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        int aeMode = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.AutoExposureMode);
        int aeStatus = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.AutoExposure);
        int gainMode = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.Shutter);
        double aeTargetGray = await _tucamService.GetPropertyValueAsync(
            idx,
            TUCamIdProp.AverageGray
        );
        double aeMaxExposure = await _tucamService.GetPropertyValueAsync(
            idx,
            TUCamIdProp.ExposureMax
        );
        double aeMinExposure = await _tucamService.GetPropertyValueAsync(
            idx,
            TUCamIdProp.ExposureMin
        );
        double exposure = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.ExposureTime);
        double globalGain = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.GlobalGain);

        return new CameraAcquisitionParamsDto
        {
            AeMode = (CameraAutoExposureMode)aeMode,
            AeStatus = aeStatus,
            GainMode = (CameraGainMode)gainMode,
            AeTargetGray = aeTargetGray,
            AeMaxExposure = aeMaxExposure,
            AeMinExposure = aeMinExposure,
            ExposureTime = exposure,
            GlobalGain = globalGain,
        };
    }

    /// <inheritdoc/>
    public async Task<CameraAcquisitionParamsDto> SetAcquisitionParamsAsync(
        Guid id,
        SetCameraAcquisitionParamsDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        if (input.AeMode.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.AutoExposureMode,
                (int)input.AeMode.Value
            );
        }
        if (input.GainMode.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.Shutter,
                (int)input.GainMode.Value
            );
        }
        if (input.AeTargetGray.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.AverageGray,
                input.AeTargetGray.Value
            );
        }
        if (input.AeMaxExposure.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ExposureMax,
                input.AeMaxExposure.Value
            );
        }
        if (input.AeMinExposure.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ExposureMin,
                input.AeMinExposure.Value
            );
        }
        if (input.ExposureTime.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ExposureTime,
                input.ExposureTime.Value
            );
        }
        if (input.GlobalGain.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.GlobalGain,
                input.GlobalGain.Value
            );
        }

        return await GetAcquisitionParamsAsync(id);
    }

    // ─── 手动控制：触发参数 ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraTriggerParamsDto> GetTriggerParamsAsync(Guid id)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        TUCamTriggerAttr trigger = await _tucamService.GetTriggerAsync(idx);
        TUCamTrgOutAttr out1 = await _tucamService.GetTriggerOutAsync(idx, 0);
        TUCamTrgOutAttr out2 = await _tucamService.GetTriggerOutAsync(idx, 1);
        TUCamTrgOutAttr out3 = await _tucamService.GetTriggerOutAsync(idx, 2);

        return new CameraTriggerParamsDto
        {
            TriggerMode = trigger.nTgrMode,
            ExpMode = trigger.nExpMode,
            EdgeMode = trigger.nEdgeMode,
            DelayTm = trigger.nDelayTm,
            Frames = trigger.nFrames,
            BufFrames = trigger.nBufFrames,
            TriggerOut1 = MapTrgOut(out1),
            TriggerOut2 = MapTrgOut(out2),
            TriggerOut3 = MapTrgOut(out3),
        };
    }

    /// <inheritdoc/>
    public async Task<CameraTriggerParamsDto> SetTriggerParamsAsync(
        Guid id,
        SetCameraTriggerParamsDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        if (
            input.TriggerMode.HasValue
            || input.ExpMode.HasValue
            || input.EdgeMode.HasValue
            || input.DelayTm.HasValue
            || input.Frames.HasValue
            || input.BufFrames.HasValue
        )
        {
            TUCamTriggerAttr trigger = await _tucamService.GetTriggerAsync(idx);
            if (input.TriggerMode.HasValue)
                trigger.nTgrMode = input.TriggerMode.Value;
            if (input.ExpMode.HasValue)
                trigger.nExpMode = input.ExpMode.Value;
            if (input.EdgeMode.HasValue)
                trigger.nEdgeMode = input.EdgeMode.Value;
            if (input.DelayTm.HasValue)
                trigger.nDelayTm = input.DelayTm.Value;
            if (input.Frames.HasValue)
                trigger.nFrames = input.Frames.Value;
            if (input.BufFrames.HasValue)
                trigger.nBufFrames = input.BufFrames.Value;
            await _tucamService.SetTriggerAsync(idx, trigger);
        }

        if (input.TriggerOut1 != null)
        {
            await _tucamService.SetTriggerOutAsync(idx, MapTrgOutDto(input.TriggerOut1));
        }
        if (input.TriggerOut2 != null)
        {
            await _tucamService.SetTriggerOutAsync(idx, MapTrgOutDto(input.TriggerOut2));
        }
        if (input.TriggerOut3 != null)
        {
            await _tucamService.SetTriggerOutAsync(idx, MapTrgOutDto(input.TriggerOut3));
        }

        return await GetTriggerParamsAsync(id);
    }

    // ─── 手动控制：自定义参数 ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraCustomParamsDto> GetCustomParamsAsync(Guid id)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        int wbMode = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.AutoWhiteBalance);
        double gainR = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.ChannelGain, 1);
        double gainG = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.ChannelGain, 2);
        double gainB = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.ChannelGain, 3);
        double saturation = await _tucamService.GetPropertyValueAsync(idx, TUCamIdProp.Saturation);
        double colorTemp = await _tucamService.GetPropertyValueAsync(
            idx,
            TUCamIdProp.ColorTemperature
        );
        int ledEnabled = await _tucamService.GetCapabilityValueAsync(idx, TUCamIdCapa.EnableImgPro);
        int currentBufFrames = await _tucamService.GetDeviceNumericInfoAsync(
            idx,
            TUCamIdInfo.CurrentBufFrames
        );

        TUCamCalcRoiAttr wbCalcRoi = await _tucamService.GetCalcRoiAsync(
            idx,
            TUCamIdCalcRoi.WhiteBalance
        );

        return new CameraCustomParamsDto
        {
            WbMode = (CameraWhiteBalanceMode)wbMode,
            ChannelGainR = gainR,
            ChannelGainG = gainG,
            ChannelGainB = gainB,
            Saturation = saturation,
            ColorTemperature = colorTemp,
            LedEnabled = ledEnabled != 0,
            CurrentBufFrames = currentBufFrames,
            WbCalcRoi = new CameraCalcRoiDto
            {
                Enabled = wbCalcRoi.bEnable != 0,
                HOffset = wbCalcRoi.nHOffset,
                VOffset = wbCalcRoi.nVOffset,
                Width = wbCalcRoi.nWidth,
                Height = wbCalcRoi.nHeight,
            },
        };
    }

    /// <inheritdoc/>
    public async Task<CameraCustomParamsDto> SetCustomParamsAsync(
        Guid id,
        SetCameraCustomParamsDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        if (input.WbMode.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.AutoWhiteBalance,
                (int)input.WbMode.Value
            );
        }
        if (input.ChannelGainR.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ChannelGain,
                input.ChannelGainR.Value,
                1
            );
        }
        if (input.ChannelGainG.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ChannelGain,
                input.ChannelGainG.Value,
                2
            );
        }
        if (input.ChannelGainB.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ChannelGain,
                input.ChannelGainB.Value,
                3
            );
        }
        if (input.Saturation.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.Saturation,
                input.Saturation.Value
            );
        }
        if (input.ColorTemperature.HasValue)
        {
            await _tucamService.SetPropertyValueAsync(
                idx,
                TUCamIdProp.ColorTemperature,
                input.ColorTemperature.Value
            );
        }
        if (input.LedEnabled.HasValue)
        {
            await _tucamService.SetCapabilityValueAsync(
                idx,
                TUCamIdCapa.EnableImgPro,
                input.LedEnabled.Value ? 1 : 0
            );
        }
        if (input.WbCalcRoi != null)
        {
            TUCamCalcRoiAttr calcRoi = new TUCamCalcRoiAttr
            {
                idCalc = (int)TUCamIdCalcRoi.WhiteBalance,
                bEnable = input.WbCalcRoi.Enabled ? 1 : 0,
                nHOffset = input.WbCalcRoi.HOffset,
                nVOffset = input.WbCalcRoi.VOffset,
                nWidth = input.WbCalcRoi.Width,
                nHeight = input.WbCalcRoi.Height,
            };
            await _tucamService.SetCalcRoiAsync(idx, calcRoi);
        }

        return await GetCustomParamsAsync(id);
    }

    // ─── 手动控制：快照与预览 ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraSnapshotDto> TakeSnapshotAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        // 启动单次连续采集 → 抓一帧 → 停止
        await _tucamService.StartCaptureAsync(idx);
        try
        {
            byte[] jpegBytes = await _tucamService.GrabFrameRawAsync(idx, timeoutMs: 5000);
            string dataUri = "data:image/jpeg;base64," + Convert.ToBase64String(jpegBytes);
            return new CameraSnapshotDto { DataUri = dataUri, CapturedAt = DateTime.UtcNow };
        }
        finally
        {
            await _tucamService.StopCaptureAsync(idx);
        }
    }

    /// <inheritdoc/>
    public async Task StartPreviewAsync(Guid id, StartCameraPreviewDto input)
    {
        EnsureManualOrMaintenanceMode();

        if (_streamingService == null)
        {
            throw new UserFriendlyException("推流服务不可用，请检查 Host 模块配置");
        }

        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        await _streamingService.StartPreviewAsync(id, input.ConnectionId, input.EnableRtp);
        Logger.LogInformation("相机 {Id} 开始实时预览", id);
    }

    /// <inheritdoc/>
    public async Task StopPreviewAsync(Guid id)
    {
        if (_streamingService == null)
        {
            return;
        }

        await _streamingService.StopPreviewAsync(id);
        Logger.LogInformation("相机 {Id} 停止实时预览", id);
    }

    /// <inheritdoc/>
    public async Task DoSoftwareTriggerAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        await _tucamService.DoSoftwareTriggerAsync(idx);
    }

    /// <inheritdoc/>
    public Task<CameraRtpEndpointDto> GetRtpEndpointAsync(Guid id)
    {
        if (_streamingService == null)
        {
            return Task.FromResult(new CameraRtpEndpointDto());
        }

        CameraRtpEndpointDto? endpoint = _streamingService.GetRtpEndpoint(id);
        return Task.FromResult(endpoint ?? new CameraRtpEndpointDto());
    }

    // ─── 手动控制：用户配置文件 ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task LoadUserProfileAsync(Guid id, CameraUserProfileDto input)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        await _tucamService.LoadProfilesAsync(idx, input.ProfileName);
        Logger.LogInformation("相机 {Id} 已加载配置文件 '{Profile}'", id, input.ProfileName);
    }

    /// <inheritdoc/>
    public async Task SaveUserProfileAsync(Guid id, CameraUserProfileDto input)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        await _tucamService.SaveProfilesAsync(idx, input.ProfileName);
        Logger.LogInformation("相机 {Id} 已保存配置文件 '{Profile}'", id, input.ProfileName);
    }

    // ─── 辅助方法 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 校验当前设备运行模式是否为手动或检修模式；不满足时抛出 UserFriendlyException
    /// </summary>
    private void EnsureManualOrMaintenanceMode()
    {
        DeviceRunMode mode = _deviceStateManager.RunMode;
        if (mode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
        {
            throw new UserFriendlyException(
                $"当前运行模式为【{mode switch {
                    DeviceRunMode.Online => "联机",
                    DeviceRunMode.Auto   => "自动",
                    _                    => mode.ToString()
                }}】，相机手动控制仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>
    /// 校验相机是否已打开
    /// </summary>
    private void EnsureCameraOpen(int deviceIndex)
    {
        if (!_tucamService.IsCameraOpen(deviceIndex))
        {
            throw new UserFriendlyException("相机未打开，请先调用打开接口");
        }
    }

    /// <summary>
    /// 根据数据库 ID 查询相机的 SDK 索引
    /// </summary>
    private async Task<int> GetDeviceIndexAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        return camera.DeviceIndex;
    }

    /// <summary>将 TUCamTrgOutAttr 映射为 DTO</summary>
    private static CameraTriggerOutDto MapTrgOut(TUCamTrgOutAttr attr) =>
        new CameraTriggerOutDto
        {
            Port = attr.nTgrOutPort,
            Mode = attr.nTgrOutMode,
            EdgeMode = attr.nEdgeMode,
            DelayTm = attr.nDelayTm,
            Width = attr.nWidth,
        };

    /// <summary>将 DTO 映射为 TUCamTrgOutAttr</summary>
    private static TUCamTrgOutAttr MapTrgOutDto(CameraTriggerOutDto dto) =>
        new TUCamTrgOutAttr
        {
            nTgrOutPort = dto.Port,
            nTgrOutMode = dto.Mode,
            nEdgeMode = dto.EdgeMode,
            nDelayTm = dto.DelayTm,
            nWidth = dto.Width,
        };
}
