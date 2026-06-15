using AuroraStruct3D.Projects.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Projects;

/// <summary>
/// 项目管理应用服务接口。
/// ABP 自动生成对应 REST 端点，路由基础路径：/api/app/project-info
/// </summary>
public interface IProjectInfoAppService : IApplicationService
{
    /// <summary>
    /// 分页查询项目列表（支持关键字、状态等过滤）。
    /// GET /api/app/project-info
    /// </summary>
    /// <param name="input">查询条件与分页参数</param>
    Task<PagedResultDto<ProjectInfoDto>> GetListAsync(GetProjectInfoListInput input);

    /// <summary>
    /// 获取单个项目详情。
    /// GET /api/app/project-info/{id}
    /// </summary>
    /// <param name="id">项目唯一标识</param>
    Task<ProjectInfoDto> GetAsync(Guid id);

    /// <summary>
    /// 创建新项目。
    /// POST /api/app/project-info
    /// </summary>
    /// <param name="input">创建参数</param>
    Task<ProjectInfoDto> CreateAsync(CreateProjectInfoInput input);

    /// <summary>
    /// 更新项目信息（不允许修改项目编号）。
    /// PUT /api/app/project-info/{id}
    /// </summary>
    /// <param name="id">项目唯一标识</param>
    /// <param name="input">更新参数</param>
    Task<ProjectInfoDto> UpdateAsync(Guid id, UpdateProjectInfoInput input);

    /// <summary>
    /// 变更项目状态。
    /// PUT /api/app/project-info/{id}/status
    /// </summary>
    /// <param name="id">项目唯一标识</param>
    /// <param name="status">目标状态</param>
    Task<ProjectInfoDto> ChangeStatusAsync(Guid id, ProjectStatus status);

    /// <summary>
    /// 删除项目。
    /// DELETE /api/app/project-info/{id}
    /// </summary>
    /// <param name="id">项目唯一标识</param>
    Task DeleteAsync(Guid id);
}
