using Lion.AbpPro.CodeManagement.Generators.Dto;
using Lion.AbpPro.CodeManagement.Templates;
using Microsoft.AspNetCore.Mvc;

namespace Lion.AbpPro.CodeManagement.Generators;

public interface IGeneratorAppService : IApplicationService
{
    /// <summary>
    /// 预览代码生成
    /// </summary>
    Task<List<TemplateTreeDto>> PreViewCodeAsync(PreViewCodeInput input);

    /// <summary>
    /// 下载源码
    /// </summary>
    Task<ActionResult> DownCodeAsync(DownCodeInput input);
}
