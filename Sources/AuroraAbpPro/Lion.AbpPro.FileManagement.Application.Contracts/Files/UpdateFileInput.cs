using Microsoft.AspNetCore.Http;

namespace Lion.AbpPro.FileManagement.Files;

public class UpdateFileInput
{
    public List<IFormFile> Files { get; set; }
}
