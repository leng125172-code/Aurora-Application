using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AuroraStruct3D.OpenCV.Workflow;

[JsonConverter(typeof(JsonStringEnumConverter<InspectionResultCode>))]
public enum InspectionResultCode { OK, NG, UNKNOWN, ERROR }

public abstract class InspectionResultBase
{
    public bool isValid { get; init; }
    public bool isOk { get; init; }
    public InspectionResultCode resultCode { get; init; }
    public string message { get; init; } = string.Empty;
    public IReadOnlyList<string> reasons { get; init; } = [];
    public JsonNode? ToDetailsNode()
    {
        object? value = GetType().GetProperty("details")?.GetValue(this);
        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            _ => JsonSerializer.SerializeToNode(value, value.GetType()),
        };
    }
    public JsonNode ToNode() => JsonSerializer.SerializeToNode(this, GetType())
        ?? new JsonObject();
}

public class InspectionResult<TDetails> : InspectionResultBase
{
    public TDetails? details { get; init; }
}


public sealed class InspectionArtifact
{
    public string name { get; init; } = string.Empty;
    public string? url { get; init; }
    public string? mediaType { get; init; }
}

public sealed class ProductInspectionResult
{
    public bool isValid { get; init; }
    public bool isOk { get; init; }
    public InspectionResultCode resultCode { get; init; }
    public string message { get; init; } = string.Empty;
    public IReadOnlyList<string> reasons { get; init; } = [];
    public JsonNode comprehensive { get; init; } = new JsonObject();
    public JsonNode installation { get; init; } = new JsonObject();
    public IReadOnlyList<InspectionArtifact> artifacts { get; init; } = [];
}

public sealed record FlatnessInspectionDetails(string status, bool isValid, bool isOk,
    double measuredFlatness, double maxFlatness, double flatnessDeviation,
    double maxAbsoluteDistance, double allowedAbsoluteDistance, bool flatnessOk,
    bool absoluteDistanceOk, JsonElement? measurement);
public sealed record ShapeInspectionDetails(string status, bool isValid, bool isOk,
    string expectedShape, int expectedCount, int detectedCount, bool allowAdditional,
    JsonElement? detection);
public sealed record ToleranceMeasurementDetails(bool enabled, double measured, double nominal,
    double deviation, double minDeviation, double maxDeviation, bool isOk);
public sealed record DimensionAngleInspectionDetails(string status, bool isValid, bool isOk,
    ToleranceMeasurementDetails length, ToleranceMeasurementDetails angle);
public sealed record LineFitInspectionDetails(double angleDegrees, string referenceAxis,
    double minAngle, double maxAngle, double rmse, bool isOk);
public sealed record CircleFitInspectionDetails(double radius, double minRadius, double maxRadius,
    double rmse, bool isOk);
public sealed record HeightDiffInspectionDetails(double heightA, double heightB, double signedDiff,
    double absDiff, double minDiff, double maxDiff, bool isOk);
public sealed record RegionPointDetails(string name, double x, double y, double z);
public sealed record PlaneHeightDiffInspectionDetails(RegionPointDetails refRegion,
    RegionPointDetails targetRegion, double signedDiff, double absDiff, double minDiff,
    double maxDiff, bool isOk);
public sealed record ContourInspectionDetails(int contourCount, int minCount, int maxCount);
public sealed class InstallationAxisInspectionDetails
{
    public double? tiltX { get; init; }
    public double? tiltY { get; init; }
    public double? axisToNormalAngle { get; init; }
    public double? axisToPlaneAngle { get; init; }
    public double? deviationX { get; init; }
    public double? deviationY { get; init; }
    public double? totalDeviation { get; init; }
    public double? axisAngle { get; init; }
    public double? angleDeviation { get; init; }
    public double? twistZ { get; init; }
    public double? twistDeviation { get; init; }
    public double? planeRmse { get; init; }
    public double? axisRmse { get; init; }
    public double? referenceRmse { get; init; }
    public double? measuredRmse { get; init; }
    public IReadOnlyList<string> reasons { get; init; } = [];
}
public sealed record InstallationPlaneToleranceDetails(double minDeviationX, double maxDeviationX,
    double minDeviationY, double maxDeviationY, double maxTotalDeviation, double maxFitRmse,
    int minPointCount);
public sealed record InstallationPlaneInspectionDetails(string status, bool isValid, bool isOk,
    double tiltX, double tiltY, double totalTilt, double nominalTiltX, double nominalTiltY,
    double deviationX, double deviationY, double totalDeviation, double referenceRmse,
    double measuredRmse, int referencePointCount, int measuredPointCount,
    IReadOnlyList<string> qualityReasons, InstallationPlaneToleranceDetails tolerance);
