using Microsoft.OpenApi.Models;

namespace ContextMemory.Api.Extensions;

public static class SwaggerServiceCollectionExtensions
{
    public static IServiceCollection AddContextMemorySwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ContextMemory Middleware API",
                Version = "v1",
                Description = """
                    Ollama-compatible context middleware. Most routes require:
                    - `X-App-Id` and `X-User-Id` headers
                    - `Authorization: Bearer {app-api-key}`

                    Admin and app registration use `Authorization: Bearer {master-key}`.
                    """
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "API Key",
                In = ParameterLocation.Header,
                Description = "App API key or Master key (admin/register)."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            options.OperationFilter<ContextMemoryHeadersOperationFilter>();
        });

        return services;
    }

    public static WebApplication UseContextMemorySwagger(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "ContextMemory API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "ContextMemory API";
        });

        return app;
    }
}

internal sealed class ContextMemoryHeadersOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        if (!path.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
            return;

        if (path.Equals("apps/register", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-App-Id",
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema { Type = "string" }
        });
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-User-Id",
            In = ParameterLocation.Header,
            Required = !path.Contains("/wiki", StringComparison.OrdinalIgnoreCase),
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
