using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Swagger;

/// <summary>
/// swagger 枚举映射，
/// 原因：前端代理生成枚举是数字
/// </summary>
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is OpenApiSchema openApiScheme && context.Type.IsEnum)
        {
            // 保持整数类型，与实际 API 序列化行为一致；description 附加枚举名称对照便于阅读
            openApiScheme.Enum?.Clear();
            openApiScheme.Type = JsonSchemaType.Integer;
            openApiScheme.Format = "int32";
            var descriptions = new System.Text.StringBuilder();
            foreach (var value in Enum.GetValues(context.Type))
            {
                var intValue = Convert.ToInt32(value);
                openApiScheme.Enum?.Add(JsonNode.Parse(intValue.ToString())!);
                descriptions.AppendLine($"{intValue} = {value}");
            }
            openApiScheme.Description =
                (openApiScheme.Description ?? string.Empty) + "\n" + descriptions;
        }
    }
}
