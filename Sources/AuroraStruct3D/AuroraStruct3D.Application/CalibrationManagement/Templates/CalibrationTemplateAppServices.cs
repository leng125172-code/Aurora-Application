using AuroraStruct3D.CalibrationManagement.Templates.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.CalibrationManagement.Templates;

/// <summary>
/// 相机参数模板应用服务实现。
/// </summary>
[Authorize(CalibrationPermissions.Template)]
public class CalibrationCameraTemplateAppService
    : CalibrationAppServiceBase,
        ICalibrationCameraTemplateAppService
{
    private readonly IRepository<CalibrationCameraTemplate, Guid> _repository;

    public CalibrationCameraTemplateAppService(
        IRepository<CalibrationCameraTemplate, Guid> repository
    )
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibrationCameraTemplateDto>> GetListAsync(
        GetCalibrationCameraTemplateListInput input
    )
    {
        IQueryable<CalibrationCameraTemplate> queryable = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            string keyword = input.Filter.Trim();
            queryable = queryable.Where(x =>
                x.Name.Contains(keyword)
                || (x.CameraModel != null && x.CameraModel.Contains(keyword))
            );
        }

        int totalCount = await AsyncExecuter.CountAsync(queryable);

        IQueryable<CalibrationCameraTemplate> sorted = ApplyCameraSorting(queryable, input.Sorting);

        List<CalibrationCameraTemplate> entities = await AsyncExecuter.ToListAsync(
            sorted.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        return new PagedResultDto<CalibrationCameraTemplateDto>(
            totalCount,
            entities.Select(MapToDto).ToList()
        );
    }

    /// <inheritdoc/>
    public async Task<CalibrationCameraTemplateDto> GetAsync(Guid id) =>
        MapToDto(await _repository.GetAsync(id));

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.TemplateCreate)]
    public async Task<CalibrationCameraTemplateDto> CreateAsync(
        CreateUpdateCalibrationCameraTemplateDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        CalibrationCameraTemplate entity = new(
            GuidGenerator.Create(),
            input.Name,
            input.ParametersJson,
            input.CameraModel,
            input.Description
        );
        await _repository.InsertAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.TemplateUpdate)]
    public async Task<CalibrationCameraTemplateDto> UpdateAsync(
        Guid id,
        CreateUpdateCalibrationCameraTemplateDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        CalibrationCameraTemplate entity = await _repository.GetAsync(id);
        entity.Update(input.Name, input.ParametersJson, input.CameraModel, input.Description);
        await _repository.UpdateAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.TemplateDelete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        await _repository.DeleteAsync(id, autoSave: true);
    }

    /// <summary>
    /// 安全白名单排序：仅允许按名称或创建时间排序，避免动态 LINQ 注入。
    /// </summary>
    private static IQueryable<CalibrationCameraTemplate> ApplyCameraSorting(
        IQueryable<CalibrationCameraTemplate> queryable,
        string? sorting
    )
    {
        string normalized = (sorting ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "name" or "name asc" => queryable.OrderBy(x => x.Name),
            "name desc" => queryable.OrderByDescending(x => x.Name),
            "creationtime" or "creationtime asc" => queryable.OrderBy(x => x.CreationTime),
            _ => queryable.OrderByDescending(x => x.CreationTime),
        };
    }

    private static CalibrationCameraTemplateDto MapToDto(CalibrationCameraTemplate entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            Name = entity.Name,
            CameraModel = entity.CameraModel,
            Description = entity.Description,
            ParametersJson = entity.ParametersJson,
        };
}

/// <summary>
/// 结构光参数模板应用服务实现。
/// </summary>
[Authorize(CalibrationPermissions.Template)]
public class CalibrationProjectorTemplateAppService
    : CalibrationAppServiceBase,
        ICalibrationProjectorTemplateAppService
{
    private readonly IRepository<CalibrationProjectorTemplate, Guid> _repository;

    public CalibrationProjectorTemplateAppService(
        IRepository<CalibrationProjectorTemplate, Guid> repository
    )
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<CalibrationProjectorTemplateDto>> GetListAsync(
        GetCalibrationProjectorTemplateListInput input
    )
    {
        IQueryable<CalibrationProjectorTemplate> queryable = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            string keyword = input.Filter.Trim();
            queryable = queryable.Where(x =>
                x.Name.Contains(keyword)
                || (x.ProjectorModel != null && x.ProjectorModel.Contains(keyword))
            );
        }

        int totalCount = await AsyncExecuter.CountAsync(queryable);

        IQueryable<CalibrationProjectorTemplate> sorted = ApplyProjectorSorting(
            queryable,
            input.Sorting
        );

        List<CalibrationProjectorTemplate> entities = await AsyncExecuter.ToListAsync(
            sorted.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        return new PagedResultDto<CalibrationProjectorTemplateDto>(
            totalCount,
            entities.Select(MapToDto).ToList()
        );
    }

    /// <inheritdoc/>
    public async Task<CalibrationProjectorTemplateDto> GetAsync(Guid id) =>
        MapToDto(await _repository.GetAsync(id));

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.TemplateCreate)]
    public async Task<CalibrationProjectorTemplateDto> CreateAsync(
        CreateUpdateCalibrationProjectorTemplateDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        CalibrationProjectorTemplate entity = new(
            GuidGenerator.Create(),
            input.Name,
            input.ParametersJson,
            input.ProjectorModel,
            input.Description
        );
        await _repository.InsertAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.TemplateUpdate)]
    public async Task<CalibrationProjectorTemplateDto> UpdateAsync(
        Guid id,
        CreateUpdateCalibrationProjectorTemplateDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        CalibrationProjectorTemplate entity = await _repository.GetAsync(id);
        entity.Update(input.Name, input.ParametersJson, input.ProjectorModel, input.Description);
        await _repository.UpdateAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    /// <inheritdoc/>
    [Authorize(CalibrationPermissions.TemplateDelete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        await _repository.DeleteAsync(id, autoSave: true);
    }

    /// <summary>
    /// 安全白名单排序：仅允许按名称或创建时间排序，避免动态 LINQ 注入。
    /// </summary>
    private static IQueryable<CalibrationProjectorTemplate> ApplyProjectorSorting(
        IQueryable<CalibrationProjectorTemplate> queryable,
        string? sorting
    )
    {
        string normalized = (sorting ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "name" or "name asc" => queryable.OrderBy(x => x.Name),
            "name desc" => queryable.OrderByDescending(x => x.Name),
            "creationtime" or "creationtime asc" => queryable.OrderBy(x => x.CreationTime),
            _ => queryable.OrderByDescending(x => x.CreationTime),
        };
    }

    private static CalibrationProjectorTemplateDto MapToDto(CalibrationProjectorTemplate entity) =>
        new()
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            Name = entity.Name,
            ProjectorModel = entity.ProjectorModel,
            Description = entity.Description,
            ParametersJson = entity.ParametersJson,
        };
}
