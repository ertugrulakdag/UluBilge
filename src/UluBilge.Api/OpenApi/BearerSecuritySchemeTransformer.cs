using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using UluBilge.Api.Localization;

namespace UluBilge.Api.OpenApi
{
    internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
    {
        private const string SchemeName = "Bearer";

        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                //Description = "Geçerli bir erişim belirteci girin. Authorization başlığında Bearer şeması kullanılır."
            };

            return Task.CompletedTask;
        }

        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (operation.Responses is not null)
            {
                foreach (var (status, response) in operation.Responses)
                {
                    if (int.TryParse(status, out var statusCode) && response is OpenApiResponse description && description.Description == ReasonPhrases.GetReasonPhrase(statusCode))
                    {
                        description.Description = TurkishApiMessages.GetStatusTitle(statusCode);
                    }
                }
            }

            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            // AllowAnonymous belirtilmedikçe API'nin varsayılan politikası kimlik doğrulama gerektirir.
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = []
            });

            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", new OpenApiResponse
            {
                Description = "Kimlik doğrulaması gereklidir veya erişim belirteci geçersizdir ya da süresi dolmuştur."
            });
            operation.Responses.TryAdd("403", new OpenApiResponse
            {
                Description = "Kimliği doğrulanmış kullanıcının bu kaynağa erişim yetkisi yoktur."
            });

            return Task.CompletedTask;
        }
    }
}
