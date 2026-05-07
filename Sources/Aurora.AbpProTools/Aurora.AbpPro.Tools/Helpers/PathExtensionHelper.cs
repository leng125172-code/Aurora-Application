using System;
using System.IO;

namespace Aurora.AbpPro.Tools.Helpers;

/// <summary>
/// 路径扩展助手：将字符串路径包装为强类型，便于声明配置时直接由字符串赋值；
/// 支持判断存在、创建（文件 / 目录）、删除等常用操作。
/// </summary>
public class PathExtensionHelper
{
    /// <summary>
    /// 包装的原始路径（已规范化为绝对路径，便于跨调用一致性）
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// 路径是否表示文件：优先按已存在实体判断（文件 / 目录）；
    /// 不存在时若以路径分隔符结尾视为目录，否则按是否含扩展名启发式判断。
    /// </summary>
    public bool IsFile
    {
        get
        {
            // 已存在则以实际类型为准，避免「同名目录被当成文件」类误判
            if (Directory.Exists(Path))
            {
                return false;
            }
            if (File.Exists(Path))
            {
                return true;
            }
            // 以分隔符结尾，明确是目录意图
            char last = Path[Path.Length - 1];
            if (
                last == System.IO.Path.DirectorySeparatorChar
                || last == System.IO.Path.AltDirectorySeparatorChar
            )
            {
                return false;
            }
            // 兜底：含扩展名视为文件
            return !string.IsNullOrEmpty(System.IO.Path.GetExtension(Path));
        }
    }

    /// <summary>
    /// 通过字符串构造路径助手（自动规范为绝对路径）
    /// </summary>
    /// <param name="path">原始路径</param>
    public PathExtensionHelper(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空", nameof(path));
        }
        // 转为绝对路径，避免依赖运行时工作目录导致歧义
        Path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>
    /// 隐式将字符串转换为 PathExtensionHelper，使配置字段可直接由字符串赋值
    /// </summary>
    public static implicit operator PathExtensionHelper(string path) => new(path);

    /// <summary>
    /// 隐式将 PathExtensionHelper 转换为字符串，便于与 Path.Combine 等 BCL API 互操作
    /// </summary>
    public static implicit operator string(PathExtensionHelper helper) => helper.Path;

    /// <summary>
    /// 判断路径是否存在（文件或目录任一存在即为 true）
    /// </summary>
    public bool Exists()
    {
        return File.Exists(Path) || Directory.Exists(Path);
    }

    /// <summary>
    /// 创建路径：若为目录则递归创建；若为文件则确保父目录存在并创建空文件（已存在时不覆盖）
    /// </summary>
    /// <returns>当前实例，便于链式调用</returns>
    public PathExtensionHelper Create()
    {
        if (IsFile)
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (!File.Exists(Path))
            {
                using var _ = File.Create(Path);
            }
        }
        else if (!Directory.Exists(Path))
        {
            Directory.CreateDirectory(Path);
        }
        return this;
    }

    /// <summary>
    /// 删除路径：文件直接删除；目录递归删除；不存在时不报错
    /// </summary>
    /// <returns>当前实例，便于链式调用</returns>
    public PathExtensionHelper Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
        else if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
        return this;
    }

    /// <summary>
    /// 返回原始路径字符串
    /// </summary>
    public override string ToString() => Path;

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj switch
        {
            PathExtensionHelper other => string.Equals(
                Path,
                other.Path,
                StringComparison.OrdinalIgnoreCase
            ),
            string s => string.Equals(Path, s, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? string.Empty);
}
