using System.Linq.Dynamic.Core;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.DeviceState;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 云台组（标定结构）应用服务实现。
/// 云台组是独立逻辑设备，不绑定具体标定项目。
/// 写操作要求设备处于手动或检修运行模式。
/// </summary>
[Authorize]
public class CalibGimbalGroupAppService : AuroraStruct3DAppService, ICalibGimbalGroupAppService
{
    private readonly IRepository<CalibGimbalGroup, Guid> _repository;
    private readonly IDeviceStateManager _deviceStateManager;

    /// <summary>构造注入</summary>
    public CalibGimbalGroupAppService(
        IRepository<CalibGimbalGroup, Guid> repository,
        IDeviceStateManager deviceStateManager
    )
    {
        _repository = repository;
        _deviceStateManager = deviceStateManager;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibGimbalGroupDto>> GetListAsync(
        GetCalibGimbalGroupListInput input
    )
    {
        IQueryable<CalibGimbalGroup> query = await _repository.GetQueryableAsync();

        query = query
            .WhereIf(!string.IsNullOrWhiteSpace(input.Filter), x => x.Name.Contains(input.Filter!))
            .WhereIf(input.IsEnabled.HasValue, x => x.IsEnabled == input.IsEnabled!.Value)
            .WhereIf(input.StartTime.HasValue, x => x.CreationTime >= input.StartTime!.Value)
            .WhereIf(input.EndTime.HasValue, x => x.CreationTime <= input.EndTime!.Value);

        long totalCount = await AsyncExecuter.CountAsync(query);

        string sorting = string.IsNullOrWhiteSpace(input.Sorting)
            ? "CreationTime DESC"
            : input.Sorting;
        List<CalibGimbalGroup> items = await AsyncExecuter.ToListAsync(
            query.OrderBy(sorting).PageBy(input)
        );

        return new PagedResultDto<CalibGimbalGroupDto>(totalCount, items.Select(ToDto).ToList());
    }

    /// <inheritdoc/>
    public async Task<CalibGimbalGroupDto> GetAsync(Guid id)
    {
        CalibGimbalGroup entity = await _repository.GetAsync(id);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<CalibGimbalGroupDto> CreateAsync(CreateUpdateCalibGimbalGroupInput input)
    {
        EnsureManualOrMaintenanceMode();

        CalibGimbalGroup entity = new(GuidGenerator.Create(), input.Name);
        entity.SetDescription(input.Description);
        entity.SetMotionParams(
            input.MaxSpeed,
            input.Acceleration,
            input.AccelerationTime,
            input.DecelerationTime
        );
        entity.SetEnabled(input.IsEnabled);

        await _repository.InsertAsync(entity);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<CalibGimbalGroupDto> UpdateAsync(
        Guid id,
        CreateUpdateCalibGimbalGroupInput input
    )
    {
        EnsureManualOrMaintenanceMode();

        CalibGimbalGroup entity = await _repository.GetAsync(id);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetMotionParams(
            input.MaxSpeed,
            input.Acceleration,
            input.AccelerationTime,
            input.DecelerationTime
        );
        entity.SetEnabled(input.IsEnabled);

        await _repository.UpdateAsync(entity);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        await _repository.DeleteAsync(id);
    }

    /// <summary>校验当前运行模式必须为手动或检修，否则抛出业务异常。</summary>
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
                }}】，云台组操作仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>将实体转换为 DTO</summary>
    private static CalibGimbalGroupDto ToDto(CalibGimbalGroup entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            MaxSpeed = entity.MaxSpeed,
            Acceleration = entity.Acceleration,
            AccelerationTime = entity.AccelerationTime,
            DecelerationTime = entity.DecelerationTime,
            IsEnabled = entity.IsEnabled,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
        };
}
