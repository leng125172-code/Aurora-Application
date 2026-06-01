using AuroraStruct3D.CalibrationManagement.Devices.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.CalibrationManagement.Devices;

/// <summary>
/// 标定设备应用服务实现。
/// 全部子集合（绑定/云台/规则/参数）均在本服务内通过聚合根方法访问，避免破坏聚合边界。
/// 查询时使用 <see cref="IRepository{TEntity, TKey}.GetQueryableAsync"/> + Include 加载子集合。
/// </summary>
[Authorize(CalibrationPermissions.Device)]
public class CalibrationDeviceAppService : AuroraStruct3DAppService, ICalibrationDeviceAppService
{
    private readonly ICalibrationDeviceRepository _deviceRepository;

    /// <summary>构造函数</summary>
    public CalibrationDeviceAppService(ICalibrationDeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 设备 CRUD
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibrationDeviceListDto>> GetListAsync(
        GetCalibrationDeviceListInput input
    )
    {
        int totalCount = await _deviceRepository.GetCountAsync(
            input.Filter,
            input.DeviceType,
            input.IsActive
        );

        List<CalibrationDevice> devices = await _deviceRepository.GetListAsync(
            input.Filter,
            input.DeviceType,
            input.IsActive,
            input.SkipCount,
            input.MaxResultCount,
            input.Sorting
        );

        List<CalibrationDeviceListDto> items = devices.Select(MapToListDto).ToList();
        return new PagedResultDto<CalibrationDeviceListDto>(totalCount, items);
    }

    /// <inheritdoc/>
    public async Task<CalibrationDeviceDetailDto> GetAsync(Guid id)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(id);
        return MapToDetailDto(device);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceCreate)]
    public async Task<CalibrationDeviceDetailDto> CreateAsync(CreateUpdateCalibrationDeviceDto input)
    {
        CalibrationDevice device = new(
            GuidGenerator.Create(),
            input.Name,
            input.DeviceType,
            input.Description
        );
        await _deviceRepository.InsertAsync(device, autoSave: true);
        return MapToDetailDto(device);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationDeviceDetailDto> UpdateAsync(
        Guid id,
        CreateUpdateCalibrationDeviceDto input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(id);
        device.UpdateBasicInfo(input.Name, input.Description);
        if (device.DeviceType != input.DeviceType)
        {
            // 切换拓扑：当前实现不级联清理子集合，由前端引导操作员先解绑后切换
            device.ChangeDeviceType(input.DeviceType);
        }
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToDetailDto(device);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceDelete)]
    public async Task DeleteAsync(Guid id)
    {
        CalibrationDevice device = await _deviceRepository.GetAsync(id);
        await _deviceRepository.DeleteAsync(device, autoSave: true);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationDeviceDetailDto> SetActiveAsync(
        Guid id,
        SetCalibrationDeviceActiveInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(id);
        device.SetActive(input.IsActive);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToDetailDto(device);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 相机 / 电机 / 投射器绑定
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceBind)]
    public async Task<CalibrationCameraBindingDto> UpsertCameraBindingAsync(
        Guid deviceId,
        UpsertCameraBindingInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);

        // 相同角色已存在则移除（按"角色唯一"语义）
        CalibrationCameraBinding? existing = device.CameraBindings.FirstOrDefault(x =>
            x.Role == input.Role
        );
        if (existing is not null)
        {
            device.CameraBindings.Remove(existing);
        }

        CalibrationCameraBinding binding = new(
            GuidGenerator.Create(),
            deviceId,
            input.CameraDeviceId,
            input.Role
        );
        device.CameraBindings.Add(binding);
        await _deviceRepository.UpdateAsync(device, autoSave: true);

        return MapToBindingDto(binding);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceBind)]
    public async Task RemoveCameraBindingAsync(Guid deviceId, Guid bindingId)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationCameraBinding binding =
            device.CameraBindings.FirstOrDefault(x => x.Id == bindingId)
            ?? throw new EntityNotFoundException(typeof(CalibrationCameraBinding), bindingId);
        device.CameraBindings.Remove(binding);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceBind)]
    public async Task<CalibrationMotorBindingDto> UpsertMotorBindingAsync(
        Guid deviceId,
        UpsertMotorBindingInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);

        CalibrationMotorBinding? existing = device.MotorBindings.FirstOrDefault(x =>
            x.Role == input.Role && x.GimbalGroupId == input.GimbalGroupId
        );
        if (existing is not null)
        {
            device.MotorBindings.Remove(existing);
        }

        CalibrationMotorBinding binding = new(
            GuidGenerator.Create(),
            deviceId,
            input.MotorAxisId,
            input.Role,
            input.GimbalGroupId
        );
        device.MotorBindings.Add(binding);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToBindingDto(binding);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceBind)]
    public async Task RemoveMotorBindingAsync(Guid deviceId, Guid bindingId)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationMotorBinding binding =
            device.MotorBindings.FirstOrDefault(x => x.Id == bindingId)
            ?? throw new EntityNotFoundException(typeof(CalibrationMotorBinding), bindingId);
        device.MotorBindings.Remove(binding);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceBind)]
    public async Task<CalibrationProjectorBindingDto> UpsertProjectorBindingAsync(
        Guid deviceId,
        UpsertProjectorBindingInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        if (!CalibrationDevice.HasStructuredLight(device.DeviceType))
        {
            throw new BusinessException("Calibration:DeviceTypeNotSupportProjector")
                .WithData("deviceType", device.DeviceType);
        }

        CalibrationProjectorBinding? existing = device.ProjectorBindings.FirstOrDefault(x =>
            x.Role == input.Role
        );
        if (existing is not null)
        {
            device.ProjectorBindings.Remove(existing);
        }

        CalibrationProjectorBinding binding = new(
            GuidGenerator.Create(),
            deviceId,
            input.ProjectorDeviceId,
            input.Role
        );
        device.ProjectorBindings.Add(binding);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToBindingDto(binding);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceBind)]
    public async Task RemoveProjectorBindingAsync(Guid deviceId, Guid bindingId)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationProjectorBinding binding =
            device.ProjectorBindings.FirstOrDefault(x => x.Id == bindingId)
            ?? throw new EntityNotFoundException(typeof(CalibrationProjectorBinding), bindingId);
        device.ProjectorBindings.Remove(binding);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 云台组
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationGimbalGroupDto> CreateGimbalGroupAsync(
        Guid deviceId,
        CreateUpdateGimbalGroupInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationGimbalGroup group = new(
            GuidGenerator.Create(),
            deviceId,
            input.Name,
            input.XAxisMotorId,
            input.YAxisMotorId,
            input.ZRotateAxisMotorId,
            input.ZTranslateAxisMotorId
        );
        group.SetMotionParameters(input.MaxVelocity, input.Acceleration, input.AccelDecelTime);
        device.GimbalGroups.Add(group);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToGimbalGroupDto(group);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationGimbalGroupDto> UpdateGimbalGroupAsync(
        Guid deviceId,
        Guid groupId,
        CreateUpdateGimbalGroupInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationGimbalGroup group =
            device.GimbalGroups.FirstOrDefault(x => x.Id == groupId)
            ?? throw new EntityNotFoundException(typeof(CalibrationGimbalGroup), groupId);

        // 云台组重命名 / 替换轴：采用替换方式（移除 + 新建），保留预设位置
        device.GimbalGroups.Remove(group);
        CalibrationGimbalGroup updated = new(
            groupId,
            deviceId,
            input.Name,
            input.XAxisMotorId,
            input.YAxisMotorId,
            input.ZRotateAxisMotorId,
            input.ZTranslateAxisMotorId
        );
        updated.SetMotionParameters(input.MaxVelocity, input.Acceleration, input.AccelDecelTime);
        foreach (CalibrationGimbalPreset preset in group.PresetPositions)
        {
            updated.PresetPositions.Add(preset);
        }
        device.GimbalGroups.Add(updated);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToGimbalGroupDto(updated);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task DeleteGimbalGroupAsync(Guid deviceId, Guid groupId)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationGimbalGroup group =
            device.GimbalGroups.FirstOrDefault(x => x.Id == groupId)
            ?? throw new EntityNotFoundException(typeof(CalibrationGimbalGroup), groupId);
        device.GimbalGroups.Remove(group);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationGimbalPresetDto> AddGimbalPresetAsync(
        Guid deviceId,
        Guid groupId,
        CreateGimbalPresetInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationGimbalGroup group =
            device.GimbalGroups.FirstOrDefault(x => x.Id == groupId)
            ?? throw new EntityNotFoundException(typeof(CalibrationGimbalGroup), groupId);

        CalibrationGimbalPreset preset = new(
            GuidGenerator.Create(),
            groupId,
            input.Name,
            input.XPosition,
            input.YPosition,
            input.ZRotatePosition,
            input.ZTranslatePosition,
            input.Remarks
        );
        group.PresetPositions.Add(preset);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToGimbalPresetDto(preset);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task RemoveGimbalPresetAsync(Guid deviceId, Guid groupId, Guid presetId)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationGimbalGroup group =
            device.GimbalGroups.FirstOrDefault(x => x.Id == groupId)
            ?? throw new EntityNotFoundException(typeof(CalibrationGimbalGroup), groupId);
        CalibrationGimbalPreset preset =
            group.PresetPositions.FirstOrDefault(x => x.Id == presetId)
            ?? throw new EntityNotFoundException(typeof(CalibrationGimbalPreset), presetId);
        group.PresetPositions.Remove(preset);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 互锁规则
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationMotorInterlockRuleDto> CreateInterlockRuleAsync(
        Guid deviceId,
        CreateUpdateInterlockRuleInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationMotorInterlockRule rule = new(
            GuidGenerator.Create(),
            deviceId,
            input.SourceMotorAxisId,
            input.SourcePositionMin,
            input.SourcePositionMax,
            input.TargetMotorAxisId,
            input.BlockedDirection,
            input.Description
        );
        if (!input.IsEnabled)
        {
            rule.SetEnabled(false);
        }
        device.InterlockRules.Add(rule);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToInterlockRuleDto(rule);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationMotorInterlockRuleDto> UpdateInterlockRuleAsync(
        Guid deviceId,
        Guid ruleId,
        CreateUpdateInterlockRuleInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationMotorInterlockRule rule =
            device.InterlockRules.FirstOrDefault(x => x.Id == ruleId)
            ?? throw new EntityNotFoundException(typeof(CalibrationMotorInterlockRule), ruleId);
        device.InterlockRules.Remove(rule);

        CalibrationMotorInterlockRule updated = new(
            ruleId,
            deviceId,
            input.SourceMotorAxisId,
            input.SourcePositionMin,
            input.SourcePositionMax,
            input.TargetMotorAxisId,
            input.BlockedDirection,
            input.Description
        );
        updated.SetEnabled(input.IsEnabled);
        device.InterlockRules.Add(updated);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToInterlockRuleDto(updated);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task DeleteInterlockRuleAsync(Guid deviceId, Guid ruleId)
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        CalibrationMotorInterlockRule rule =
            device.InterlockRules.FirstOrDefault(x => x.Id == ruleId)
            ?? throw new EntityNotFoundException(typeof(CalibrationMotorInterlockRule), ruleId);
        device.InterlockRules.Remove(rule);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 硬件参数
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationCameraParameterDto> UpsertCameraParameterAsync(
        Guid deviceId,
        CreateUpdateCameraParameterInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);

        CalibrationCameraParameter? existing = device.CameraParameters.FirstOrDefault(x =>
            x.CameraDeviceId == input.CameraDeviceId
        );
        if (existing is not null)
        {
            device.CameraParameters.Remove(existing);
        }

        CalibrationCameraParameter param = new(
            GuidGenerator.Create(),
            deviceId,
            input.CameraDeviceId,
            input.Role
        );
        param.SetCmos(input.CmosSize, input.CmosWidthMm, input.CmosHeightMm);
        param.SetResolution(input.ResolutionWidthPx, input.ResolutionHeightPx);
        param.SetLens(
            input.NominalFocalLengthMm,
            input.MaxAperture,
            input.MinAperture,
            input.CurrentAperture
        );
        param.SetExposureGain(
            input.MinExposureUs,
            input.MaxExposureUs,
            input.MinGainDb,
            input.MaxGainDb
        );

        device.CameraParameters.Add(param);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToCameraParameterDto(param);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationProjectorParameterDto> UpsertProjectorParameterAsync(
        Guid deviceId,
        CreateUpdateProjectorParameterInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);
        if (!CalibrationDevice.HasStructuredLight(device.DeviceType))
        {
            throw new BusinessException("Calibration:DeviceTypeNotSupportProjector")
                .WithData("deviceType", device.DeviceType);
        }

        CalibrationProjectorParameter? existing = device.ProjectorParameters.FirstOrDefault(x =>
            x.ProjectorDeviceId == input.ProjectorDeviceId
        );
        if (existing is not null)
        {
            device.ProjectorParameters.Remove(existing);
        }

        CalibrationProjectorParameter param = new(
            GuidGenerator.Create(),
            deviceId,
            input.ProjectorDeviceId
        );
        param.SetResolution(input.ResolutionWidthPx, input.ResolutionHeightPx);
        param.SetOptics(input.ThrowRatio, input.MinWorkingDistanceMm, input.MaxWorkingDistanceMm);
        param.SetPattern(input.Pattern, input.PatternCount, input.PhaseShift);

        device.ProjectorParameters.Add(param);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToProjectorParameterDto(param);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.DeviceUpdate)]
    public async Task<CalibrationMotorParameterDto> UpsertMotorParameterAsync(
        Guid deviceId,
        CreateUpdateMotorParameterInput input
    )
    {
        CalibrationDevice device = await LoadDeviceWithChildrenAsync(deviceId);

        CalibrationMotorParameter? existing = device.MotorParameters.FirstOrDefault(x =>
            x.MotorAxisId == input.MotorAxisId
        );
        if (existing is not null)
        {
            device.MotorParameters.Remove(existing);
        }

        CalibrationMotorParameter param = new(
            GuidGenerator.Create(),
            deviceId,
            input.MotorAxisId,
            input.Role
        );
        param.SetMechanical(input.EncoderResolution, input.GearRatio);
        param.SetHoming(
            input.HomePosition,
            input.HomeDirection,
            input.HomingVelocity,
            input.HomingAcceleration
        );
        param.SetSoftLimits(input.SoftLimitMin, input.SoftLimitMax);

        device.MotorParameters.Add(param);
        await _deviceRepository.UpdateAsync(device, autoSave: true);
        return MapToMotorParameterDto(param);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 私有辅助方法
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 加载标定设备并预加载全部子集合（包括嵌套的云台预设）。
    /// </summary>
    private async Task<CalibrationDevice> LoadDeviceWithChildrenAsync(Guid id)
    {
        CalibrationDevice? device = await _deviceRepository.FindWithDetailsAsync(id);
        return device ?? throw new EntityNotFoundException(typeof(CalibrationDevice), id);
    }

    // ── 实体 → DTO 映射 ─────────────────────────────────────────────────

    private static CalibrationDeviceListDto MapToListDto(CalibrationDevice entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            Name = entity.Name,
            Description = entity.Description,
            DeviceType = entity.DeviceType,
            IsActive = entity.IsActive,
            CameraBindingCount = entity.CameraBindings.Count,
            MotorBindingCount = entity.MotorBindings.Count,
            ProjectorBindingCount = entity.ProjectorBindings.Count,
            HasStructuredLight = CalibrationDevice.HasStructuredLight(entity.DeviceType),
        };

    private static CalibrationDeviceDetailDto MapToDetailDto(CalibrationDevice entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            Name = entity.Name,
            Description = entity.Description,
            DeviceType = entity.DeviceType,
            IsActive = entity.IsActive,
            CameraBindingCount = entity.CameraBindings.Count,
            MotorBindingCount = entity.MotorBindings.Count,
            ProjectorBindingCount = entity.ProjectorBindings.Count,
            HasStructuredLight = CalibrationDevice.HasStructuredLight(entity.DeviceType),
            CameraBindings = entity.CameraBindings.Select(MapToBindingDto).ToList(),
            MotorBindings = entity.MotorBindings.Select(MapToBindingDto).ToList(),
            ProjectorBindings = entity.ProjectorBindings.Select(MapToBindingDto).ToList(),
            GimbalGroups = entity.GimbalGroups.Select(MapToGimbalGroupDto).ToList(),
            InterlockRules = entity.InterlockRules.Select(MapToInterlockRuleDto).ToList(),
            CameraParameters = entity.CameraParameters.Select(MapToCameraParameterDto).ToList(),
            ProjectorParameters = entity
                .ProjectorParameters.Select(MapToProjectorParameterDto)
                .ToList(),
            MotorParameters = entity.MotorParameters.Select(MapToMotorParameterDto).ToList(),
        };

    private static CalibrationCameraBindingDto MapToBindingDto(CalibrationCameraBinding entity) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            CameraDeviceId = entity.CameraDeviceId,
            Role = entity.Role,
        };

    private static CalibrationMotorBindingDto MapToBindingDto(CalibrationMotorBinding entity) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            MotorAxisId = entity.MotorAxisId,
            Role = entity.Role,
            GimbalGroupId = entity.GimbalGroupId,
        };

    private static CalibrationProjectorBindingDto MapToBindingDto(
        CalibrationProjectorBinding entity
    ) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            ProjectorDeviceId = entity.ProjectorDeviceId,
            Role = entity.Role,
        };

    private static CalibrationGimbalGroupDto MapToGimbalGroupDto(CalibrationGimbalGroup entity) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            Name = entity.Name,
            XAxisMotorId = entity.XAxisMotorId,
            YAxisMotorId = entity.YAxisMotorId,
            ZRotateAxisMotorId = entity.ZRotateAxisMotorId,
            ZTranslateAxisMotorId = entity.ZTranslateAxisMotorId,
            MaxVelocity = entity.MaxVelocity,
            Acceleration = entity.Acceleration,
            AccelDecelTime = entity.AccelDecelTime,
            PresetPositions = entity.PresetPositions.Select(MapToGimbalPresetDto).ToList(),
        };

    private static CalibrationGimbalPresetDto MapToGimbalPresetDto(
        CalibrationGimbalPreset entity
    ) =>
        new()
        {
            Id = entity.Id,
            GimbalGroupId = entity.GimbalGroupId,
            Name = entity.Name,
            Remarks = entity.Remarks,
            XPosition = entity.XPosition,
            YPosition = entity.YPosition,
            ZRotatePosition = entity.ZRotatePosition,
            ZTranslatePosition = entity.ZTranslatePosition,
        };

    private static CalibrationMotorInterlockRuleDto MapToInterlockRuleDto(
        CalibrationMotorInterlockRule entity
    ) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            SourceMotorAxisId = entity.SourceMotorAxisId,
            SourcePositionMin = entity.SourcePositionMin,
            SourcePositionMax = entity.SourcePositionMax,
            TargetMotorAxisId = entity.TargetMotorAxisId,
            BlockedDirection = entity.BlockedDirection,
            IsEnabled = entity.IsEnabled,
            Description = entity.Description,
        };

    private static CalibrationCameraParameterDto MapToCameraParameterDto(
        CalibrationCameraParameter entity
    ) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            CameraDeviceId = entity.CameraDeviceId,
            Role = entity.Role,
            CmosSize = entity.CmosSize,
            CmosWidthMm = entity.CmosWidthMm,
            CmosHeightMm = entity.CmosHeightMm,
            ResolutionWidthPx = entity.ResolutionWidthPx,
            ResolutionHeightPx = entity.ResolutionHeightPx,
            NominalFocalLengthMm = entity.NominalFocalLengthMm,
            MaxAperture = entity.MaxAperture,
            MinAperture = entity.MinAperture,
            CurrentAperture = entity.CurrentAperture,
            MinExposureUs = entity.MinExposureUs,
            MaxExposureUs = entity.MaxExposureUs,
            MinGainDb = entity.MinGainDb,
            MaxGainDb = entity.MaxGainDb,
            PixelSizeUm = entity.PixelSizeUm,
        };

    private static CalibrationProjectorParameterDto MapToProjectorParameterDto(
        CalibrationProjectorParameter entity
    ) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            ProjectorDeviceId = entity.ProjectorDeviceId,
            ResolutionWidthPx = entity.ResolutionWidthPx,
            ResolutionHeightPx = entity.ResolutionHeightPx,
            ThrowRatio = entity.ThrowRatio,
            MinWorkingDistanceMm = entity.MinWorkingDistanceMm,
            MaxWorkingDistanceMm = entity.MaxWorkingDistanceMm,
            Pattern = entity.Pattern,
            PatternCount = entity.PatternCount,
            PhaseShift = entity.PhaseShift,
        };

    private static CalibrationMotorParameterDto MapToMotorParameterDto(
        CalibrationMotorParameter entity
    ) =>
        new()
        {
            Id = entity.Id,
            CalibrationDeviceId = entity.CalibrationDeviceId,
            MotorAxisId = entity.MotorAxisId,
            Role = entity.Role,
            EncoderResolution = entity.EncoderResolution,
            GearRatio = entity.GearRatio,
            HomePosition = entity.HomePosition,
            HomeDirection = entity.HomeDirection,
            HomingVelocity = entity.HomingVelocity,
            HomingAcceleration = entity.HomingAcceleration,
            SoftLimitMin = entity.SoftLimitMin,
            SoftLimitMax = entity.SoftLimitMax,
            IsHomed = entity.IsHomed,
            LastHomedTime = entity.LastHomedTime,
        };
}
