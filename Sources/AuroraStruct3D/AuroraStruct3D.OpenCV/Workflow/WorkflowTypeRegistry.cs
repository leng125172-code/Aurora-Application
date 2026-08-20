using System.Collections.Concurrent;

namespace AuroraStruct3D.OpenCV.Workflow;

public static class WorkflowTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Type> Types = new(StringComparer.Ordinal);

    static WorkflowTypeRegistry()
    {
        Register<string>(); Register<bool>(); Register<int>(); Register<long>();
        Register<float>(); Register<double>(); Register<decimal>(); Register<Guid>();
        Register<DateTime>(); Register<InspectionResultCode>();
        Types["string"] = typeof(string); Types["bool"] = typeof(bool);
        Types["int"] = typeof(int); Types["long"] = typeof(long);
        Types["float"] = typeof(float); Types["double"] = typeof(double);
        Types["decimal"] = typeof(decimal);
        Register<FlatnessInspectionDetails>(); Register<ShapeInspectionDetails>();
        Register<ToleranceMeasurementDetails>(); Register<DimensionAngleInspectionDetails>();
        Register<LineFitInspectionDetails>(); Register<CircleFitInspectionDetails>();
        Register<HeightDiffInspectionDetails>(); Register<RegionPointDetails>();
        Register<PlaneHeightDiffInspectionDetails>(); Register<ContourInspectionDetails>();
        Register<InstallationAxisInspectionDetails>(); Register<InstallationPlaneToleranceDetails>();
        Register<InstallationPlaneInspectionDetails>(); Register<CombinedInspectionDetails>();
        Register<InspectionArtifact>(); Register<ProductInspectionResult>();
    }

    public static void Register<T>() => Register(typeof(T));

    public static void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsPublic && !type.IsNestedPublic)
            throw new ArgumentException($"工作流类型必须公开：{type.FullName}", nameof(type));
        Types[type.Name] = type;
        if (type.FullName is { } fullName) Types[fullName] = type;
    }

    public static Type? Resolve(string typeName)
    {
        string value = typeName.Trim();
        if (value.EndsWith("[]", StringComparison.Ordinal))
            return Resolve(value[..^2])?.MakeArrayType();
        if (value.EndsWith("?", StringComparison.Ordinal))
        {
            Type? inner = Resolve(value[..^1]);
            return inner is { IsValueType: true } ? typeof(Nullable<>).MakeGenericType(inner) : inner;
        }
        int genericStart = value.IndexOf('<');
        if (genericStart > 0 && value.EndsWith('>'))
        {
            string outer = value[..genericStart].Trim();
            string[] arguments = SplitGenericArguments(value[(genericStart + 1)..^1]);
            if (arguments.Length != 1) return null;
            Type? element = Resolve(arguments[0]);
            if (element is null) return null;
            return outer switch
            {
                "List" or "System.Collections.Generic.List" => typeof(List<>).MakeGenericType(element),
                "IReadOnlyList" or "System.Collections.Generic.IReadOnlyList" => typeof(IReadOnlyList<>).MakeGenericType(element),
                "InspectionResult" or "AuroraStruct3D.OpenCV.Workflow.InspectionResult" =>
                    typeof(InspectionResult<>).MakeGenericType(element),
                _ => null,
            };
        }
        return Types.GetValueOrDefault(value);
    }

    public static IReadOnlyCollection<Type> GetRegisteredTypes() => Types.Values.Distinct().ToArray();

    public static bool IsRegistered(Type type)
    {
        if (Types.Values.Contains(type)) return true;
        if (type.IsArray) return IsRegistered(type.GetElementType()!);
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return IsRegistered(nullable);
        return type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(List<>)
                || type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                || type.GetGenericTypeDefinition() == typeof(InspectionResult<>))
            && type.GetGenericArguments().All(IsRegistered);
    }

    private static string[] SplitGenericArguments(string value)
    {
        List<string> values = [];
        int depth = 0, start = 0;
        for (int index = 0; index < value.Length; index++)
            switch (value[index])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0:
                    values.Add(value[start..index].Trim()); start = index + 1; break;
            }
        values.Add(value[start..].Trim());
        return values.Where(x => x.Length > 0).ToArray();
    }
}
