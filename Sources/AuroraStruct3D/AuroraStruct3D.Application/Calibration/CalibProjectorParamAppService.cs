using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定投影仪参数应用服务实现（Step3 投影仪参数配置页）。
/// 按 CalibProjectId 执行 Upsert（每个标定项目对应一条投影仪参数记录）。
/// </summary>
[Authorize]
public class CalibProjectorParamAppService : AuroraStruct3DAppService,
    ICalibProjectorParamAppService
{
    private readonly IRepository<CalibProjectorParam, Guid> _repository;

    /// <summary>构造注入</summary>
    public CalibProjectorParamAppService(IRepository<CalibProjectorParam, Guid> repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<CalibProjectorParamDto?> GetAsync(Guid calibProjectId)
    {
        IQueryable<CalibProjectorParam> query = await _repository.GetQueryableAsync();
        CalibProjectorParam? entity = await AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x => x.CalibProjectId == calibProjectId)
        );
        return entity == null ? null : ToDto(entity);
    }

    /// <inheritdoc/>
    public async Task<CalibProjectorParamDto> UpdateAsync(
        Guid calibProjectId,
        SaveCalibProjectorParamInput input
    )
    {
        // 路径参数与请求体项目ID必须一致
        if (calibProjectId != input.CalibProjectId)
        {
            throw new UserFriendlyException(
                "路径参数 calibProjectId 与请求体 CalibProjectId 不一致"
            );
        }

        IQueryable<CalibProjectorParam> query = await _repository.GetQueryableAsync();
        CalibProjectorParam? existing = await AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x => x.CalibProjectId == calibProjectId)
        );

        if (existing != null)
        {
            // 更新已有记录：保留 Name 等基础字段，仅刷新投影/条纹参数
            existing.SetResolution(input.ResolutionWidth, input.ResolutionHeight);
            existing.SetFringeParams(
                input.PeriodCount,
                input.FringeType,
                GrayCodePatternLayout.TotalFrameCount,
                input.PhaseShift
            );
            // ProjectorDeviceId 允许变更（用户切换投影仪后重新保存）
            // 由于 ProjectorDeviceId 是 private set，需通过反射或新增 setter；
            // 此处选择新建记录方式处理 ProjectorDeviceId 变更场景：删除后重建
            if (existing.ProjectorDeviceId != input.ProjectorDeviceId)
            {
                await _repository.DeleteAsync(existing);
                CalibProjectorParam newEntity = await CreateNewEntityAsync(input);
                await _repository.InsertAsync(newEntity);
                return ToDto(newEntity);
            }

            await _repository.UpdateAsync(existing);
            return ToDto(existing);
        }
        else
        {
            CalibProjectorParam entity = await CreateNewEntityAsync(input);
            await _repository.InsertAsync(entity);
            return ToDto(entity);
        }
    }

    /// <summary>根据输入 DTO 创建新实体</summary>
    private Task<CalibProjectorParam> CreateNewEntityAsync(SaveCalibProjectorParamInput input)
    {
        CalibProjectorParam entity = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.ProjectorDeviceId,
            $"项目 {input.CalibProjectId} 投影仪参数"
        );
        entity.SetResolution(input.ResolutionWidth, input.ResolutionHeight);
        entity.SetFringeParams(
            input.PeriodCount,
            input.FringeType,
            GrayCodePatternLayout.TotalFrameCount,
            input.PhaseShift
        );
        return Task.FromResult(entity);
    }

    /// <summary>实体转 DTO</summary>
    private static CalibProjectorParamDto ToDto(CalibProjectorParam entity)
    {
        return new CalibProjectorParamDto
        {
            Id = entity.Id,
            CalibProjectId = entity.CalibProjectId,
            ProjectorDeviceId = entity.ProjectorDeviceId,
            Name = entity.Name,
            Description = entity.Description,
            ResolutionWidth = entity.ResolutionWidth,
            ResolutionHeight = entity.ResolutionHeight,
            PeriodCount = entity.PeriodCount,
            FringeType = entity.FringeType,
            PatternCount = entity.PatternCount,
            PhaseShift = entity.PhaseShift,
            IsEnabled = entity.IsEnabled,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
        };
    }
}
