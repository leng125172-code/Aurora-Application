using System;
using System.IO;
using Aurora.AbpPro.Tools.Helpers;

namespace Aurora.AbpPro.Tools.Models;

/// <summary>
/// 只读配置模型：集中放置工具运行所需的固定路径；
/// 字段类型为 <see cref="PathExtensionHelper"/>，可由字符串隐式赋值，且自带存在/创建/删除等扩展能力。
/// </summary>
public class ReadonlyConfig
{
    /// <summary>
    /// 工具数据根目录：%UserProfile%\.aurora.abp.pro
    /// </summary>
    public PathExtensionHelper RootPath { get; }

    /// <summary>
    /// 代码库根路径
    /// </summary>
    public PathExtensionHelper BasePath { get; }

    /// <summary>
    /// GitHub Release 压缩包下载目录：%UserProfile%\.aurora.abp.pro\Download
    /// </summary>
    public PathExtensionHelper DownloadPath { get; }

    /// <summary>
    /// Release 解压根目录：%UserProfile%\.aurora.abp.pro\Source
    /// </summary>
    public PathExtensionHelper SourcePath { get; }

    /// <summary>
    /// 工具数据目录（Token / 版本映射等）：%UserProfile%\.aurora.abp.pro\Data
    /// </summary>
    public PathExtensionHelper DataPath { get; }

    /// <summary>
    /// GitHub Token 持久化文件路径：%UserProfile%\.aurora.abp.pro\Data\GithubToken
    /// </summary>
    public PathExtensionHelper TokenFilePath { get; }

    public ReadonlyConfig()
    {
        BasePath = @"D:\GitRepos";
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aurora.abp.pro"
        );
        RootPath = root;
        DownloadPath = Path.Combine(root, "Download");
        SourcePath = Path.Combine(root, "Source");
        DataPath = Path.Combine(root, "Data");
        TokenFilePath = Path.Combine(root, "Data", "GithubToken");

        // 启动时确保目录存在，避免后续每个调用都判空创建
        DownloadPath.Create();
        SourcePath.Create();
        DataPath.Create();
    }
}
