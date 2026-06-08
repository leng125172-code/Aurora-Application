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
            input.MotorAxisId,
            input.MotorType
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
    }

    private static CalibMotorParamDto ToDto(CalibMotorParam entity) =>
        new()
        {
            Id = entity.Id,
            CalibProjectId = entity.CalibProjectId,
            MotorAxisId = entity.MotorAxisId,
            MotorType = entity.MotorType,
            EncoderResolution = entity.EncoderResolution,
            GearRatio = entity.GearRatio,
            MechanicalOriginPosition = entity.MechanicalOriginPosition,
            OriginDirection = entity.OriginDirection,
            PositiveSoftLimit = entity.PositiveSoftLimit,
            NegativeSoftLimit = entity.NegativeSoftLimit,
            HomeSpeed = entity.HomeSpeed,
            HomeAcceleration = entity.HomeAcceleration,
            IsOriginLocked = entity.IsOriginLocked,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
        };
}
