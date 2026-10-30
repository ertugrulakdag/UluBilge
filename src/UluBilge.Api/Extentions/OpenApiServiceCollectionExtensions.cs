using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using UluBilge.Api.Authentication;
using UluBilge.App.Commands.AuthHandler;

namespace UluBilge.Api.Extentions
{
    public static class OpenApiServiceCollectionExtensions
    {
        public static IServiceCollection AddApiOpenApi(this IServiceCollection services, IHostEnvironment environment)
        {
            services.AddOpenApi(options =>
            {
                // CQRS siniflarinda Command/Result adlari tekrarlanir; dis sinif ve namespace de kimlige katilir.
                options.CreateSchemaReferenceId = typeInfo => OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo) is null
                    ? null
                    : GetSchemaName(typeInfo.Type);
                options.AddDocumentTransformer<BearerSecurityScheme>();
                options.AddOperationTransformer<BearerSecurityScheme>();
                if (environment.IsDevelopment())
                {
                    options.AddOperationTransformer((operation, context, _) =>
                    {
                        if (context.Description.ParameterDescriptions.Any(parameter => parameter.Type == typeof(LoginAsync.Command)) && operation.RequestBody?.Content is { } content)
                        {
                            foreach (var mediaType in content.Values)
                            {
                                mediaType.Example = new JsonObject { ["userName"] = "admin", ["password"] = "admin" };
                            }
                        }

                        return Task.CompletedTask;
                    });
                }
            });

            return services;
        }

        private static string GetSchemaName(Type type)
        {
            if (type.IsArray)
                return GetSchemaName(type.GetElementType()!) + "Array";

            if (!type.IsGenericType)
                return (type.FullName ?? type.Name).Replace('+', '.');

            var definition = type.GetGenericTypeDefinition();
            var name = Regex.Replace(definition.FullName ?? definition.Name, @"`\d+", string.Empty).Replace('+', '.');
            return name + "Of" + string.Join("And", type.GetGenericArguments().Select(GetSchemaName));
        }
    }
}
