namespace Lion.AbpPro.CodeManagement.Projects.Dto.Generators;

public class GeneratorTemplateContext
{
    /// <summary>
    /// 项目信息
    /// </summary>
    public GeneratorProjectContext Project { get; set; }

    /// <summary>
    ///
    /// </summary>
    public GeneratorTreeEntityModelContext EntityModel { get; set; }

    public GeneratorEnumTypeContext EnumType { get; set; }

    public List<GeneratorEntityModelContext> AllEntityModels { get; set; }
}