public sealed record CombinedInspectionDetails(InspectionResultBase flatness,
    InspectionResultBase line, InspectionResultBase circle);

internal static class InspectionResultSchema
{
    public const string Inspection =
        """
        {"type":"object","title":"InspectionResult","required":["isValid","isOk","resultCode"],"properties":{"isValid":{"type":"boolean"},"isOk":{"type":"boolean"},"resultCode":{"type":"string","enum":["OK","NG","UNKNOWN","ERROR"]},"message":{"type":"string"},"reasons":{"type":"array","items":{"type":"string"}},"details":{"type":"object"}}}
        """;

    public const string Product =
        """
        {"type":"object","title":"ProductInspectionResult","required":["isValid","isOk","resultCode","comprehensive","installation"],"properties":{"isValid":{"type":"boolean"},"isOk":{"type":"boolean"},"resultCode":{"type":"string","enum":["OK","NG","UNKNOWN","ERROR"]},"message":{"type":"string"},"reasons":{"type":"array","items":{"type":"string"}},"comprehensive":{"type":"object","properties":{"isValid":{"type":"boolean"},"isOk":{"type":"boolean"},"resultCode":{"type":"string","enum":["OK","NG","UNKNOWN","ERROR"]},"message":{"type":"string"},"reasons":{"type":"array","items":{"type":"string"}},"details":{"type":"object"}}},"installation":{"type":"object","properties":{"isValid":{"type":"boolean"},"isOk":{"type":"boolean"},"resultCode":{"type":"string","enum":["OK","NG","UNKNOWN","ERROR"]},"message":{"type":"string"},"reasons":{"type":"array","items":{"type":"string"}},"details":{"type":"object"}}},"artifacts":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"url":{"type":"string"},"mediaType":{"type":"string"}}}}}}
        """;
}

public static class WorkflowJsonSchema
{
    public static string For<T>() => For(typeof(T));

    public static string For(Type type) => Build(type, new HashSet<Type>()).ToJsonString();

    private static JsonObject Build(Type type, HashSet<Type> stack)
    {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return NullableSchema(Build(nullable, stack));
        if (type.IsEnum)
            return new JsonObject
            {
                ["type"] = "string",
                ["x-clr-type"] = type.FullName,
                ["enum"] = new JsonArray(Enum.GetNames(type).Select(name => JsonValue.Create(name)).ToArray()),
            };
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int)
            || type == typeof(long) || type == typeof(uint) || type == typeof(ulong))
            return new JsonObject { ["type"] = "integer" };
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };
        Type? elementType = type.IsArray ? type.GetElementType()
            : type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
                ? type.GetGenericArguments().FirstOrDefault() : null;
        if (elementType is not null)
            return new JsonObject { ["type"] = "array", ["items"] = Build(elementType, stack) };
        if (!stack.Add(type)) return new JsonObject { ["type"] = "object" };
        JsonObject properties = [];
        var nullability = new System.Reflection.NullabilityInfoContext();
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (property.GetMethod is not null)
            {
                JsonObject propertySchema = Build(property.PropertyType, stack);
                if (!property.PropertyType.IsValueType
                    && nullability.Create(property).ReadState == System.Reflection.NullabilityState.Nullable)
                    propertySchema = NullableSchema(propertySchema);
                properties[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = propertySchema;
            }
        stack.Remove(type);
        return new JsonObject
        {
            ["type"] = "object",
            ["title"] = type.Name,
            ["properties"] = properties,
        };
    }

    private static JsonObject NullableSchema(JsonObject valueSchema) => new()
    {
        ["anyOf"] = new JsonArray(valueSchema, new JsonObject { ["type"] = "null" }),
    };
}

public static class InspectionResults
{
    public static InspectionResult<TDetails> CreateTyped<TDetails>(
        bool isValid, bool isOk, TDetails details, string? message = null)
    {
        InspectionResultCode code = !isValid ? InspectionResultCode.UNKNOWN
            : isOk ? InspectionResultCode.OK : InspectionResultCode.NG;
        return new InspectionResult<TDetails>
        {
            isValid = isValid,
            isOk = isOk,
            resultCode = code,
            message = message ?? code.ToString(),
            reasons = isOk ? [] : [message ?? code.ToString()],
            details = details,
        };
    }

    public static VisionParameter<InspectionResult<TDetails>> Output<TDetails>(string displayName) =>
        new()
        {
            ParameterName = "result",
            DisplayName = displayName,
            ParameterType = typeof(InspectionResult<TDetails>),
            JsonSchema = WorkflowJsonSchema.For<InspectionResult<TDetails>>(),
        };
}
