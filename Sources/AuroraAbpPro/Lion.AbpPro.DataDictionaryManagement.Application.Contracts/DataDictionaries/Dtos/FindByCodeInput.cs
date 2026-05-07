namespace Lion.AbpPro.DataDictionaryManagement.DataDictionaries.Dtos;

public class FindByCodeInput
{
    [Required(ErrorMessage = "Code不能为空")]
    public string Code { get; set; }
}
