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

        ValidateFringeConfiguration(input);

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
                input.PatternCount,
                input.PhaseShift,
                input.DarkLevel,
                input.BrightLevel
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
            input.PatternCount,
            input.PhaseShift,
            input.DarkLevel,
            input.BrightLevel
        );
        return Task.FromResult(entity);
    }

    private static void ValidateFringeConfiguration(SaveCalibProjectorParamInput input)
    {
        if (input.ResolutionWidth <= 0 || input.ResolutionHeight <= 0)
            throw new UserFriendlyException("投影分辨率必须大于 0");
        if (
            input.PeriodCount <= 0
            || input.ResolutionWidth % input.PeriodCount != 0
            || input.ResolutionHeight % input.PeriodCount != 0
        )
            throw new UserFriendlyException("条纹周期数必须同时整除投影宽度和高度");
        if (input.PatternCount < 3 || input.PatternCount > 64)
            throw new UserFriendlyException("每个方向的相移图像数量必须在 3 到 64 之间");
        if (!string.Equals(input.FringeType, "bw", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.FringeType, "wb", StringComparison.OrdinalIgnoreCase))
            throw new UserFriendlyException("条纹类型仅支持 bw 或 wb");
        if (input.DarkLevel >= input.BrightLevel)
            throw new UserFriendlyException("暗部灰阶必须小于亮部灰阶");

        decimal shortestFullPeriod =
            Math.Min(
                input.ResolutionWidth / input.PeriodCount,
                input.ResolutionHeight / input.PeriodCount
            ) * 2m;
        if (
            !input.PhaseShift.HasValue
            || input.PhaseShift.Value != decimal.Truncate(input.PhaseShift.Value)
            || input.PhaseShift.Value <= 0
            || input.PhaseShift.Value >= shortestFullPeriod
        )
            throw new UserFriendlyException("相移量必须为正整数，且小于最短条纹的完整黑白周期");
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
            DarkLevel = entity.DarkLevel,
            BrightLevel = entity.BrightLevel,
            PatternCount = entity.PatternCount,
            PhaseShift = entity.PhaseShift,
            IsEnabled = entity.IsEnabled,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
        };
    }
}
