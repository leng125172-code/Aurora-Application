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

        CalibProject entity = new(GuidGenerator.Create(), input.Name, input.DeviceType);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<CalibProjectDto> UpdateAsync(Guid id, UpdateCalibProjectInput input)
    {
        EnsureManualOrMaintenanceMode();

        CalibProject entity = await _repository.GetAsync(id);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);

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
        };
}
