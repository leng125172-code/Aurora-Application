namespace Lion.AbpPro.CodeManagement.Files;

public interface IFileLoader
{
    Task<string> LoadAsync(string sqlPath);
}
