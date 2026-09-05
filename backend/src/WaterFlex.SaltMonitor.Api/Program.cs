using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Api;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Operations;
using WaterFlex.SaltMonitor.Provisioning;

var builder = WebApplication.CreateBuilder(args);

// The API is container-first. Console logging is deterministic in containers,
// local development, and unprivileged test processes; the Windows Event Log
// provider can throw when the process cannot create or write an event source.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

const int maximumTelemetryBodyBytes = 64 * 1024;

builder.WebHost.ConfigureKestrel(options =>
	options.Limits.MaxRequestBodySize = maximumTelemetryBodyBytes);
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(options =>
{
	options.EnableForHttps = true;
	options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
	options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddOpenApi(options =>
{
	options.AddDocumentTransformer<DeviceTokenSecuritySchemeTransformer>();
	options.AddOperationTransformer<DeviceTokenSecurityRequirementTransformer>();
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
	options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddSaltMonitorPersistence();
builder.Services
    .AddAuthentication()
	.AddScheme<AuthenticationSchemeOptions, DeviceTokenAuthenticationHandler>(
		DeviceTokenAuthenticationHandler.SchemeName,
		_ => { })
	.AddScheme<AuthenticationSchemeOptions, StaffAuthenticationHandler>(
		StaffAuthenticationHandler.SchemeName,
		_ => { });
builder.Services.AddHttpClient(nameof(CloudflareAccessTokenValidator), client =>
{
	client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.Configure<CloudflareAccessOptions>(
	builder.Configuration.GetSection(CloudflareAccessOptions.SectionName));
builder.Services.Configure<FactoryProvisioningOptions>(
	builder.Configuration.GetSection(FactoryProvisioningOptions.SectionName));
builder.Services.AddSingleton<ICloudflareAccessTokenValidator, CloudflareAccessTokenValidator>();
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddSingleton<IFactoryBundleStorage, FactoryBundleStorage>();
builder.Services.AddAuthorization(options =>
{
	options.AddPolicy(DevelopmentIdentity.AuthenticatedPolicy, policy => policy
		.AddAuthenticationSchemes(StaffAuthenticationHandler.SchemeName)
		.RequireAuthenticatedUser());
	options.AddPolicy(DevelopmentIdentity.ActivationPolicy, policy => policy
		.AddAuthenticationSchemes(StaffAuthenticationHandler.SchemeName)
		.RequireAuthenticatedUser()
		.RequireClaim("staff_activation_candidate", "true"));
	foreach (var role in Enum.GetValues<StaffRole>())
	{
		options.AddPolicy(DevelopmentIdentity.PolicyName(role), policy => policy
			.AddAuthenticationSchemes(StaffAuthenticationHandler.SchemeName)
			.RequireAuthenticatedUser()
			.RequireRole(role.ToString()));
	}
	foreach (var capability in Enum.GetValues<StaffCapability>())
	{
		options.AddPolicy(DevelopmentIdentity.CapabilityPolicyName(capability), policy => policy
			.AddAuthenticationSchemes(StaffAuthenticationHandler.SchemeName)
			.RequireAuthenticatedUser()
			.RequireAssertion(context => context.User.Claims
				.Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role)
				.Select(claim => Enum.TryParse<StaffRole>(claim.Value, out var role) ? role : (StaffRole?)null)
				.Any(role => role?.HasCapability(capability) == true)));
	}
});
if (!builder.Environment.IsDevelopment())
{
	builder.Services.Configure<ForwardedHeadersOptions>(options =>
	{
		options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
			| ForwardedHeaders.XForwardedProto
			| ForwardedHeaders.XForwardedHost;
		// The API is reachable only from the Nginx service on the private Docker
		// network in staging. Container addresses are dynamic, so trust the
		// immediate private proxy rather than pinning an ephemeral address.
		options.KnownIPNetworks.Clear();
		options.KnownProxies.Clear();
		options.ForwardLimit = 1;
	});
}
builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.OnRejected = async (context, cancellationToken) =>
	{
		context.HttpContext.Response.Headers.RetryAfter = "60";
		await context.HttpContext.Response.WriteAsJsonAsync(
			new { errorCode = "rate_limited", retryAfterSeconds = 60 },
			cancellationToken);
	};
	static RateLimitPartition<string> FixedIpLimit(HttpContext context, string policy, int permits, TimeSpan window)
	{
		var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		return RateLimitPartition.GetFixedWindowLimiter(
			$"{policy}:{address}",
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permits,
				Window = window,
				QueueLimit = 0,
				AutoReplenishment = true
			});
	}
	static RateLimitPartition<string> FixedStaffLimit(HttpContext context, string policy, int permits, TimeSpan window)
	{
		var staffId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
			?? context.Connection.RemoteIpAddress?.ToString()
			?? "unknown";
		return RateLimitPartition.GetFixedWindowLimiter(
			$"{policy}:{staffId}",
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permits,
				Window = window,
				QueueLimit = 0,
				AutoReplenishment = true
			});
	}
	options.AddPolicy(RateLimitPolicies.Device, context => FixedIpLimit(context, RateLimitPolicies.Device, 30, TimeSpan.FromMinutes(1)));
	options.AddPolicy(RateLimitPolicies.Activation, context => FixedIpLimit(context, RateLimitPolicies.Activation, 10, TimeSpan.FromMinutes(15)));
	options.AddPolicy(RateLimitPolicies.Staff, context => FixedIpLimit(context, RateLimitPolicies.Staff, 120, TimeSpan.FromMinutes(1)));
	options.AddPolicy(RateLimitPolicies.Factory, context => FixedStaffLimit(context, RateLimitPolicies.Factory, 120, TimeSpan.FromHours(1)));
	options.AddPolicy(RateLimitPolicies.FactoryBundle, context => FixedIpLimit(context, RateLimitPolicies.FactoryBundle, 30, TimeSpan.FromHours(1)));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	using var scope = app.Services.CreateScope();
	var dbContext = scope.ServiceProvider.GetRequiredService<SaltMonitorDbContext>();
	await dbContext.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
	app.UseSwaggerUI(options =>
	{
		options.RoutePrefix = "swagger";
		options.SwaggerEndpoint("/openapi/v1.json", "WaterFlex Salt Monitor API v1");
		options.DocumentTitle = "WaterFlex Salt Monitor API";
		options.DisplayRequestDuration();
	});
}

if (!app.Environment.IsDevelopment())
{
	app.UseForwardedHeaders();
	app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
	if (context.Request.Method == "POST" && (context.Request.Path.Equals("/api/v1/factory/flash-authorizations/verify")
		|| context.Request.Path.Equals("/api/v1/factory/verifications")))
	{
		context.Request.EnableBuffering();
		using var buffer = new MemoryStream();
		await context.Request.Body.CopyToAsync(buffer);
		context.Items["FactorySignedBody"] = buffer.ToArray();
		context.Request.Body.Position = 0;
	}
	await next(context);
});

app.UseExceptionHandler(new ExceptionHandlerOptions
{
	StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
		? badRequest.StatusCode
		: StatusCodes.Status500InternalServerError
});
app.Use(async (context, next) =>
{
	var isStaffMutation = context.Request.Method is not ("GET" or "HEAD" or "OPTIONS")
		&& (context.Request.Path.StartsWithSegments("/api/v1/staff-admin")
			|| context.Request.Path.StartsWithSegments("/api/v1/staff/activate")
			|| (context.Request.Path.StartsWithSegments("/api/v1/factory")
				&& !context.Request.Path.Equals("/api/v1/factory/flash-authorizations/verify")
				&& !context.Request.Path.Equals("/api/v1/factory/verifications")
				&& !context.Request.Path.Equals("/api/v1/factory/stations/enroll")));
	if (isStaffMutation && context.Request.Headers["X-WaterFlex-Request"] != "console")
	{
		context.Response.StatusCode = StatusCodes.Status400BadRequest;
		await context.Response.WriteAsJsonAsync(new { errorCode = "staff_request_header_required" });
		return;
	}
	await next(context);
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.Use(async (context, next) =>
{
	var requestId = context.Request.Headers["X-Correlation-ID"].ToString();
	if (!Guid.TryParse(requestId, out _)) requestId = Guid.NewGuid().ToString("D");
	context.Response.Headers["X-Correlation-ID"] = requestId;
	var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WaterFlex.Request");
	var started = Stopwatch.GetTimestamp();
	using (logger.BeginScope(new Dictionary<string, object?>
	{
		["CorrelationId"] = requestId,
		["TraceId"] = Activity.Current?.TraceId.ToString(),
		["DeviceId"] = context.User.FindFirstValue("device_id"),
		["StaffSubject"] = context.User.FindFirstValue("staff_subject")
	}))
	{
		await next(context);
		logger.LogInformation(
			"HTTP {Method} {Path} completed {StatusCode} in {ElapsedMilliseconds:F1} ms",
			context.Request.Method,
			context.Request.Path.Value,
			context.Response.StatusCode,
			Stopwatch.GetElapsedTime(started).TotalMilliseconds);
	}
});
app.UseResponseCompression();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
	.WithName("GetHealth")
	.WithSummary("Check API health")
	.WithDescription("Returns a successful response when the API process is available.")
	.WithTags("System")
	.Produces(StatusCodes.Status200OK);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
	.WithName("GetLegacyHealth")
	.WithSummary("Check API process health")
	.WithTags("System")
	.Produces(StatusCodes.Status200OK);

app.MapGet("/health/ready", async (
		SaltMonitorDbContext database,
		IHostEnvironment environment,
		IOptions<CloudflareAccessOptions> accessOptions,
		CancellationToken cancellationToken) =>
	{
		try
		{
			if (!await database.Database.CanConnectAsync(cancellationToken))
			{
				return Results.Json(new { status = "not_ready", component = "database" }, statusCode: 503);
			}
			var pendingMigrations = await database.Database.GetPendingMigrationsAsync(cancellationToken);
			if (pendingMigrations.Any())
			{
				return Results.Json(new { status = "not_ready", component = "schema" }, statusCode: 503);
			}
			if (environment.IsStaging()
				&& (string.IsNullOrWhiteSpace(accessOptions.Value.Issuer)
					|| string.IsNullOrWhiteSpace(accessOptions.Value.Audience)))
			{
				return Results.Json(new { status = "not_ready", component = "staff_identity" }, statusCode: 503);
			}
			return Results.Ok(new { status = "ready" });
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Results.Json(new { status = "not_ready", component = "database" }, statusCode: 503);
		}
	})
	.WithName("GetReadiness")
	.WithSummary("Check required API dependencies")
	.WithDescription("Checks database and schema compatibility without exposing configuration or secrets.")
	.WithTags("System")
	.Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status503ServiceUnavailable);

if (app.Environment.IsDevelopment())
{
	app.MapGet("/api/v1/development/users", (IDevelopmentIdentityDirectory identityDirectory) =>
		Results.Ok(identityDirectory.GetUsers()))
		.WithName("GetDevelopmentUsers")
		.WithSummary("List seeded development identities")
		.WithTags("Development");

}

app.MapGet("/api/v1/staff/session", (HttpContext httpContext) =>
	{
		if (httpContext.User.HasClaim("staff_activation_candidate", "true"))
		{
			return Results.Ok(new StaffSessionSummary("activationRequired", null));
		}

		return Results.Ok(new StaffSessionSummary("active", httpContext.GetStaffActor()));
	})
	.RequireAuthorization(DevelopmentIdentity.AuthenticatedPolicy)
	.WithName("GetCurrentStaffSession")
	.WithSummary("Get the authenticated WaterFlex staff session state")
	.WithTags("Staff identity")
	.Produces<StaffSessionSummary>(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status401Unauthorized);

app.MapGet("/api/v1/staff/me", (HttpContext httpContext) =>
		Results.Ok(httpContext.GetStaffActor()))
	.RequireAuthorization(DevelopmentIdentity.AuthenticatedPolicy)
	.WithName("GetCurrentStaffIdentity")
	.WithSummary("Get the active WaterFlex staff identity")
	.WithTags("Staff identity")
	.Produces<StaffActor>(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status401Unauthorized);

app.MapPost("/api/v1/staff/activate", async (
		HttpContext context,
		IStaffAccessService service,
		CancellationToken cancellationToken) =>
	{
		if (!context.User.HasClaim("staff_activation_candidate", "true"))
		{
			return Results.Ok(context.GetStaffActor());
		}

		var issuer = context.User.FindFirstValue("staff_issuer");
		var subject = context.User.FindFirstValue("staff_subject");
		var email = context.User.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
		var invitationValue = context.User.FindFirstValue("staff_invitation_id");
		if (issuer is null || subject is null || email is null || !Guid.TryParse(invitationValue, out var invitationId))
		{
			return Results.Unauthorized();
		}
		var actor = await service.ActivateInvitationAsync(invitationId, issuer, subject, email, cancellationToken);
		return actor is null ? Results.Forbid() : Results.Ok(actor);
	})
	.RequireAuthorization(DevelopmentIdentity.AuthenticatedPolicy)
	.WithName("ActivateStaffInvitation")
	.WithSummary("Activate the current authenticated staff invitation")
	.WithTags("Staff identity");

app.MapStaffAccessEndpoints();
app.MapFactoryEndpoints();

var technicianApi = app.MapGroup("/api/v1/technician")
	.WithTags("Technician provisioning")
	.RequireRateLimiting(RateLimitPolicies.Staff)
	.RequireStaffCapability(StaffCapability.TechnicianOperations);
technicianApi.MapCommissioningSessionEndpoints();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
	technicianApi.MapGet("/customers", async (
			string? search,
			IWaterFlexCustomerDirectory customerDirectory,
			CancellationToken cancellationToken) =>
		Results.Ok(await customerDirectory.SearchAsync(search, cancellationToken)))
		.WithName("SearchWaterFlexCustomers")
		.WithSummary("Search WaterFlex customers")
		.WithDescription(
			"Development directory adapter for selecting a WaterFlex customer, service location, and tank during sensor provisioning.")
		.Produces<IReadOnlyList<WaterFlexCustomerOption>>(StatusCodes.Status200OK);

	technicianApi.MapPost("/commission", async (
			CommissionSensorRequest request,
			HttpContext httpContext,
			ISensorCommissioningService commissioningService,
			CancellationToken cancellationToken) =>
		{
			var result = await commissioningService.CommissionAsync(
				request,
				httpContext.GetStaffActor(),
				cancellationToken);
			if (result.IsSuccess)
			{
				return Results.Ok(result.Commissioning);
			}

			return result.Failure switch
			{
				CommissioningFailure.InvalidRequest => Results.ValidationProblem(
					result.ValidationErrors
						.GroupBy(error => char.ToLowerInvariant(error.Field[0]) + error.Field[1..])
						.ToDictionary(
							group => group.Key,
							group => group.Select(error => error.Message).ToArray())),
				CommissioningFailure.DirectorySelectionNotFound => Results.Problem(
					statusCode: StatusCodes.Status404NotFound,
					title: "WaterFlex selection not found"),
				CommissioningFailure.DeviceAlreadyRegistered => Results.Problem(
					statusCode: StatusCodes.Status409Conflict,
					title: "Sensor already registered"),
				CommissioningFailure.TankAlreadyOccupied => Results.Problem(
					statusCode: StatusCodes.Status409Conflict,
					title: "Tank already has an active sensor"),
				CommissioningFailure.InvalidTechnician => Results.Problem(
					statusCode: StatusCodes.Status403Forbidden,
					title: "Dealer technician identity required"),
				_ => Results.Problem(
					statusCode: StatusCodes.Status409Conflict,
					title: "Commissioning conflict")
			};
		})
		.WithName("CommissionSensor")
		.WithSummary("Commission a salt sensor")
		.WithDescription(
			"Atomically registers the ESP32, binds it to the selected WaterFlex tank, records calibration, and returns its device token exactly once. Development/Staging only until WaterFlex staff authentication is configured.")
		.Accepts<CommissionSensorRequest>("application/json")
		.Produces<CommissionSensorResponse>(StatusCodes.Status200OK)
		.ProducesValidationProblem(StatusCodes.Status400BadRequest)
		.ProducesProblem(StatusCodes.Status404NotFound)
		.ProducesProblem(StatusCodes.Status409Conflict);

	app.MapOpsEndpoints();
}

app.MapBootstrapActivationEndpoints();

var deviceApi = app.MapGroup("/api/v1/device")
	.RequireAuthorization(new AuthorizeAttribute
	{
		AuthenticationSchemes = DeviceTokenAuthenticationHandler.SchemeName
	});

deviceApi.MapPost("/health", async (
		ClaimsPrincipal principal,
		DeviceHealthHeartbeat heartbeat,
		IDeviceHealthService healthService,
		IDeviceCredentialUsageRecorder usageRecorder,
		CancellationToken cancellationToken) =>
	{
		var deviceId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
		var credentialRecordId = Guid.Parse(principal.FindFirstValue("device_credential_record_id")!);
		await usageRecorder.RecordAsync(credentialRecordId, cancellationToken);
		var result = await healthService.ReportAsync(deviceId, heartbeat, cancellationToken);

		if (result.IsSuccess)
		{
			return Results.Ok(result.Acknowledgement);
		}

		return result.Failure switch
		{
			DeviceHealthFailure.InvalidPayload => Results.ValidationProblem(
				result.ValidationErrors
					.GroupBy(error => char.ToLowerInvariant(error.Field[0]) + error.Field[1..])
					.ToDictionary(
						group => group.Key,
						group => group.Select(error => error.Message).ToArray())),
			DeviceHealthFailure.DeviceUnavailable => Results.Problem(
				statusCode: StatusCodes.Status403Forbidden,
				title: "Device unavailable"),
			_ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
		};
	})
	.RequireRateLimiting(RateLimitPolicies.Device)
	.WithName("ReportDeviceHealth")
	.WithSummary("Report device and sensor health")
	.WithDescription("Records health without creating a distance or changing the operational fill level.")
	.WithTags("Device telemetry")
	.Accepts<DeviceHealthHeartbeat>("application/json")
	.Produces<DeviceHealthAcknowledgement>(StatusCodes.Status200OK)
	.ProducesValidationProblem(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status401Unauthorized)
	.ProducesProblem(StatusCodes.Status403Forbidden)
	.Produces(StatusCodes.Status429TooManyRequests);

deviceApi.MapPost("/telemetry", async (
		ClaimsPrincipal principal,
		TelemetryBatch batch,
		ITelemetryIngestionService ingestionService,
		IDeviceCredentialUsageRecorder usageRecorder,
		CancellationToken cancellationToken) =>
	{
		var deviceId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
		var credentialRecordId = Guid.Parse(principal.FindFirstValue("device_credential_record_id")!);
		await usageRecorder.RecordAsync(credentialRecordId, cancellationToken);
		var result = await ingestionService.IngestAsync(deviceId, batch, cancellationToken);

		if (result.IsSuccess)
		{
			return Results.Ok(result.Acknowledgement);
		}

		return result.Failure switch
		{
			TelemetryIngestionFailure.InvalidPayload => Results.ValidationProblem(
				result.ValidationErrors
					.GroupBy(error => error.ReadingIndex is { } index
						? $"readings[{index}].{error.Field}"
						: error.Field)
					.ToDictionary(
						group => group.Key,
						group => group.Select(error => error.Message).ToArray())),
			TelemetryIngestionFailure.DeviceUnavailable => Results.Problem(
				statusCode: StatusCodes.Status403Forbidden,
				title: "Device unavailable"),
			TelemetryIngestionFailure.DeviceNotCommissioned => Results.Problem(
				statusCode: StatusCodes.Status409Conflict,
				title: "Device is not commissioned"),
			TelemetryIngestionFailure.CalibrationUnavailable => Results.Problem(
				statusCode: StatusCodes.Status409Conflict,
				title: "Device calibration is unavailable"),
			_ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
		};
	})
	.RequireRateLimiting(RateLimitPolicies.Device)
	.WithName("SubmitDeviceTelemetry")
	.WithSummary("Submit device telemetry")
	.WithDescription(
		"Accepts up to 50 readings from an authenticated device. Customer, location, tank, installation, and calibration are resolved server-side. Replayed reading keys are acknowledged as duplicates.")
	.WithTags("Device telemetry")
	.Accepts<TelemetryBatch>("application/json")
	.Produces<TelemetryBatchAcknowledgement>(StatusCodes.Status200OK)
	.ProducesValidationProblem(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status401Unauthorized)
	.ProducesProblem(StatusCodes.Status403Forbidden)
	.ProducesProblem(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status413PayloadTooLarge)
	.Produces(StatusCodes.Status429TooManyRequests)
	.ProducesProblem(StatusCodes.Status500InternalServerError)
	.AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
	{
		if (operation.RequestBody?.Content is not { } content
			|| !content.TryGetValue("application/json", out var mediaType))
		{
			return Task.CompletedTask;
		}

		mediaType.Example = new JsonObject
		{
			["schemaVersion"] = 1,
			["firmwareVersion"] = "1.0.0",
			["readings"] = new JsonArray
			{
				new JsonObject
				{
					["bootId"] = "11111111-1111-1111-1111-111111111111",
					["sequenceNumber"] = 1,
					["observedAtUtc"] = "2026-07-23T12:00:00Z",
					["uptimeMilliseconds"] = 60000,
					["rawDistanceMm"] = 1000,
					["quality"] = 95,
					["sampleCount"] = 8,
					["wifiRssiDbm"] = -60,
					["errorFlags"] = new JsonArray()
				}
			}
		};

		return Task.CompletedTask;
	});

app.Run();

public partial class Program;
