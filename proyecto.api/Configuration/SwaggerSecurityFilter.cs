using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using proyecto.api.Controllers;

namespace proyecto.api.Configuration;

// documenta las credenciales reales, la autorizacion sigue en sus handlers
public sealed class SwaggerSecurityFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        foreach (var description in context.ApiDescriptions)
        {
            var path = "/" + description.RelativePath?.Split('?')[0];
            if (!document.Paths.TryGetValue(path, out var item) || item.Operations is null)
                continue;
            var operation = item.Operations.FirstOrDefault(pair =>
                pair.Key.ToString().Equals(description.HttpMethod, StringComparison.OrdinalIgnoreCase)).Value;
            if (operation is null)
                continue;

            var controller = description.ActionDescriptor as ControllerActionDescriptor;
            if (controller?.ControllerTypeInfo.AsType() == typeof(MlAgentController))
            {
                operation.Security = [new OpenApiSecurityRequirement {
                    [new OpenApiSecuritySchemeReference("MlAgent", document)] = []
                }];
            }
            else if (description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                operation.Security = [];
            else if (description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
                operation.Security = [new OpenApiSecurityRequirement {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                }];
            else
                operation.Security = [];
        }
    }
}
