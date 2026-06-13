using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定电机参数应用服务实现。
/// 按 CalibProjectId + MotorAxisId 执行 Upsert。
/// </summary>
[Authorize]
public class CalibMotorParamAppService : AuroraStruct3DAppService, ICalibMotorParamAppService
{
    private readonly IRepository<CalibMotorParam, Guid> _repository;

    public CalibMotorParamAppService(IRepository<CalibMotorParam, Guid> repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<List<CalibMotorParamDto>> GetListAsync(Guid calibProjectId)
    {
        IQueryable<CalibMotorParam> query = await _repository.GetQueryableAsync();
        List<CalibMotorParam> items = await AsyncExecuter.ToListAsync(
            query.Where(x => x.CalibProjectId == calibProjectId).OrderBy(x => x.CreationTime)
        );
        return items.Select(ToDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<CalibMotorParamDto> SaveAsync(SaveCalibMotorParamInput input)
    {
        IQueryable<CalibMotorParam> query = await _repository.GetQueryableAsync();
        CalibMotorParam? existing = await AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x =>
                x.CalibProjectId == input.CalibProjectId && x.MotorAxisId == input.MotorAxisId
            )
        );

        if (existing is not null)
        {
            ApplyInputToEntity(existing, input);
            await _repository.UpdateAsync(existing);
            return ToDto(existing);
        }

        CalibMotorParam entity = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.MotorAxisId
        );
        ApplyInputToEntity(entity, input);
        await _repository.InsertAsync(entity);
        return ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }

    private static void ApplyInputToEntity(CalibMotorParam entity, SaveCalibMotorParamInput input)
    {
        entity.SetEncoderParams(
            input.EncoderResolution ?? entity.EncoderResolution,
            input.GearRatio ?? entity.GearRatio
        );
        entity.SetOriginConfig(
            input.MechanicalOriginPosition ?? entity.MechanicalOriginPosition,
            input.OriginDirection ?? entity.OriginDirection
        );
        entity.SetSoftLimits(
            input.PositiveSoftLimit ?? entity.PositiveSoftLimit,
            input.NegativeSoftLimit ?? entity.NegativeSoftLimit
        );
        entity.SetHomingSpeedParams(
            input.HomeSpeed ?? entity.HomeSpeed,
            input.HomeAcceleration ?? entity.HomeAcceleration
        );
        entity.SetOriginLocked(input.IsOriginLocked ?? entity.IsOriginLocked);
        entity.SetLimitEnabled(input.LimitEnabled ?? entity.LimitEnabled);
        entity.SetHomingMode(input.HomingMode ?? entity.HomingMode);
    }

    private static CalibMotorParamDto ToDto(CalibMotorParam entity) =>
        new()
        {
            Id = entity.Id,
            CalibProjectId = entity.CalibProjectId,
            MotorAxisId = entity.MotorAxisId,
            EncoderResolution = entity.EncoderResolution,
            GearRatio = entity.GearRatio,
            MechanicalOriginPosition = entity.MechanicalOriginPosition,
            OriginDirection = entity.OriginDirection,
            PositiveSoftLimit = entity.PositiveSoftLimit,
            NegativeSoftLimit = entity.NegativeSoftLimit,
            HomeSpeed = entity.HomeSpeed,
            HomeAcceleration = entity.HomeAcceleration,
            IsOriginLocked = entity.IsOriginLocked,
            LimitEnabled = entity.LimitEnabled,
            HomingMode = entity.HomingMode,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
        };

    /// <inheritdoc/>
    public async Task<bool> ValidateStep4Async(Guid calibProjectId)
    {
        // 读取项目以获得绑定电机轴列表
        IQueryable<CalibProject> projectQuery = await LazyServiceProvider
            .LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<CalibProject, Guid>>()
            .GetQueryableAsync();
        CalibProject? project = await AsyncExecuter.FirstOrDefaultAsync(
            projectQuery.Where(x => x.Id == calibProjectId)
        );
        if (project == null)
            return false;

        // 收集绑定的电机轴 ID
        List<Guid> axisIds = new();
        if (project.MainCameraMotorAxisId.HasValue)
            axisIds.Add(project.MainCameraMotorAxisId.Value);
        if (project.SecondaryCameraMotorAxisId.HasValue)
            axisIds.Add(project.SecondaryCameraMotorAxisId.Value);
        if (project.DistanceMotorAxisId.HasValue)
            axisIds.Add(project.DistanceMotorAxisId.Value);
        if (axisIds.Count == 0)
            return false;

        IQueryable<CalibMotorParam> paramQuery = await _repository.GetQueryableAsync();
        List<Guid> savedAxisIds = await AsyncExecuter.ToListAsync(
            paramQuery
                .Where(x => x.CalibProjectId == calibProjectId && axisIds.Contains(x.MotorAxisId))
                .Select(x => x.MotorAxisId)
        );

        // 每个绑定的电机轴均必须有配置记录
        return axisIds.All(id => savedAxisIds.Contains(id));
    }
}
