using System.Net;
using Lion.AbpPro.CodeManagement.Generators;
using Lion.AbpPro.CodeManagement.Generators.Dto;
using Lion.AbpPro.CodeManagement.Templates;

namespace Lion.AbpPro.CodeManagement;

[Route("Generator")]
public class GeneratorController : CodeManagementController, IGeneratorAppService
{
    private readonly IGeneratorAppService _generatorAppService;

    public GeneratorController(IGeneratorAppService generatorAppService)
    {
        _generatorAppService = generatorAppService;
    }

    [HttpPost("PreViewCode")]
    [SwaggerOperation(summary: "预览", Tags = new[] { "Generator" })]
    public Task<List<TemplateTreeDto>> PreViewCodeAsync(PreViewCodeInput input)
    {
        return _generatorAppService.PreViewCodeAsync(input);
    }

    [HttpPost("Down")]
    [SwaggerOperation(summary: "下载", Tags = new[] { "Generator" })]
    [ProducesResponseType(typeof(FileContentResult), (int)HttpStatusCode.OK)]
    public Task<ActionResult> DownCodeAsync(DownCodeInput input)
    {
        return _generatorAppService.DownCodeAsync(input);
    }
}
