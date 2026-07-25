using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Sessions;
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
[Authorize]
public class CameraDeviceAppService : AuroraStruct3DAppService, ICameraDeviceAppService
{
    private readonly ICameraDeviceRepository _cameraDeviceRepository;
    private readonly ICameraOperationLogRepository _operationLogRepository;
    private readonly ICameraParameterSetRepository _parameterSetRepository;
    private readonly ITucamCameraService _tucamService;
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly ICameraStreamingService? _streamingService;
    private readonly IDeviceOperationSessionManager _sessionManager;
    private readonly ICurrentClientSession _currentClientSession;

    public CameraDeviceAppService(
        ICameraDeviceRepository cameraDeviceRepository,
        ICameraOperationLogRepository operationLogRepository,
        ICameraParameterSetRepository parameterSetRepository,
        ITucamCameraService tucamService,
        IDeviceStateManager deviceStateManager,
        IDeviceOperationSessionManager sessionManager,
        ICurrentClientSession currentClientSession,
        ICameraStreamingService? streamingService = null
    )
    {
        _cameraDeviceRepository = cameraDeviceRepository;
        _operationLogRepository = operationLogRepository;
        _parameterSetRepository = parameterSetRepository;
        _tucamService = tucamService;
        _deviceStateManager = deviceStateManager;
        _sessionManager = sessionManager;
        _currentClientSession = currentClientSession;
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
        EnsureManualOrMaintenanceMode();
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
        EnsureManualOrMaintenanceMode();
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
        EnsureManualOrMaintenanceMode();
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
        EnsureManualOrMaintenanceMode();
        int count = await _tucamService.InitializeAsync();

        List<CameraDevice> existingCameras = await _cameraDeviceRepository.GetListAsync();
        var byIndex = existingCameras
            .GroupBy(x => x.DeviceIndex)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.LastModificationTime ?? x.CreationTime).First()
            );
        var bySerial = existingCameras
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceSerialNumber))
            .GroupBy(x => x.DeviceSerialNumber!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.LastModificationTime ?? x.CreationTime).First(),
                StringComparer.Ordinal
            );

        int nextSpareIndex =
            Math.Max(
                count,
                existingCameras.Count == 0 ? 0 : existingCameras.Max(x => x.DeviceIndex) + 1
            ) + 1;

        int AllocateSpareIndex()
        {
            while (byIndex.ContainsKey(nextSpareIndex))
            {
                nextSpareIndex++;
            }

            return nextSpareIndex++;
        }

        async Task EnsureIndexAvailableAsync(int targetIndex, Guid keepCameraId)
        {
            if (!byIndex.TryGetValue(targetIndex, out CameraDevice? occupant))
            {
                return;
            }

            if (occupant.Id == keepCameraId)
            {
                return;
            }

            int spareIndex = AllocateSpareIndex();
            int oldIndex = occupant.DeviceIndex;
            occupant.SetDeviceIndex(spareIndex);
            await _cameraDeviceRepository.UpdateAsync(occupant);

            byIndex.Remove(oldIndex);
            byIndex[spareIndex] = occupant;

            Logger.LogInformation(
                "相机索引冲突处理：将记录 {CameraId} 从索引 {OldIndex} 暂移到备用索引 {SpareIndex}",
                occupant.Id,
                oldIndex,
                spareIndex
            );
        }

        // 扫描过程中同步构建索引→设备ID映射，供操作日志使用
        var deviceIdMap = new Dictionary<int, Guid>();

        for (int i = 0; i < count; i++)
        {
            // 扫描阶段无需打开相机，直接按索引读取型号
            string model = await _tucamService.GetModelByIndexAsync(i);

            // 扫描阶段读取序列号用于稳定识别相机归属
            string? serialNumber = await TryReadSerialNumberByIndexAsync(i);

            CameraDevice? matchedCamera = null;
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                bySerial.TryGetValue(serialNumber, out matchedCamera);
            }

            // 回退策略：仅在“索引位上的旧记录也没有序列号”时按索引匹配。
            // 这样可以避免热插拔后索引漂移把 A 设备误绑定到 B 的历史记录。
            if (
                matchedCamera == null
                && byIndex.TryGetValue(i, out CameraDevice? indexCamera)
                && CanUseIndexFallback(matchedCamera, indexCamera)
            )
            {
                matchedCamera = indexCamera;
            }

            if (matchedCamera == null)
            {
                await EnsureIndexAvailableAsync(i, Guid.Empty);

                var newCamera = new CameraDevice(GuidGenerator.Create(), $"相机 #{i}", i);
                newCamera.UpdateHardwareInfo(model);
                newCamera.UpdateDeviceSerialNumber(serialNumber);
                await _cameraDeviceRepository.InsertAsync(newCamera);

                byIndex[i] = newCamera;
                if (!string.IsNullOrWhiteSpace(newCamera.DeviceSerialNumber))
                {
                    bySerial[newCamera.DeviceSerialNumber] = newCamera;
                }

                Logger.LogInformation(
                    "自动注册相机设备，索引: {Index}，型号: {Model}，序列号: {SerialNumber}",
                    i,
                    model,
                    serialNumber ?? "(空)"
                );
                deviceIdMap[i] = newCamera.Id;
                continue;
            }

            bool updated = false;
            int oldMatchedIndex = matchedCamera.DeviceIndex;
            string? oldMatchedSerial = matchedCamera.DeviceSerialNumber;

            if (matchedCamera.DeviceIndex != i)
            {
                await EnsureIndexAvailableAsync(i, matchedCamera.Id);
                matchedCamera.SetDeviceIndex(i);
                updated = true;
            }

            if (!string.Equals(matchedCamera.Model, model, StringComparison.Ordinal))
            {
                matchedCamera.UpdateHardwareInfo(model);
                updated = true;
            }

            if (
                !string.Equals(
                    matchedCamera.DeviceSerialNumber,
                    serialNumber,
                    StringComparison.Ordinal
                )
            )
            {
                matchedCamera.UpdateDeviceSerialNumber(serialNumber);
                updated = true;
            }

            if (updated)
            {
                await _cameraDeviceRepository.UpdateAsync(matchedCamera);

                if (oldMatchedIndex != matchedCamera.DeviceIndex)
                {
                    byIndex.Remove(oldMatchedIndex);
                    byIndex[matchedCamera.DeviceIndex] = matchedCamera;
                }

                if (
                    !string.IsNullOrWhiteSpace(oldMatchedSerial)
                    && !string.Equals(
                        oldMatchedSerial,
                        matchedCamera.DeviceSerialNumber,
                        StringComparison.Ordinal
                    )
                )
                {
                    bySerial.Remove(oldMatchedSerial);
                }

                if (!string.IsNullOrWhiteSpace(matchedCamera.DeviceSerialNumber))
                {
                    bySerial[matchedCamera.DeviceSerialNumber] = matchedCamera;
                }

                Logger.LogInformation(
                    "更新相机记录，ID: {CameraId}，索引: {Index}，型号: {Model}，序列号: {SerialNumber}",
                    matchedCamera.Id,
                    i,
                    model,
                    serialNumber ?? "(空)"
                );
            }

            deviceIdMap[i] = matchedCamera.Id;
        }

        // 扫描完成后刷新 TucamCameraService 的操作日志映射，防止外键违规
        _tucamService.SetCameraDeviceIdMapping(deviceIdMap);

        return count;
    }

    private static bool CanUseIndexFallback(
        CameraDevice? matchedBySerial,
        CameraDevice? indexCamera
    )
    {
        if (matchedBySerial != null || indexCamera == null)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(indexCamera.DeviceSerialNumber);
    }

    /// <inheritdoc/>
    public async Task OpenCameraAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);

        await _tucamService.OpenCameraAsync(camera.DeviceIndex);

        // 读取硬件信息并更新数据库
        string model = await _tucamService.GetCameraModelAsync(camera.DeviceIndex);
        string? serialNumber = await TryReadSerialNumberFromOpenCameraAsync(camera.DeviceIndex);
        camera.UpdateHardwareInfo(model);

        // DeviceSerialNumber 在库内唯一；若当前读到的序列号已属于其他相机记录，
        // 则跳过本次写入，避免触发唯一约束异常导致接口 500。
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            camera.UpdateDeviceSerialNumber(null);
        }
        else
        {
            CameraDevice? serialOwner = await _cameraDeviceRepository.FindByDeviceSerialNumberAsync(
                serialNumber
            );

            if (serialOwner != null && serialOwner.Id != camera.Id)
            {
                Logger.LogWarning(
                    "[Cameras] Skip updating DeviceSerialNumber for camera {CameraId}. "
                        + "Serial '{SerialNumber}' already belongs to camera {OwnerCameraId}.",
                    camera.Id,
                    serialNumber,
                    serialOwner.Id
                );
            }
            else
            {
                camera.UpdateDeviceSerialNumber(serialNumber);
            }
        }

        camera.SetStatus(CameraStatus.Ready);
        await _cameraDeviceRepository.UpdateAsync(camera);
    }

    /// <inheritdoc/>
    public async Task CloseCameraAsync(Guid id)
    {
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);

        if (_streamingService?.IsPreviewActive(id) == true)
        {
            await _streamingService.StopPreviewAsync(id);
        }

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

    /// <summary>
    /// 扫描阶段按索引临时打开相机读取 DeviceSerialNumber，读取后立即关闭。
    /// </summary>
    private async Task<string?> TryReadSerialNumberByIndexAsync(int deviceIndex)
    {
        try
        {
            await _tucamService.OpenCameraAsync(deviceIndex);
            try
            {
                return await TryReadSerialNumberFromOpenCameraAsync(deviceIndex);
            }
            finally
            {
                await _tucamService.CloseCameraAsync(deviceIndex);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "扫描阶段读取相机序列号失败，索引: {Index}，将回退索引匹配",
                deviceIndex
            );
            return null;
        }
    }

    /// <summary>
    /// 在相机已打开时读取 DeviceSerialNumber，并做空值归一化。
    /// </summary>
    private async Task<string?> TryReadSerialNumberFromOpenCameraAsync(int deviceIndex)
    {
        string? serialNumber = await _tucamService.GetGenICamStringAsync(
            deviceIndex,
            "DeviceSerialNumber"
        );

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return null;
        }

        return serialNumber.Trim();
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
    public async Task UpdateImageRotationAngleAsync(Guid id, SetCameraRotationAngleDto input)
    {
        EnsureManualOrMaintenanceMode();
        await EnsureOrAcquireSessionAsync(id, DeviceType.Camera);
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
        await EnsureOrAcquireSessionAsync(id, DeviceType.Camera);
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        int idx = camera.DeviceIndex;
        EnsureCameraOpen(idx);

        // 读取当前 TriggerMode 以决定抓帧策略（0=FreeRunning, 1=Standard(外触发), 2=Software）
        long triggerMode = 0;
        try
        {
            triggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
        }
        catch
        {
            // 读取失败按 FreeRunning 处理
        }

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

            // Software 触发模式：必须先发软件触发，否则 WaitForFrame 永远不会返回
            if (triggerMode == 2)
            {
                await _tucamService.DoSoftwareTriggerAsync(idx);
            }
            else if (triggerMode == 1)
            {
                // Standard 外触发：依赖外部硬件触发信号，若长时间无信号给出友好提示
                // 这里仍正常等待，但拉长超时到 15s
                grabTimeoutMs = Math.Max(grabTimeoutMs, 15000);
            }

            byte[] jpegBytes;
            try
            {
                jpegBytes = await _tucamService.GrabFrameRawAsync(
                    idx,
                    timeoutMs: grabTimeoutMs,
                    imageRotationAngle: camera.ImageRotationAngle
                );
            }
            catch (InvalidOperationException ex)
            {
                bool isExternalTriggerTimeout =
                    triggerMode == 1
                    && ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase);
                if (isExternalTriggerTimeout)
                {
                    throw new UserFriendlyException(
                        "外触发模式下未在超时时间内收到硬件触发信号，请确认触发线路正常或切换至自由运行模式后再试。"
                    );
                }
                throw;
            }

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
        await EnsureOrAcquireSessionAsync(id, DeviceType.Camera);

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
            camera.ImageRotationAngle,
            clientSessionId: _currentClientSession.SessionId
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
        await EnsureOrAcquireSessionAsync(id, DeviceType.Camera);
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        // 仅 Software 触发模式（TriggerMode=2）下允许发送软件触发
        long triggerMode = 0;
        try
        {
            triggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
        }
        catch
        {
            // 读取失败按未知处理，仍允许触发避免误拦截
        }

        if (triggerMode != 2)
        {
            throw new UserFriendlyException(
                "当前 TriggerMode 不是 Software（软件触发）模式，发送软件触发无效。请先将 TriggerMode 切换为 Software。"
            );
        }

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
        await EnsureOrAcquireSessionAsync(id, DeviceType.Camera);
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);

        // 收集本次实际写入成功的节点名，稍后聚合受影响节点统一回读 + SignalR 推送
        HashSet<string> writtenNodes = new(StringComparer.Ordinal);

        // ─── 批量模式：JSON 含 nodes 数组，类型由 NodeMap 缓存自动推断 ────────────
        if (input.Nodes is { Count: > 0 })
        {
            GenICamNodeMap? nodeMap = _tucamService.GetCachedNodeMap(idx);
            foreach (GenICamBatchSetItem item in input.Nodes)
            {
                if (string.IsNullOrWhiteSpace(item.NodeName))
                {
                    continue;
                }

                TuElemType? nodeType = null;
                if (
                    nodeMap is not null
                    && nodeMap.NodesByName.TryGetValue(item.NodeName, out GenICamNodeMeta? meta)
                )
                {
                    nodeType = meta.Type;
                }

                // Command / Category / Port 节点不参与写入，静默跳过
                if (nodeType is TuElemType.Command or TuElemType.Category or TuElemType.Port)
                {
                    Logger.LogDebug("批量 Set 跳过 {Type} 节点: {Name}", nodeType, item.NodeName);
                    continue;
                }

                try
                {
                    await SetSingleNodeByTypeAsync(idx, item.NodeName, item.Value, nodeType);
                    writtenNodes.Add(item.NodeName);
                }
                catch (Exception ex)
                {
                    // 单个节点失败不阻断批量，记录警告后继续
                    Logger.LogWarning(
                        ex,
                        "批量 Set {Name}='{Value}' 失败，跳过继续",
                        item.NodeName,
                        item.Value
                    );
                }
            }

            await PushAffectedNodeChangesAsync(id, idx, writtenNodes);
            return;
        }

        // ─── 单节点模式：JSON 含 nodeName / dataType / value ──────────────────────
        if (string.IsNullOrWhiteSpace(input.NodeName))
        {
            throw new UserFriendlyException("未提供 nodeName，也未提供 nodes 列表");
        }

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
                await _tucamService.SetGenICamStringAsync(
                    idx,
                    input.NodeName,
                    input.Value ?? string.Empty
                );
                break;
            default: // "int" / "enum" / "bool"
                if (!long.TryParse(input.Value, out long longVal))
                {
                    throw new UserFriendlyException($"无法将值 '{input.Value}' 解析为整数");
                }
                await _tucamService.SetGenICamIntAsync(idx, input.NodeName, longVal);
                break;
        }

        writtenNodes.Add(input.NodeName);
        await PushAffectedNodeChangesAsync(id, idx, writtenNodes);
    }

    /// <summary>
    /// 聚合本次写入节点 + 依赖图中受影响节点，批量回读后通过 SignalR 推送增量变更
    /// </summary>
    private async Task PushAffectedNodeChangesAsync(
        Guid cameraId,
        int idx,
        HashSet<string> writtenNodes
    )
    {
        if (writtenNodes.Count == 0 || _streamingService == null)
        {
            return;
        }

        try
        {
            // 聚合自身 + 依赖图中受影响节点
            HashSet<string> targets = new(writtenNodes, StringComparer.Ordinal);
            GenICamDependencyGraph? graph = _tucamService.GetCachedDependencyGraph(idx);
            if (graph is not null)
            {
                foreach (string written in writtenNodes)
                {
                    if (
                        graph.AffectedBySelector.TryGetValue(
                            written,
                            out IReadOnlyList<string>? affected
                        )
                    )
                    {
                        foreach (string a in affected)
                        {
                            targets.Add(a);
                        }
                    }
                }
            }

            IReadOnlyList<GenICamNodeValue> values = await _tucamService.ReadGenICamNodesAsync(
                idx,
                targets.ToList()
            );

            // 将回读结果同步写回服务端 NodeMap 缓存，保持与硬件一致，
            // 避免页面刷新时从缓存读到枚举时的过期初始值
            _tucamService.UpdateCachedNodeValues(idx, values);

            List<GenICamNodeChangeDto> changes = values
                .Where(v => v.Success)
                .Select(v => new GenICamNodeChangeDto
                {
                    NodeName = v.NodeName,
                    Value = v.Value,
                    Access = v.Access.ToString(),
                    IsLocked = v.IsLocked,
                })
                .ToList();

            if (changes.Count > 0)
            {
                await _streamingService.NotifyGenICamNodesChangedAsync(cameraId, changes);
            }
        }
        catch (Exception ex)
        {
            // 推送失败不影响 Set 操作主流程
            Logger.LogWarning(ex, "相机 {Id} 推送 GenICam 节点变更失败", cameraId);
        }
    }

    /// <summary>
    /// 按节点类型（来自 NodeMap 缓存或 Fallback 推断）写入单个节点
    /// </summary>
    private async Task SetSingleNodeByTypeAsync(
        int idx,
        string nodeName,
        string value,
        TuElemType? nodeType
    )
    {
        if (nodeType == TuElemType.Float)
        {
            if (
                !double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double dbl
                )
            )
            {
                Logger.LogWarning("Set {Name} 失败：无法解析浮点值 '{Value}'", nodeName, value);
                return;
            }
            await _tucamService.SetGenICamFloatAsync(idx, nodeName, dbl);
        }
        else if (nodeType == TuElemType.String)
        {
            await _tucamService.SetGenICamStringAsync(idx, nodeName, value);
        }
        else if (nodeType is not null)
        {
            // Integer / Enumeration / Boolean 均用整数写入
            if (!long.TryParse(value, out long lng))
            {
                Logger.LogWarning("Set {Name} 失败：无法解析整数值 '{Value}'", nodeName, value);
                return;
            }
            await _tucamService.SetGenICamIntAsync(idx, nodeName, lng);
        }
        else
        {
            // NodeMap 缓存缺失时 Fallback：long → double → string
            if (long.TryParse(value, out long lngFb))
            {
                await _tucamService.SetGenICamIntAsync(idx, nodeName, lngFb);
            }
            else if (
                double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double dblFb
                )
            )
            {
                await _tucamService.SetGenICamFloatAsync(idx, nodeName, dblFb);
            }
            else
            {
                await _tucamService.SetGenICamStringAsync(idx, nodeName, value);
            }
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteGenICamCommandAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] string nodeName
    )
    {
        EnsureManualOrMaintenanceMode();
        await EnsureOrAcquireSessionAsync(id, DeviceType.Camera);
        int idx = await GetDeviceIndexAsync(id);
        EnsureCameraOpen(idx);
        await _tucamService.ExecuteGenICamCommandAsync(idx, nodeName);
    }

    // ─── 辅助方法 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 检查并获取设备独占会话。
    /// 若当前标签页已持有该设备会话，则续期并返回；若设备无人，则自动获取会话。
    /// 若设备已被其他标签页占用，则抛出 <see cref="DeviceOccupiedException"/>（HTTP 409）。
    /// </summary>
    private Task EnsureOrAcquireSessionAsync(Guid deviceId, DeviceType deviceType)
    {
        string? clientSessionId = _currentClientSession.SessionId;
        if (string.IsNullOrWhiteSpace(clientSessionId))
        {
            // 无 clientSessionId（如内部调用）：跳过独占检查
            return Task.CompletedTask;
        }

        string? userId = CurrentUser.Id?.ToString();
        string userName = CurrentUser.Name ?? CurrentUser.UserName ?? clientSessionId[..8] + "...";

        _sessionManager.TryAcquire(
            deviceId,
            deviceType,
            clientSessionId,
            userId,
            userName,
            force: false
        );

        return Task.CompletedTask;
    }

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

        // NodeMap 整体重新枚举完成，广播给所有客户端，由前端拉取最新整表
        if (_streamingService != null)
        {
            try
            {
                await _streamingService.NotifyGenICamNodeMapReloadedAsync(id, nodeMap.EnumeratedAt);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "相机 {Id} 推送 NodeMap reload 事件失败", id);
            }
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
                    // 读取成功时返回最新 Access，便于前端无需重新枚举即可同步节点可写状态
                    Access = v.Success ? v.Access.ToString() : null,
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

        // 按 Category 节点 + Level 构建多级嵌套分组树：
        // 节点列表来自 SDK，顺序为先父 Category 再其子节点；
        // 同 XmlScope 内子节点 Level = 父 Category.Level + 1。
        // 算法：维护带 Level 的 Category 栈，遇到 Category 时入栈并挂载到正确的父节点，
        //       遇到普通节点时归到栈顶 Category 的 Nodes 列表。
        string categoryTypeName = TuElemType.Category.ToString();
        // rootCategories：顶层 Category 列表（Level 最低的 Category）
        List<GenICamCategoryDto> rootCategories = new();
        // categoryStack：(dto, level) 栈，用于追踪当前活跃的 Category 层级
        Stack<(GenICamCategoryDto Dto, int Level)> categoryStack = new();
        Dictionary<string, GenICamCategoryDto> categoryByName = new(StringComparer.Ordinal);
        GenICamCategoryDto? fallbackCategory = null;
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
                rootCategories.Add(fallbackCategory);
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

            // 弹出所有 Level >= 当前节点 Level 的 Category（同级或更深），维持栈的单调递增
            while (categoryStack.Count > 0 && categoryStack.Peek().Level >= node.Level)
            {
                categoryStack.Pop();
            }

            if (node.NodeType == categoryTypeName)
            {
                // 创建 Category 容器；同名 Category 复用（跨 XmlScope 可能重名）
                if (!categoryByName.TryGetValue(node.NodeName, out GenICamCategoryDto? dto))
                {
                    dto = new GenICamCategoryDto
                    {
                        Name = node.NodeName,
                        DisplayName = string.IsNullOrWhiteSpace(node.DisplayName)
                            ? node.NodeName
                            : node.DisplayName,
                    };
                    categoryByName[node.NodeName] = dto;
                }

                if (categoryStack.Count > 0)
                {
                    // 有父 Category：加入父节点的 Children 列表（避免重复添加）
                    GenICamCategoryDto parent = categoryStack.Peek().Dto;
                    if (!parent.Children.Contains(dto))
                    {
                        parent.Children.Add(dto);
                    }
                }
                else
                {
                    // 无父 Category：加入顶层列表（避免重复添加）
                    if (!rootCategories.Contains(dto))
                    {
                        rootCategories.Add(dto);
                    }
                }

                categoryStack.Push((dto, node.Level));
                continue;
            }

            // 普通节点：归到栈顶 Category 的 Nodes；无栈顶则归到 fallback
            if (categoryStack.Count > 0)
            {
                categoryStack.Peek().Dto.Nodes.Add(node);
            }
            else
            {
                EnsureFallback().Nodes.Add(node);
            }
        }

        // 递归过滤：移除自身无叶子节点且无子分类的空 Category
        static bool HasContent(GenICamCategoryDto cat)
        {
            if (cat.Nodes.Count > 0)
                return true;
            cat.Children = cat.Children.Where(HasContent).ToList();
            return cat.Children.Count > 0;
        }
        List<GenICamCategoryDto> categories = rootCategories.Where(HasContent).ToList();

        List<GenICamDependencyDto> deps =
            depGraph
                ?.Edges.Select(e => new GenICamDependencyDto
                {
                    SelectorNode = e.SelectorNode,
                    OptionValue = e.OptionValue,
                    OptionLabel = e.OptionLabel,
                    AffectedNode = e.AffectedNode,
                    ChangeSummary = e.ChangeSummary,
                    NewAccess = e.NewAccess,
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

    /// <inheritdoc/>
    public async Task<CameraSnapshotStateDto> GetCameraSnapshotStateAsync(Guid id)
    {
        // 1. 基础校验与索引解析
        CameraDevice camera = await _cameraDeviceRepository.GetAsync(id);
        int idx = camera.DeviceIndex;

        CameraSnapshotStateDto snapshot = new CameraSnapshotStateDto
        {
            CameraId = id,
            CameraStatus = camera.Status.ToString(),
            SnapshotAt = DateTime.UtcNow,
        };

        // 相机未打开则只回填基础状态，避免触发任何 SDK 读写
        if (!_tucamService.IsCameraOpen(idx))
        {
            return snapshot;
        }

        // 2. NodeMap 快照（缓存若未就绪返回空骨架，与 GetNodeMapAsync 行为一致）
        GenICamNodeMap? nodeMap = _tucamService.GetCachedNodeMap(idx);
        GenICamDependencyGraph? depGraph = _tucamService.GetCachedDependencyGraph(idx);
        snapshot.NodeMap = nodeMap is not null
            ? MapNodeMapToDto(id, nodeMap, depGraph)
            : new CameraNodeMapDto { CameraId = id, EnumeratedAt = DateTime.UtcNow };

        // 3. TriggerMode（读取失败回退为 0=FreeRunning）
        try
        {
            snapshot.TriggerMode = await _tucamService.GetGenICamIntAsync(idx, "TriggerMode");
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "相机 {Id} 读取 TriggerMode 失败，按 FreeRunning 处理", id);
            snapshot.TriggerMode = 0;
        }
        snapshot.TriggerModeSymbol = snapshot.TriggerMode switch
        {
            0 => "FreeRunning",
            1 => "Standard",
            2 => "Software",
            _ => "Unknown",
        };

        // 4. 运行态：预览/采集/RTP 端点
        snapshot.IsPreviewing = _streamingService?.IsPreviewActive(id) ?? false;
        snapshot.IsCapturing = _tucamService.IsCapturing(idx);
        snapshot.RtpEndpoint = _streamingService?.GetRtpEndpoint(id);

        return snapshot;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CameraOperationLogDto>> GetLogsAsync(GetCameraLogListDto input)
    {
        if (!input.CameraDeviceId.HasValue)
            return new PagedResultDto<CameraOperationLogDto>(0, new List<CameraOperationLogDto>());

        Guid deviceId = input.CameraDeviceId.Value;
        long totalCount = await _operationLogRepository.GetCountAsync(
            deviceId,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );
        List<CameraOperationLog> logs = await _operationLogRepository.GetPagedListAsync(
            deviceId,
            input.SkipCount,
            input.MaxResultCount,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );
        return new PagedResultDto<CameraOperationLogDto>(
            totalCount,
            logs.Select(x => x.ToDto()).ToList()
        );
    }
}
