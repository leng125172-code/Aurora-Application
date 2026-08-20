using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// Compiled, case-sensitive access to a workflow value expression such as
/// <c>inspection_result.resultCode</c> or <c>result.artifacts[0].name</c>.
/// </summary>
public sealed class WorkflowValueAccessor
{
    private readonly IReadOnlyList<object> _segments;

    public string Expression { get; }
    public string RootVariableName { get; }

    private WorkflowValueAccessor(string expression, string root, IReadOnlyList<object> segments)
    {
        Expression = expression;
        RootVariableName = root;
        _segments = segments;
    }

    public static WorkflowValueAccessor Compile(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        expression = expression.Trim();
        int position = 0;
        string root = ReadIdentifier(expression, ref position);
        List<object> segments = [];
        while (position < expression.Length)
        {
            if (expression[position] == '.')
            {
                position++;
                segments.Add(ReadIdentifier(expression, ref position));
                continue;
            }
            if (expression[position] == '[')
            {
                int close = expression.IndexOf(']', ++position);
                if (close < 0 || !int.TryParse(expression[position..close], out int index) || index < 0)
                    throw new FormatException($"Invalid array index in workflow expression '{expression}'.");
                segments.Add(index);
                position = close + 1;
                continue;
            }
            throw new FormatException($"Invalid character at position {position + 1} in workflow expression '{expression}'.");
        }
        return new WorkflowValueAccessor(expression, root, segments);
    }

    public object? Resolve(IWorkflowContext context)
    {
        object? value = context.Get(RootVariableName);
        foreach (object segment in _segments)
        {
            if (value is null)
                return null;
            value = segment is int index ? ReadIndex(value, index) : ReadMember(value, (string)segment);
        }
        return value;
    }

    private static object? ReadMember(object value, string member)
    {
        if (member == "Length" && value is not string && value is IEnumerable enumerable)
            return value is ICollection collection ? collection.Count : enumerable.Cast<object?>().Count();
        if (value is JsonElement element)
        {
            if (member == "Length" && element.ValueKind == JsonValueKind.Array)
                return element.GetArrayLength();
            if (element.ValueKind == JsonValueKind.String)
                return ReadMember(JsonNode.Parse(element.GetString()!)!, member);
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(member, out JsonElement property))
                return property.Clone();
            throw new InvalidOperationException($"Member '{member}' does not exist on JSON value.");
        }
        if (value is string json && (json.TrimStart().StartsWith('{') || json.TrimStart().StartsWith('[')))
            return ReadMember(JsonNode.Parse(json)!, member);
        if (value is JsonObject jsonObject && jsonObject.TryGetPropertyValue(member, out JsonNode? node))
            return node;
        if (value is IDictionary dictionary && dictionary.Contains(member))
            return dictionary[member];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        Type type = value.GetType();
        PropertyInfo? propertyInfo = type.GetProperty(member, flags);
        propertyInfo ??= type.GetProperties(flags).FirstOrDefault(property =>
            string.Equals(JsonNamingPolicy.CamelCase.ConvertName(property.Name), member,
                StringComparison.Ordinal)
            || string.Equals(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name,
                member, StringComparison.Ordinal));
        if (propertyInfo is not null)
            return propertyInfo.GetValue(value);
        FieldInfo? fieldInfo = type.GetField(member, flags);
        if (fieldInfo is not null)
            return fieldInfo.GetValue(value);
        throw new InvalidOperationException($"Member '{member}' does not exist on type '{type.Name}'.");
    }

    private static object? ReadIndex(object value, int index)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().ElementAtOrDefault(index).Clone();
        if (value is JsonArray jsonArray)
            return index < jsonArray.Count ? jsonArray[index] : throw new IndexOutOfRangeException();
        if (value is IList list)
            return index < list.Count ? list[index] : throw new IndexOutOfRangeException();
        if (value is Array array)
            return index < array.Length ? array.GetValue(index) : throw new IndexOutOfRangeException();
        if (value is IEnumerable enumerable)
        {
            IEnumerator iterator = enumerable.GetEnumerator();
            try
            {
                for (int current = 0; iterator.MoveNext(); current++)
                    if (current == index) return iterator.Current;
            }
            finally { (iterator as IDisposable)?.Dispose(); }
            throw new IndexOutOfRangeException();
        }
        throw new InvalidOperationException($"Value of type '{value.GetType().Name}' is not an array.");
    }

    private static string ReadIdentifier(string value, ref int position)
    {
        int start = position;
        if (position >= value.Length || !(char.IsLetter(value[position]) || value[position] == '_'))
            throw new FormatException($"Expected identifier at position {position + 1} in workflow expression '{value}'.");
        position++;
        while (position < value.Length && (char.IsLetterOrDigit(value[position]) || value[position] == '_'))
            position++;
        return value[start..position];
    }
}
