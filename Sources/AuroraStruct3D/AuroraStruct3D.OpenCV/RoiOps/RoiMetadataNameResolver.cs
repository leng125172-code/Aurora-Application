using System.Text.Json;

namespace AuroraStruct3D.OpenCV.RoiOps;

internal static class RoiMetadataNameResolver
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

    public static string Resolve(string? metadataJson, string fallbackName, string inputName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return fallbackName;
        }

        RoiPartitionMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<RoiPartitionMetadata>(metadataJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"输入端口 '{inputName}' 的 ROI metadata JSON 解析失败。",
                exception
            );
        }

        string? name = metadata?.Rois?.FirstOrDefault()?.Name;
        return string.IsNullOrWhiteSpace(name) ? fallbackName : name;
    }
}
