using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuroraStruct3D.Swagger;

/// <summary>Adds the optional client display-theme header to every API operation.</summary>
public sealed class ClientThemeHeaderOperationFilter : IOperationFilter
{
    public const string HeaderName = "X-Aurora-Theme";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(parameter.Name, HeaderName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = HeaderName,
            In = ParameterLocation.Header,
            Required = false,
            Description = "客户端实际显示主题：light 或 dark。缺失或无效时按 light 处理。",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
    }
}
