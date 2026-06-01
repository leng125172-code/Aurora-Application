using AuroraStruct3D.CalibrationManagement.Templates.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.CalibrationManagement.Templates;

/// <summary>
/// 相机参数模板应用服务接口。
/// 自动生成路径：/api/app/calibration-camera-template
/// </summary>
public interface ICalibrationCameraTemplateAppService : IApplicationService
{
    Task<PagedResultDto<CalibrationCameraTemplateDto>> GetListAsync(
        GetCalibrationCameraTemplateListInput input
    );

    Task<CalibrationCameraTemplateDto> GetAsync(Guid id);

    Task<CalibrationCameraTemplateDto> CreateAsync(CreateUpdateCalibrationCameraTemplateDto input);

    Task<CalibrationCameraTemplateDto> UpdateAsync(
        Guid id,
        CreateUpdateCalibrationCameraTemplateDto input
    );

    Task DeleteAsync(Guid id);
}

/// <summary>
/// 结构光参数模板应用服务接口。
/// 自动生成路径：/api/app/calibration-projector-template
/// </summary>
public interface ICalibrationProjectorTemplateAppService : IApplicationService
{
    Task<PagedResultDto<CalibrationProjectorTemplateDto>> GetListAsync(
        GetCalibrationProjectorTemplateListInput input
    );

    Task<CalibrationProjectorTemplateDto> GetAsync(Guid id);

    Task<CalibrationProjectorTemplateDto> CreateAsync(
        CreateUpdateCalibrationProjectorTemplateDto input
    );

    Task<CalibrationProjectorTemplateDto> UpdateAsync(
        Guid id,
        CreateUpdateCalibrationProjectorTemplateDto input
    );

    Task DeleteAsync(Guid id);
}
