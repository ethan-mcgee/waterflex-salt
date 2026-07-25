using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WaterFlex.SaltMonitor.Api;

public sealed class DeviceTokenSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
	public Task TransformAsync(
		OpenApiDocument document,
		OpenApiDocumentTransformerContext context,
		CancellationToken cancellationToken)
	{
		document.Info = new()
		{
			Title = "WaterFlex Salt Monitor API",
			Version = "v1",
			Description = "Device telemetry ingestion and WaterFlex salt-monitoring operations."
		};
		document.Components ??= new OpenApiComponents();
		document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
		document.Components.SecuritySchemes[DeviceTokenAuthenticationHandler.SchemeName] =
			new OpenApiSecurityScheme
			{
				Type = SecuritySchemeType.Http,
				Scheme = "bearer",
				In = ParameterLocation.Header,
				BearerFormat = "<credential-id>.<device-secret>",
				Description = "Unique device token issued during commissioning. Paste the token only; Swagger adds the Bearer prefix."
			};

		return Task.CompletedTask;
	}
}

public sealed class DeviceTokenSecurityRequirementTransformer : IOpenApiOperationTransformer
{
	public Task TransformAsync(
		OpenApiOperation operation,
		OpenApiOperationTransformerContext context,
		CancellationToken cancellationToken)
	{
		var metadata = context.Description.ActionDescriptor.EndpointMetadata;
		var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
		var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

		if (!requiresAuthorization || allowsAnonymous)
		{
			return Task.CompletedTask;
		}

		operation.Security ??= [];
		operation.Security.Add(new OpenApiSecurityRequirement
		{
			[new OpenApiSecuritySchemeReference(
				DeviceTokenAuthenticationHandler.SchemeName,
				context.Document)] = []
		});

		return Task.CompletedTask;
	}
}