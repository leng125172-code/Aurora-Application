using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Values;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>
/// 工作流执行阶段的类型归一化与兼容性判断。
/// </summary>
internal static class WorkflowExecutionTypeNormalizer
{
    private static readonly Dictionary<string, string> AliasMap = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["string"] = "System.String",
        ["system.string"] = "System.String",
        ["int"] = "System.Int32",
        ["int32"] = "System.Int32",
        ["system.int32"] = "System.Int32",
        ["long"] = "System.Int64",
        ["int64"] = "System.Int64",
        ["system.int64"] = "System.Int64",
        ["double"] = "System.Double",
        ["system.double"] = "System.Double",
        ["float"] = "System.Double",
        ["single"] = "System.Double",
        ["system.single"] = "System.Double",
        ["bool"] = "System.Boolean",
        ["boolean"] = "System.Boolean",
        ["system.boolean"] = "System.Boolean",
        [WorkflowValueTypes.Mat] = WorkflowValueTypes.Mat,
        ["matimg"] = WorkflowValueTypes.Mat,
        ["mat"] = WorkflowValueTypes.Mat,
        ["opencvsharp.mat"] = WorkflowValueTypes.Mat,
        [WorkflowValueTypes.PointCloud] = WorkflowValueTypes.PointCloud,
        ["pointclouddata"] = WorkflowValueTypes.PointCloud,
        ["aurorastruct3d.pointclouddata"] = WorkflowValueTypes.PointCloud,
    };

    /// <summary>
    /// 归一化声明类型名。
    /// </summary>
    /// <param name="typeName">声明类型名。</param>
    /// <returns>归一化类型名。</returns>
    public static string NormalizeDeclaredType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        string key = typeName.Trim();
        if (AliasMap.TryGetValue(key, out string? alias))
        {
            return alias;
        }

        Type? resolved = ValueCoercion.ResolveType(key);
        if (resolved is null)
        {
            return key;
        }

        if (resolved == typeof(string))
        {
            return "System.String";
        }

        if (resolved == typeof(int))
        {
            return "System.Int32";
        }

        if (resolved == typeof(long))
        {
            return "System.Int64";
        }

        if (resolved == typeof(float) || resolved == typeof(double))
        {
            return "System.Double";
        }

        if (resolved == typeof(bool))
        {
            return "System.Boolean";
        }

        if (string.Equals(resolved.Name, "Mat", StringComparison.Ordinal))
        {
            return WorkflowValueTypes.Mat;
        }

        if (string.Equals(resolved.Name, "PointCloudData", StringComparison.Ordinal))
        {
            return WorkflowValueTypes.PointCloud;
        }

        return GetCanonicalTypeName(resolved);
    }

    private static string GetCanonicalTypeName(Type type)
    {
        if (type.IsArray)
        {
            return GetCanonicalTypeName(type.GetElementType()!) + "[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        Type definition = type.GetGenericTypeDefinition();
        string definitionName = definition.FullName ?? definition.Name;
        int aritySeparator = definitionName.IndexOf('`');
        if (aritySeparator >= 0)
        {
            definitionName = definitionName[..aritySeparator];
        }

        return definitionName
            + "<"
            + string.Join(",", type.GetGenericArguments().Select(GetCanonicalTypeName))
            + ">";
    }

    /// <summary>
    /// 归一化运行时值类型。
    /// </summary>
    /// <param name="value">运行时值。</param>
    /// <returns>归一化类型名。</returns>
    public static string NormalizeRuntimeValueType(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string)
        {
            return "System.String";
        }

        if (value is bool)
        {
            return "System.Boolean";
        }

        if (value is int)
        {
            return "System.Int32";
        }

        if (value is long)
        {
            return "System.Int64";
        }

        if (value is float or double)
        {
            return "System.Double";
        }

        string token = WorkflowValueSerializer.InferValueType(value);
        if (
            token is WorkflowValueTypes.Mat or WorkflowValueTypes.PointCloud
            && AliasMap.TryGetValue(token, out string? alias)
        )
        {
            return alias;
        }

        return NormalizeDeclaredType(value.GetType().FullName ?? value.GetType().Name);
    }

    /// <summary>
    /// 判断实际类型是否可赋值到声明类型。
    /// </summary>
    /// <param name="declaredType">声明类型。</param>
    /// <param name="runtimeType">运行时类型。</param>
    /// <returns>是否兼容。</returns>
    public static bool IsCompatible(string declaredType, string runtimeType)
    {
        if (string.IsNullOrWhiteSpace(declaredType) || string.IsNullOrWhiteSpace(runtimeType))
        {
            return true;
        }

        string declared = NormalizeDeclaredType(declaredType);
        string runtime = NormalizeDeclaredType(runtimeType);

        if (string.Equals(declared, runtime, StringComparison.Ordinal))
        {
            return true;
        }

        if (declared == "System.Double")
        {
            return runtime is "System.Int32" or "System.Int64";
        }

        if (declared == "System.Int64")
        {
            return runtime == "System.Int32";
        }

        return false;
    }
}
