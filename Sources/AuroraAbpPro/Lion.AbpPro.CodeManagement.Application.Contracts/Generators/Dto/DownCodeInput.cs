namespace Lion.AbpPro.CodeManagement.Generators.Dto;

public class DownCodeInput
{
    public Guid TemplateId { get; set; }

    public Guid ProjectId { get; set; }

    public List<Guid> EntityId { get; set; }
}
