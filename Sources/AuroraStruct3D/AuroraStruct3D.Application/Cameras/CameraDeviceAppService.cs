using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Tucam;
using AuroraStruct3D.Tucam.GenICam;
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

        // 扫描过程中同步构建索引→设备ID映射，供操作日志使用
        var deviceIdMap = new Dictionary<int, Guid>();

        for (int i = 0; i < count; i++)
        {
            // 扫描阶段无需打开相机，直接按索引读取型号
            string model = await _tucamService.GetModelByIndexAsync(i);

            CameraDevice? existing = await _cameraDeviceRepository.FindByDeviceIndexAsync(i);
            if (existing == null)
            {
                // 自动创建数据库记录，附带型号信息
                var newCamera = new CameraDevice(GuidGenerator.Create(), $"相机 #{i}", i);
                if (!string.IsNullOrEmpty(model))
                {
                    newCamera.UpdateHardwareInfo(model, null);
                }
                await _cameraDeviceRepository.InsertAsync(newCamera);
                Logger.LogInformation("自动注册相机设备，索引: {Index}，型号: {Model}", i, model);
                deviceIdMap[i] = newCamera.Id;
            }
            else
            {
                if (!string.IsNullOrEmpty(model) && existing.Model != model)
                {
                    // 型号有变化时更新（保留已有序列号）
                    existing.UpdateHardwareInfo(model, existing.SerialNumber);
                    await _cameraDeviceRepository.UpdateAsync(existing);
                    Logger.LogInformation("更新相机 {Index} 型号: {Model}", i, model);
                }
                deviceIdMap[i] = existing.Id;
            }
        }

        // 扫描完成后刷新 TucamCameraService 的操作日志映射，防止外键违规
        _tucamService.SetCameraDeviceIdMapping(deviceIdMap);

        return count;
    }

    /// <inheritdoc/>
    public async Task OpenCameraAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);

        await _tucamService.OpenCameraAsync(camera.DeviceIndex);

        // 读取硬件信息并更新数据库（保留已有序列号）
        string model = await _tucamService.GetCameraModelAsync(camera.DeviceIndex);
        camera.UpdateHardwareInfo(model, camera.SerialNumber);
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

        // 通过 GenICam DeviceVersion 节点读取固件版本（ElementAttr 方式）
        string firmwareVersion =
            await _tucamService.GetGenICamStringAsync(idx, "DeviceVersion") ?? string.Empty;
        string fpgaVersion = string.Empty;

        // 通过 GenICam String 节点读取设备序列号（Expert/RO）
        string serialNumber =
            await _tucamService.GetGenICamStringAsync(idx, "DeviceSerialNumber") ?? string.Empty;

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

    // ─── 手动控制：图像旋转角度（软件端旋转）────────────────────────────────

    /// <inheritdoc/>
    public async Task<int> GetImageRotationAngleAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        return camera.ImageRotationAngle;
    }

    /// <inheritdoc/>
    public async Task SetImageRotationAngleAsync(Guid id, SetCameraRotationAngleDto input)
    {
        EnsureManualOrMaintenanceMode();
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        try
        {
            camera.SetImageRotationAngle(input.Angle);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new UserFriendlyException("图像旋转角度仅支持 0、90、180、270 度");
        }
        await _cameraDeviceRepository.UpdateAsync(camera);
        if (_streamingService != null)
        {
            await _streamingService.UpdatePreviewRotationAsync(id, camera.ImageRotationAngle);
        }
    }

    // 注：原 typed 参数方法（GetImageParams/PostImageParams/GetAcquisitionParams/PostAcquisitionParams/
    // GetTriggerParams/PostTriggerParams/GetCustomParams/PostCustomParams）已删除，
    // 前端改用 GetNodeMapAsync + SetGenICamParamAsync 完成所有动态参数读写。

    // ─── 手动控制：快照与预览 ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraSnapshotDto> TakeSnapshotAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        int idx = camera.DeviceIndex;
        EnsureCameraOpen(idx);

        bool startedForSnapshot = false;
        try
        {
            if (!_tucamService.IsCapturing(idx))
            {
                await _tucamService.StartCaptureAsync(idx);
                startedForSnapshot = true;
            }

            // 动态计算抓帧超时：取当前曝光时间（微秒）× 2 + 1s 裕量，最少 8s
            int grabTimeoutMs = 8000;
            try
            {
                long exposureUs = await _tucamService.GetGenICamIntAsync(idx, "ExposureTime");
                int exposureMs = (int)(exposureUs / 1000L);
                grabTimeoutMs = Math.Max(exposureMs * 2 + 1000, 8000);
            }
            catch
            {
                // 读取失败则使用默认 8s
            }

            byte[] jpegBytes = await _tucamService.GrabFrameRawAsync(
                idx,
                timeoutMs: grabTimeoutMs,
                imageRotationAngle: camera.ImageRotationAngle
            );
            string dataUri = "data:image/jpeg;base64," + Convert.ToBase64String(jpegBytes);
            return new CameraSnapshotDto { DataUri = dataUri, CapturedAt = DateTime.UtcNow };
        }
        finally
        {
            if (startedForSnapshot)
            {
                await _tucamService.StopCaptureAsync(idx);
            }
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

        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        int idx = camera.DeviceIndex;
        EnsureCameraOpen(idx);

        await _streamingService.StartPreviewAsync(
            id,
            input.ConnectionId,
            input.EnableRtp,
            camera.ImageRotationAngle
        );
        camera.SetStatus(CameraStatus.Capturing);
        await _cameraDeviceRepository.UpdateAsync(camera);
        Logger.LogInformation("相机 {Id} 开始实时预览", id);
    }

    /// <inheritdoc/>
    public async Task StopPreviewAsync(Guid id)
    {
        if (_streamingService == null)
        {
            return;
        }

        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        await _streamingService.StopPreviewAsync(id);
        camera.SetStatus(
            _tucamService.IsCameraOpen(camera.DeviceIndex)
                ? CameraStatus.Ready
                : CameraStatus.Closed
        );
        await _cameraDeviceRepository.UpdateAsync(camera);
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
    public async Task DoExposureAutoOncePulseAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        await _tucamService.ExecuteGenICamCommandAsync(idx, "ExposureAutoOncePulse");
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

    // ─── 通用 GenICam 节点读写 ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<GenICamNodeResultDto> GetGenICamParamAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromQuery] GenICamNodeGetInput input
    )
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);
        try
        {
            string? value = input.DataType switch
            {
                "float" => (await _tucamService.GetGenICamFloatAsync(idx, input.NodeName)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                "string" => await _tucamService.GetGenICamStringAsync(idx, input.NodeName),
                _ => (await _tucamService.GetGenICamIntAsync(idx, input.NodeName)).ToString(),
            };
            return new GenICamNodeResultDto
            {
                NodeName = input.NodeName,
                Value = value,
                Success = true,
            };
        }
        catch (Exception ex)
        {
            return new GenICamNodeResultDto
            {
                NodeName = input.NodeName,
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <inheritdoc/>
    public async Task<GenICamBatchGetResultDto> BatchGetGenICamParamsAsync(
        Guid id,
        GenICamBatchGetInput input
    )
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        GenICamNodeResultDto[] results = await Task.WhenAll(
            input.Nodes.Select(async node =>
            {
                try
                {
                    string? value = node.DataType switch
                    {
                        "float" => (
                            await _tucamService.GetGenICamFloatAsync(idx, node.NodeName)
                        ).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "string" => await _tucamService.GetGenICamStringAsync(idx, node.NodeName),
                        _ => (
                            await _tucamService.GetGenICamIntAsync(idx, node.NodeName)
                        ).ToString(),
                    };
                    return new GenICamNodeResultDto
                    {
                        NodeName = node.NodeName,
                        Value = value,
                        Success = true,
                    };
                }
                catch (Exception ex)
                {
                    return new GenICamNodeResultDto
                    {
                        NodeName = node.NodeName,
                        Success = false,
                        ErrorMessage = ex.Message,
                    };
                }
            })
        );

        return new GenICamBatchGetResultDto { Results = results.ToList() };
    }

    /// <inheritdoc/>
    public async Task SetGenICamParamAsync(Guid id, GenICamNodeSetInput input)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        switch (input.DataType)
        {
            case "float":
                if (
                    !double.TryParse(
                        input.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double dblVal
                    )
                )
                {
                    throw new UserFriendlyException($"无法将值 '{input.Value}' 解析为浮点数");
                }
                await _tucamService.SetGenICamFloatAsync(idx, input.NodeName, dblVal);
                break;
            case "string":
                await _tucamService.SetGenICamStringAsync(idx, input.NodeName, input.Value);
                break;
            default: // "int" / "enum" / "bool"
                if (!long.TryParse(input.Value, out long longVal))
                {
                    throw new UserFriendlyException($"无法将值 '{input.Value}' 解析为整数");
                }
                await _tucamService.SetGenICamIntAsync(idx, input.NodeName, longVal);
                break;
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteGenICamCommandAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] string nodeName
    )
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);
        await _tucamService.ExecuteGenICamCommandAsync(idx, nodeName);
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
    /// 根据数据库 ID 查询相机的 SDK 索引。
    /// 使用 AsNoTracking 投影查询，避免 EF Core 跟踪完整实体，
    /// 防止在长时间 SDK 操作期间（如图像采集）UoW 完成时触发并发异常。
    /// </summary>
    private async Task<int> GetDeviceIndexAsync(Guid id)
    {
        return await _cameraDeviceRepository.GetDeviceIndexByIdAsync(id);
    }

    // ─── GenICam 动态 NodeMap API（前端动态生成 UI）────────────────────────────

    /// <inheritdoc/>
    public async Task<CameraNodeMapDto> GetNodeMapAsync(Guid id)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        GenICamNodeMap? nodeMap = _tucamService.GetCachedNodeMap(idx);
        GenICamDependencyGraph? depGraph = _tucamService.GetCachedDependencyGraph(idx);

        if (nodeMap == null)
        {
            // 缓存尚未就绪：返回空骨架，前端可轮询或调用 Refresh
            return new CameraNodeMapDto { CameraId = id, EnumeratedAt = DateTime.UtcNow };
        }

        return MapNodeMapToDto(id, nodeMap, depGraph);
    }

    /// <inheritdoc/>
    public async Task<CameraNodeMapDto> RefreshNodeMapAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        await _tucamService.RefreshGenICamNodeMapAsync(idx);

        GenICamNodeMap? nodeMap = _tucamService.GetCachedNodeMap(idx);
        GenICamDependencyGraph? depGraph = _tucamService.GetCachedDependencyGraph(idx);

        if (nodeMap == null)
        {
            throw new UserFriendlyException("GenICam NodeMap 刷新失败，请检查相机连接状态。");
        }

        return MapNodeMapToDto(id, nodeMap, depGraph);
    }

    /// <inheritdoc/>
    public async Task<GenICamBatchGetResultDto> ReadNodesAsync(Guid id, GenICamBatchGetInput input)
    {
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        List<string> names = input
            .Nodes.Select(n => n.NodeName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<GenICamNodeValue> values = await _tucamService.ReadGenICamNodesAsync(
            idx,
            names
        );

        return new GenICamBatchGetResultDto
        {
            Results = values
                .Select(v => new GenICamNodeResultDto
                {
                    NodeName = v.NodeName,
                    Value = v.Value,
                    Success = v.Success,
                    ErrorMessage = v.Error,
                })
                .ToList(),
        };
    }

    /// <summary>将 double 中的 NaN/±Infinity 标准化为可被 JSON 序列化的有限值</summary>
    private static double SanitizeDouble(double value)
    {
        if (double.IsNaN(value))
            return 0d;
        if (double.IsPositiveInfinity(value))
            return double.MaxValue;
        if (double.IsNegativeInfinity(value))
            return double.MinValue;
        return value;
    }

    /// <summary>将 SDK 缓存的 NodeMap 与依赖图映射为前端 DTO</summary>
    private static CameraNodeMapDto MapNodeMapToDto(
        Guid cameraId,
        GenICamNodeMap nodeMap,
        GenICamDependencyGraph? depGraph
    )
    {
        List<GenICamNodeDto> allNodes = nodeMap
            .Nodes.Select(meta => new GenICamNodeDto
            {
                NodeName = meta.NodeName,
                DisplayName = meta.DisplayName,
                XmlScope = meta.XmlScope,
                Level = meta.Level,
                NodeType = meta.Type.ToString(),
                Access = meta.Access.ToString(),
                Visibility = meta.Visibility.ToString(),
                Representation = meta.Representation,
                Unit = meta.Unit,
                Description = meta.Description,
                IsLocked = meta.IsLocked,
                IntMin = meta.IntMin,
                IntMax = meta.IntMax,
                IntStep = meta.IntStep,
                FloatMin = SanitizeDouble(meta.FloatMin),
                FloatMax = SanitizeDouble(meta.FloatMax),
                FloatStep = SanitizeDouble(meta.FloatStep),
                CurrentValue = meta.CurrentValue,
                EnumEntries = meta
                    .EnumEntries.Select(e => new GenICamEnumEntryDto
                    {
                        Value = e.Value,
                        Symbolic = e.Symbolic,
                        DisplayName = e.DisplayName,
                        IsAvailable = e.IsAvailable,
                    })
                    .ToList(),
                PollingTime = meta.PollingTime,
                DisplayPrecision = meta.DisplayPrecision,
            })
            .ToList();

        // 按 Category 节点 + Level 构建层级分组：
        // 节点列表来自 SDK，顺序为先父 Category 再其子节点；
        // 同 XmlScope 内子节点 Level = 父 Category.Level + 1。
        // 算法：维护 Category 栈，遇到 Category 入栈，遇到普通节点归到栈顶 Category。
        string categoryTypeName = TuElemType.Category.ToString();
        List<GenICamCategoryDto> categories = new();
        Dictionary<string, GenICamCategoryDto> categoryByName = new(StringComparer.Ordinal);
        GenICamCategoryDto? fallbackCategory = null;
        Stack<GenICamNodeDto> categoryStack = new();
        int currentXmlScope = int.MinValue;

        GenICamCategoryDto EnsureFallback()
        {
            if (fallbackCategory is null)
            {
                fallbackCategory = new GenICamCategoryDto
                {
                    Name = string.Empty,
                    DisplayName = "其它",
                };
                categories.Add(fallbackCategory);
            }
            return fallbackCategory;
        }

        foreach (GenICamNodeDto node in allNodes)
        {
            // XmlScope 切换时清空栈，避免跨域错位
            if (node.XmlScope != currentXmlScope)
            {
                currentXmlScope = node.XmlScope;
                categoryStack.Clear();
            }

            // 弹出所有 Level >= 当前节点 Level 的 Category（同级或更深）
            while (categoryStack.Count > 0 && categoryStack.Peek().Level >= node.Level)
            {
                categoryStack.Pop();
            }

            if (node.NodeType == categoryTypeName)
            {
                // 创建 Category 容器并入栈；同名 Category 复用
                if (!categoryByName.TryGetValue(node.NodeName, out GenICamCategoryDto? dto))
                {
                    dto = new GenICamCategoryDto
                    {
                        Name = node.NodeName,
                        DisplayName = string.IsNullOrWhiteSpace(node.DisplayName)
                            ? node.NodeName
                            : node.DisplayName,
                    };
                    categories.Add(dto);
                    categoryByName[node.NodeName] = dto;
                }
                categoryStack.Push(node);
                continue;
            }

            if (categoryStack.Count > 0)
            {
                string parentName = categoryStack.Peek().NodeName;
                categoryByName[parentName].Nodes.Add(node);
            }
            else
            {
                EnsureFallback().Nodes.Add(node);
            }
        }

        // 移除空的 Category（仅作为中间层、自身无直属节点的容器）
        categories = categories.Where(c => c.Nodes.Count > 0).ToList();

        List<GenICamDependencyDto> deps =
            depGraph
                ?.Edges.Select(e => new GenICamDependencyDto
                {
                    SelectorNode = e.SelectorNode,
                    OptionValue = e.OptionValue,
                    OptionLabel = e.OptionLabel,
                    AffectedNode = e.AffectedNode,
                    ChangeSummary = e.ChangeSummary,
                })
                .ToList()
            ?? new List<GenICamDependencyDto>();

        return new CameraNodeMapDto
        {
            CameraId = cameraId,
            EnumeratedAt = nodeMap.EnumeratedAt,
            Categories = categories,
            AllNodes = allNodes,
            Dependencies = deps,
        };
    }
}
