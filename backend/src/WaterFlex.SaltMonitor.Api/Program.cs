using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WaterFlex.SaltMonitor.Api;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Ingestion;
using WaterFlex.SaltMonitor.Operations;
using WaterFlex.SaltMonitor.Provisioning;

var builder = WebApplication.CreateBuilder(args);

const int maximumTelemetryBodyBytes = 64 * 1024;
const string deviceTelemetryRateLimit = "device-telemetry";

builder.WebHost.ConfigureKestrel(options =>
	options.Limits.MaxRequestBodySize = maximumTelemetryBodyBytes);
builder.Services.AddProblemDetails();
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
	.AddAuthentication(DeviceTokenAuthenticationHandler.SchemeName)
	.AddScheme<AuthenticationSchemeOptions, DeviceTokenAuthenticationHandler>(
		DeviceTokenAuthenticationHandler.SchemeName,
		_ => { });
builder.Services.AddAuthorization();
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
	options.AddPolicy(deviceTelemetryRateLimit, context =>
	{
		var partitionKey = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
			?? context.Connection.RemoteIpAddress?.ToString()
			?? "unknown";

		return RateLimitPartition.GetFixedWindowLimiter(
			partitionKey,
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = 10,
				Window = TimeSpan.FromMinutes(1),
				QueueLimit = 0,
				AutoReplenishment = true
			});
	});
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
	app.UseHttpsRedirection();
}

app.UseExceptionHandler(new ExceptionHandlerOptions
{
	StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
		? badRequest.StatusCode
		: StatusCodes.Status500InternalServerError
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
	.WithName("GetHealth")
	.WithSummary("Check API health")
	.WithDescription("Returns a successful response when the API process is available.")
	.WithTags("System")
	.Produces(StatusCodes.Status200OK);

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
	app.MapGet("/api/v1/development/users", (IDevelopmentIdentityDirectory identityDirectory) =>
		Results.Ok(identityDirectory.GetUsers()))
		.WithName("GetDevelopmentUsers")
		.WithSummary("List seeded development identities")
		.WithTags("Development");

	var technicianApi = app.MapGroup("/api/v1/technician")
		.WithTags("Technician provisioning")
		.RequireDevelopmentRole(StaffRole.DealerTechnician);
	technicianApi.MapCommissioningSessionEndpoints();

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
				httpContext.GetDevelopmentActor(),
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
	app.MapDevelopmentFactoryEndpoints();
}

app.MapBootstrapActivationEndpoints();

var deviceApi = app.MapGroup("/api/v1/device")
	.RequireAuthorization();

deviceApi.MapPost("/telemetry", async (
		ClaimsPrincipal principal,
		TelemetryBatch batch,
		ITelemetryIngestionService ingestionService,
		CancellationToken cancellationToken) =>
	{
		var deviceId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
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
	.RequireRateLimiting(deviceTelemetryRateLimit)
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

// TODO(plan-c): secure technician and operations endpoints with staff/dealer identity outside Development.

app.Run();

public partial class Program;
