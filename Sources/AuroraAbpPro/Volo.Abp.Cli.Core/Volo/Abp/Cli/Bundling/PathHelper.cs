using System;
using System.IO;
using System.Linq;

namespace Volo.Abp.Cli.Bundling;

internal static class PathHelper
{
    internal static string GetWebAssemblyFrameworkFolderPath(
        string projectDirectory,
        string frameworkVersion
    )
    {
        return Path.Combine(
            projectDirectory,
            "bin",
            "Debug",
            frameworkVersion,
            "wwwroot",
            "_framework"
        );
    }

    internal static string GetWebAssemblyFilePath(
        string directory,
        string frameworkVersion,
        string projectFileName
    )
    {
        var outputDirectory = Path.Combine(directory, "bin", "Debug", frameworkVersion);
        var path = Path.Combine(outputDirectory, projectFileName + ".dll");
        return !File.Exists(path) ? null : path;
    }

    internal static string GetMauiBlazorAssemblyFilePath(string directory, string projectFileName)
    {
        return Directory
            .GetFiles(Path.Combine(directory, "bin"), "*.dll", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                !f.Contains("android")
                && !f.Contains("windows10")
                && f.EndsWith(projectFileName + ".dll", StringComparison.OrdinalIgnoreCase)
            );
    }

    internal static string GetWwwRootPath(string directory)
    {
        return Path.Combine(directory, "wwwroot");
    }
}
