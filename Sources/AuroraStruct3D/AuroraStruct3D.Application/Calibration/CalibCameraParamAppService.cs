using AuroraStruct3D.Calibration.Dtos;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定相机参数应用服务实现。
/// 按 CalibProjectId + CameraDeviceId 执行 Upsert（写入幂等）。
/// </summary>
[Authorize]
public class CalibCameraParamAppService : AuroraStruct3DAppService, ICalibCameraParamAppService
{
    private readonly IRepository<CalibCameraParam, Guid> _repository;

    /// <summary>构造注入</summary>
    public CalibCameraParamAppService(IRepository<CalibCameraParam, Guid> repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<List<CalibCameraParamDto>> GetListAsync(Guid calibProjectId)
    {
        IQueryable<CalibCameraParam> query = await _repository.GetQueryableAsync();
        List<CalibCameraParam> items = await AsyncExecuter.ToListAsync(
            query.Where(x => x.CalibProjectId == calibProjectId).OrderBy(x => x.CreationTime)
        );
        return items.Select(ToDto).ToList();
    }

    /// <inheritdoc/>
    public async Task<CalibCameraParamDto> SaveAsync(SaveCalibCameraParamInput input)
    {
        // 查找是否已存在相同项目+相机的记录
        IQueryable<CalibCameraParam> query = await _repository.GetQueryableAsync();
        CalibCameraParam? existing = await AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x =>
                x.CalibProjectId == input.CalibProjectId && x.CameraDeviceId == input.CameraDeviceId
            )
        );

        if (existing != null)
        {
            // 更新已有记录
            ApplyInputToEntity(existing, input);
            await _repository.UpdateAsync(existing);
            return ToDto(existing);
        }
        else
        {
            // 新建记录
            CalibCameraParam entity = new(
                GuidGenerator.Create(),
                input.CalibProjectId,
                input.CameraDeviceId,
                input.Name
            );
            ApplyInputToEntity(entity, input);
            await _repository.InsertAsync(entity);
            return ToDto(entity);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }

    /// <summary>将输入 DTO 的字段应用到实体</summary>
    private static void ApplyInputToEntity(CalibCameraParam entity, SaveCalibCameraParamInput input)
    {
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetEnabled(input.IsEnabled);

        // 传感器与分辨率参数（有数据时才写入）
        if (
            !string.IsNullOrWhiteSpace(input.SensorSize)
            && input.SensorWidthMm.HasValue
            && input.SensorHeightMm.HasValue
            && input.ImageWidthPixels.HasValue
            && input.ImageHeightPixels.HasValue
        )
        {
            entity.SetSensorParams(
                input.SensorSize,
                input.SensorWidthMm.Value,
                input.SensorHeightMm.Value,
                input.ImageWidthPixels.Value,
                input.ImageHeightPixels.Value
            );
        }

        // 镜头参数（有数据时才写入）
        if (
            input.LensFocalLength.HasValue
            && input.MaxAperture.HasValue
            && input.MinAperture.HasValue
            && input.CurrentAperture.HasValue
        )
        {
            entity.SetLensParams(
                input.LensFocalLength.Value,
                input.MaxAperture.Value,
                input.MinAperture.Value,
                input.CurrentAperture.Value
            );
        }

        // Step 2 当前只采集曝光范围，不采集增益范围。
        // 因此这里放宽写入条件：只要曝光上下限存在就落库，增益缺失时保留实体当前值。
        if (input.ExposureTimeMinUs.HasValue && input.ExposureTimeMaxUs.HasValue)
        {
            entity.SetExposureGainRange(
                input.ExposureTimeMinUs.Value,
                input.ExposureTimeMaxUs.Value,
                input.GainMinDb ?? entity.GainMinDb,
                input.GainMaxDb ?? entity.GainMaxDb
            );
        }

        // 位置绑定标签（始终写入，允许 null 清除）
        entity.SetCameraPosition(input.CameraPosition);
    }

    /// <summary>将实体转换为 DTO（可空字段在未赋值时返回 null）</summary>
    private static CalibCameraParamDto ToDto(CalibCameraParam entity) =>
        new()
        {
            Id = entity.Id,
            CalibProjectId = entity.CalibProjectId,
            CameraDeviceId = entity.CameraDeviceId,
            Name = entity.Name,
            Description = entity.Description,
            IsEnabled = entity.IsEnabled,
            SensorSize = string.IsNullOrEmpty(entity.SensorSize) ? null : entity.SensorSize,
            SensorWidthMm = entity.SensorWidthMm == 0 ? null : entity.SensorWidthMm,
            SensorHeightMm = entity.SensorHeightMm == 0 ? null : entity.SensorHeightMm,
            ImageWidthPixels = entity.ImageWidthPixels == 0 ? null : entity.ImageWidthPixels,
            ImageHeightPixels = entity.ImageHeightPixels == 0 ? null : entity.ImageHeightPixels,
            PixelSizeUm = entity.PixelSizeUm == 0 ? null : entity.PixelSizeUm,
            LensFocalLength = entity.LensFocalLength == 0 ? null : entity.LensFocalLength,
            MaxAperture = entity.MaxAperture == 0 ? null : entity.MaxAperture,
            MinAperture = entity.MinAperture == 0 ? null : entity.MinAperture,
            CurrentAperture = entity.CurrentAperture == 0 ? null : entity.CurrentAperture,
            ExposureTimeMinUs = entity.ExposureTimeMinUs == 0 ? null : entity.ExposureTimeMinUs,
            ExposureTimeMaxUs = entity.ExposureTimeMaxUs == 0 ? null : entity.ExposureTimeMaxUs,
            GainMinDb = entity.GainMinDb == 0 ? null : entity.GainMinDb,
            GainMaxDb = entity.GainMaxDb == 0 ? null : entity.GainMaxDb,
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime,
            CameraPosition = entity.CameraPosition,
        };

    /// <inheritdoc/>
    public async Task<bool> ValidateStep2Async(Guid calibProjectId)
    {
        // 读取项目以获得绑定相机列表
        IQueryable<CalibProject> projectQuery = await LazyServiceProvider
            .LazyGetRequiredService<Volo.Abp.Domain.Repositories.IRepository<CalibProject, Guid>>()
            .GetQueryableAsync();
        CalibProject? project = await AsyncExecuter.FirstOrDefaultAsync(
            projectQuery.Where(x => x.Id == calibProjectId)
        );
        if (project == null)
            return false;

        // 收集绑定相机 ID（主相机必填，从相机根据项目类型可能为空）
        List<Guid> cameraIds = new();
        if (project.MainCameraDeviceId.HasValue)
            cameraIds.Add(project.MainCameraDeviceId.Value);
        if (project.SecondaryCameraDeviceId.HasValue)
            cameraIds.Add(project.SecondaryCameraDeviceId.Value);
        if (cameraIds.Count == 0)
            return false;

        IQueryable<CalibCameraParam> paramQuery = await _repository.GetQueryableAsync();
        List<CalibCameraParam> existing = await AsyncExecuter.ToListAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && cameraIds.Contains(x.CameraDeviceId)
            )
        );

        // 所有绑定相机均必须有记录，且传感器尺寸已填写（代表基础参数已保存）
        return cameraIds.All(id =>
            existing.Any(p => p.CameraDeviceId == id && !string.IsNullOrEmpty(p.SensorSize))
        );
    }
}
