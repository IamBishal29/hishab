using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Hishab.Api.OpenApi;

/// <summary>
/// Keeps shared enum schemas free of <c>null</c>. The generator builds an enum's component schema
/// from the first place it meets the type; when that is an optional query parameter (OrderSource?
/// on the export), <c>null</c> lands in the shared enum and every generated client type becomes
/// nullable. Optionality belongs to the parameter or property, not to the enum itself.
/// </summary>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type.IsEnum && schema.Enum is { Count: > 0 } values)
        {
            for (var i = values.Count - 1; i >= 0; i--)
            {
                if (values[i] is null)
                {
                    values.RemoveAt(i);
                }
            }
        }

        return Task.CompletedTask;
    }
}
