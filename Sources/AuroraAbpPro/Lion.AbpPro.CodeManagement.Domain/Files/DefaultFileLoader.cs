using Microsoft.Extensions.FileProviders;
using Volo.Abp.VirtualFileSystem;

namespace Lion.AbpPro.CodeManagement.Files;

public class DefaultFileLoader : CodeManagementDomainService, IFileLoader
{
    private readonly IVirtualFileProvider _virtualFileProvider;

    public DefaultFileLoader(IVirtualFileProvider virtualFileProvider)
    {
        _virtualFileProvider = virtualFileProvider;
    }

    public async Task<string> LoadAsync(string path)
    {
        return await _virtualFileProvider.GetFileInfo(path).ReadAsStringAsync();
    }
}
