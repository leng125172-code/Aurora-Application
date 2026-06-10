using System.Linq.Dynamic.Core;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.DeviceState;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 设备标定项目应用服务实现。
/// 写操作要求设备处于手动或检修运行模式。
/// </summary>
[Authorize]
public class CalibProjectAppService : AuroraStruct3DAppService, ICalibProjectAppService
{
    private readonly IRepository<CalibProject, Guid> _repository;
    private readonly IDeviceStateManager _deviceStateManager;

    /// <summary>构造注入</summary>
    public CalibProjectAppService(
        IRepository<CalibProject, Guid> repository,
        IDeviceStateManager deviceStateManager
    )
    {
        _repository = repository;
        _deviceStateManager = deviceStateManager;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibProjectDto>> GetListAsync(GetCalibProjectListInput input)
    {
        IQueryable<CalibProject> query = await _repository.GetQueryableAsync();

        // 应用过滤条件
        query = query
            .WhereIf(!string.IsNullOrWhiteSpace(input.Filter), x => x.Name.Contains(input.Filter!))
            .WhereIf(input.DeviceSeries.HasValue, x => x.DeviceSeries == input.DeviceSeries!.Value)
            .WhereIf(input.DeviceType.HasValue, x => x.DeviceType == input.DeviceType!.Value)
            .WhereIf(input.CalibStatus.HasValue, x => x.CalibStatus == input.CalibStatus!.Value)
            .WhereIf(input.StartTime.HasValue, x => x.CreationTime >= input.StartTime!.Value)
            .WhereIf(input.EndTime.HasValue, x => x.CreationTime <= input.EndTime!.Value);

        long totalCount = await AsyncExecuter.CountAsync(query);

        // 排序与分页
        string sorting = string.IsNullOrWhiteSpace(input.Sorting)
            ? "CreationTime DESC"
            : input.Sorting;
        List<CalibProject> items = await AsyncExecuter.ToListAsync(
            query.OrderBy(sorting).PageBy(input)
        );

        return new PagedResultDto<CalibProjectDto>(totalCount, items.Select(ToDto).ToList());
    }

    /// <inheritdoc/>
    public async Task<CalibProjectDto> GetAsync(Guid id)
    {
        CalibProject entity = await _repository.GetAsync(id);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<CalibProjectDto> CreateAsync(CreateCalibProjectInput input)
    {
        EnsureManualOrMaintenanceMode();
        ValidateDeviceBindings(input.DeviceType, input);

        CalibProject entity = new(GuidGenerator.Create(), input.Name, input.DeviceType);
        entity.SetDescription(input.Description);
        entity.SetDeviceBindings(
            input.MainCameraDeviceId,
            input.SecondaryCameraDeviceId,
            input.MainCameraMotorAxisId,
            input.SecondaryCameraMotorAxisId,
            input.DistanceMotorAxisId,
            input.BoundProjectorDeviceId
        );

        await _repository.InsertAsync(entity);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<CalibProjectDto> UpdateAsync(Guid id, UpdateCalibProjectInput input)
    {
        EnsureManualOrMaintenanceMode();

        CalibProject entity = await _repository.GetAsync(id);
        ValidateDeviceBindings(entity.DeviceType, input);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetDeviceBindings(
            input.MainCameraDeviceId,
            input.SecondaryCameraDeviceId,
            input.MainCameraMotorAxisId,
            input.SecondaryCameraMotorAxisId,
            input.DistanceMotorAxisId,
            input.BoundProjectorDeviceId
        );

        await _repository.UpdateAsync(entity);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        await _repository.DeleteAsync(id);
    }

    /// <summary>
    /// 校验当前运行模式必须为手动或检修，否则抛出业务异常。
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
                }}】，标定项目操作仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>创建输入绑定规则校验</summary>
    private static void ValidateDeviceBindings(
        CalibDeviceType deviceType,
        CreateCalibProjectInput input
    )
    {
        ValidateDeviceBindingsCore(
            deviceType,
            input.MainCameraDeviceId,
            input.SecondaryCameraDeviceId,
            input.MainCameraMotorAxisId,
            input.SecondaryCameraMotorAxisId,
            input.DistanceMotorAxisId,
            input.BoundProjectorDeviceId
        );
    }

    /// <summary>更新输入绑定规则校验</summary>
    private static void ValidateDeviceBindings(
        CalibDeviceType deviceType,
        UpdateCalibProjectInput input
    )
    {
        ValidateDeviceBindingsCore(
            deviceType,
            input.MainCameraDeviceId,
            input.SecondaryCameraDeviceId,
            input.MainCameraMotorAxisId,
            input.SecondaryCameraMotorAxisId,
            input.DistanceMotorAxisId,
            input.BoundProjectorDeviceId
        );
    }

    /// <summary>设备绑定规则统一校验（项目管理页与向导复用）</summary>
    private static void ValidateDeviceBindingsCore(
        CalibDeviceType deviceType,
        Guid? mainCameraDeviceId,
        Guid? secondaryCameraDeviceId,
        Guid? mainCameraMotorAxisId,
        Guid? secondaryCameraMotorAxisId,
        Guid? distanceMotorAxisId,
        Guid? boundProjectorDeviceId
    )
    {
        if (mainCameraDeviceId == null)
        {
            throw new UserFriendlyException("必须绑定主相机");
        }

        if (mainCameraMotorAxisId == null)
        {
            throw new UserFriendlyException("必须绑定主相机角度控制电机");
        }

        if (distanceMotorAxisId == null)
        {
            throw new UserFriendlyException("必须绑定间距控制电机");
        }

        if (
            mainCameraDeviceId.HasValue
            && secondaryCameraDeviceId.HasValue
            && mainCameraDeviceId.Value == secondaryCameraDeviceId.Value
        )
        {
            throw new UserFriendlyException("主相机与从相机必须绑定不同设备");
        }

        List<Guid> motorIds = [];
        if (mainCameraMotorAxisId.HasValue)
            motorIds.Add(mainCameraMotorAxisId.Value);
        if (secondaryCameraMotorAxisId.HasValue)
            motorIds.Add(secondaryCameraMotorAxisId.Value);
        if (distanceMotorAxisId.HasValue)
            motorIds.Add(distanceMotorAxisId.Value);
        if (motorIds.Distinct().Count() != motorIds.Count)
        {
            throw new UserFriendlyException(
                "电机绑定存在重复，主相机角度/从相机角度/间距控制必须互斥"
            );
        }

        switch (deviceType)
        {
            case CalibDeviceType.TwoCamera0Light:
                if (secondaryCameraDeviceId == null)
                    throw new UserFriendlyException("2目0光必须绑定从相机");
                if (secondaryCameraMotorAxisId == null)
                    throw new UserFriendlyException("2目0光必须绑定从相机角度控制电机");
                if (boundProjectorDeviceId != null)
                    throw new UserFriendlyException("2目0光不允许绑定结构光设备");
                break;

            case CalibDeviceType.OneCamera1Light:
                if (secondaryCameraDeviceId != null)
                    throw new UserFriendlyException("1目1光仅允许绑定主相机，不能绑定从相机");
                if (secondaryCameraMotorAxisId != null)
                    throw new UserFriendlyException("1目1光不允许绑定从相机角度控制电机");
                if (boundProjectorDeviceId == null)
                    throw new UserFriendlyException("1目1光必须绑定主结构光机");
                break;

            case CalibDeviceType.TwoCamera1Light:
                if (secondaryCameraDeviceId == null)
                    throw new UserFriendlyException("2目1光必须绑定从相机");
                if (secondaryCameraMotorAxisId == null)
                    throw new UserFriendlyException("2目1光必须绑定从相机角度控制电机");
                if (boundProjectorDeviceId == null)
                    throw new UserFriendlyException("2目1光必须绑定主结构光机");
                break;

            default:
                throw new UserFriendlyException("不支持的设备类型");
        }
    }

    /// <summary>将实体转换为 DTO</summary>
    private static CalibProjectDto ToDto(CalibProject entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            DeviceSeries = entity.DeviceSeries,
            DeviceType = entity.DeviceType,
            CameraCount = entity.CameraCount,
            ProjectorCount = entity.ProjectorCount,
            CalibStatus = entity.CalibStatus,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
            BoundProjectorDeviceId = entity.BoundProjectorDeviceId,
            MainCameraDeviceId = entity.MainCameraDeviceId,
            SecondaryCameraDeviceId = entity.SecondaryCameraDeviceId,
            MainCameraMotorAxisId = entity.MainCameraMotorAxisId,
            SecondaryCameraMotorAxisId = entity.SecondaryCameraMotorAxisId,
            DistanceMotorAxisId = entity.DistanceMotorAxisId,
        };
}
