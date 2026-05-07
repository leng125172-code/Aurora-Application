using System.IO;
using System.Text.Json;
using Aurora.AbpPro.Tools.Models;
using Microsoft.Extensions.Options;

namespace Aurora.AbpPro.Tools.Services;

/// <summary>
/// 应用配置服务接口
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// 当前生效的配置
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// 持久化配置到本地用户目录
    /// </summary>
    void Save();
}

/// <summary>
/// 应用配置服务实现：启动时从 Options 加载默认值，运行时变更可持久化到 LocalAppData
/// </summary>
public class AppSettingsService : IAppSettingsService
{
    /// <summary>
    /// 用户配置文件路径（位于 %LOCALAPPDATA%/Aurora.AbpPro.Tools/settings.json）
    /// </summary>
    private static readonly string UserSettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aurora.AbpPro.Tools",
        "settings.json"
    );

    public AppSettings Current { get; }

    private readonly ReadonlyConfig _readonlyConfig;

    public AppSettingsService(IOptions<AppSettings> options, ReadonlyConfig readonlyConfig)
    {
        _readonlyConfig = readonlyConfig;
        // 优先读取用户目录中的覆盖文件，否则使用默认配置
        Current = LoadFromUserFile() ?? options.Value ?? new AppSettings();

        // 从独立的 GithubToken 文件读取 Token（优先级最高），便于用户手工修改
        var tokenFromFile = LoadTokenFromFile(_readonlyConfig.TokenFilePath);
        if (!string.IsNullOrWhiteSpace(tokenFromFile))
        {
            Current.GitHubToken = tokenFromFile;
        }
    }

    /// <summary>
    /// 读取 GithubToken 文件（首行非空内容作为 Token），不存在或读取失败时返回 null
    /// </summary>
    private static string? LoadTokenFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 将 GithubToken 单独写入 Data\GithubToken 文件（覆盖写入）
    /// </summary>
    private void SaveTokenToFile()
    {
        try
        {
            var path = (string)_readonlyConfig.TokenFilePath;
            var directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, Current.GitHubToken ?? string.Empty);
        }
        catch
        {
            // 忽略保存异常，避免影响主流程
        }
    }

    /// <summary>
    /// 从用户目录加载配置（不存在时返回 null）
    /// </summary>
    private static AppSettings? LoadFromUserFile()
    {
        try
        {
            if (!File.Exists(UserSettingsFile))
            {
                return null;
            }
            var json = File.ReadAllText(UserSettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(UserSettingsFile)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var json = JsonSerializer.Serialize(
                Current,
                new JsonSerializerOptions { WriteIndented = true }
            );
            File.WriteAllText(UserSettingsFile, json);

            // Token 同步落地到独立文件，方便其他工具读取
            SaveTokenToFile();
        }
        catch
        {
            // 忽略保存异常，避免影响主流程
        }
    }
}
