using System;
using System.Text.Json;

namespace Volo.Abp.Internal.Telemetry.Activity;

internal static class TelemetryJsonExtensions
{
    internal static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? null
            : null;
    }

    internal static bool? GetBooleanOrNull(JsonElement element, string propertyName)
    {
        if (
            element.TryGetProperty(propertyName, out var property)
            && bool.TryParse(property.GetString(), out var boolValue)
        )
        {
            return boolValue;
        }

        return null;
    }

    internal static DateTimeOffset? GetDateTimeOffsetOrNull(
        JsonElement element,
        string propertyName
    )
    {
        if (
            element.TryGetProperty(propertyName, out var date)
            && DateTimeOffset.TryParse(date.GetString(), out var dateTimeValue)
        )
        {
            return dateTimeValue;
        }

        return null;
    }
}
