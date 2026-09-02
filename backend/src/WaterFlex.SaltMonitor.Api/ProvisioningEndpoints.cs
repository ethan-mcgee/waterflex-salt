using WaterFlex.SaltMonitor.Provisioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Domain.Security;

namespace WaterFlex.SaltMonitor.Api;

/// <summary>
/// Endpoints for the sensor provisioning lifecycle: factory registration of new devices, bootstrap
/// activation by the device itself, and technician-driven commissioning sessions.
/// </summary>
public static class ProvisioningEndpoints
{
    /// <summary>Maps staff-authenticated factory-floor endpoints for registering, resuming, and verifying new devices.</summary>
    public static IEndpointRouteBuilder MapFactoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var factoryApi = endpoints.MapGroup("/api/v1/factory")
            .WithTags("Factory provisioning")
            .RequireRateLimiting(RateLimitPolicies.Factory)
            .RequireStaffCapability(StaffCapability.FactoryProvisioning);

        factoryApi.MapGet("/configuration", (IOptions<FactoryProvisioningOptions> configured) =>
            {
                var options = configured.Value;
                return Results.Ok(new FactoryProvisioningConfiguration(
                    options.Enabled,
                    options.Model,
                    options.ApprovedFirmwareVersion,
                    options.ConfigurationVersion,
                    options.HelperBaseUrl,
                    options.HelperProtocolVersion));
            })
            .WithName("GetFactoryProvisioningConfiguration")
            .WithSummary("Get the approved factory firmware and local-helper configuration")
            .Produces<FactoryProvisioningConfiguration>(StatusCodes.Status200OK);

        factoryApi.MapPost("/devices", async (
                RegisterFactoryDeviceRequest request,
                HttpContext httpContext,
                IOptions<FactoryProvisioningOptions> configured,
                IFactoryDeviceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                var options = configured.Value;
                if (!options.Enabled)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Factory provisioning is disabled");
                }
                if (!string.Equals(request.Model.Trim(), options.Model, StringComparison.Ordinal)
                    || !string.Equals(request.FirmwareVersion.Trim(), options.ApprovedFirmwareVersion, StringComparison.Ordinal)
                    || !string.Equals(request.ConfigurationVersion.Trim(), options.ConfigurationVersion, StringComparison.Ordinal))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["factoryConfiguration"] = ["The requested model, firmware, or configuration version is not approved."]
                    });
                }
                var result = await registrationService.RegisterAsync(
                    request,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                if (result.IsSuccess)
                {
                    return Results.Created(
                        $"/api/v1/factory/devices/{result.Registration!.DeviceId:D}",
                        result.Registration);
                }

                return result.Failure switch
                {
                    FactoryRegistrationFailure.InvalidRequest => Results.ValidationProblem(
                        ToValidationDictionary(result.ValidationErrors)),
                    FactoryRegistrationFailure.DeviceAlreadyRegistered => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Device already registered"),
                    FactoryRegistrationFailure.BootstrapCredentialAlreadyRegistered => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Bootstrap credential already registered"),
                    _ => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Factory registration conflict")
                };
            })
            .WithName("RegisterFactoryDevice")
            .WithSummary("Register a factory-provisioned sensor")
            .WithDescription(
                "Registers inventory and a SHA-256 bootstrap hash. Plaintext bootstrap secrets are never accepted or returned.")
            .Accepts<RegisterFactoryDeviceRequest>("application/json")
            .Produces<FactoryDeviceRegistration>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        factoryApi.MapGet("/devices/active", async (
                HttpContext httpContext,
                IFactoryDeviceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                var result = await registrationService.FindActiveByOperatorAsync(
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result.IsSuccess
                    ? Results.Ok(result.Registration)
                    : Results.NotFound();
            })
            .WithName("GetActiveFactoryDevice")
            .WithSummary("Resume the caller's own in-progress factory provisioning job, if any")
            .WithDescription(
                "Lets the console recover an operator's own Registered or Quarantined job when local browser " +
                "state is lost, so a new job is never started (and a new serial never minted) for a unit that " +
                "already has one in progress.")
            .Produces<FactoryDeviceRegistration>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        factoryApi.MapGet("/devices/by-idempotency/{idempotencyKey}", async (
                string idempotencyKey,
                HttpContext httpContext,
                IFactoryDeviceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                var result = await registrationService.FindByIdempotencyKeyAsync(
                    idempotencyKey,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result.IsSuccess
                    ? Results.Ok(result.Registration)
                    : Results.NotFound();
            })
            .WithName("GetFactoryDeviceByIdempotencyKey")
            .WithSummary("Resume an idempotent factory provisioning job")
            .Produces<FactoryDeviceRegistration>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        factoryApi.MapPost("/devices/{deviceId:guid}/verification", async (
                Guid deviceId,
                FactoryVerificationRequest request,
                HttpContext httpContext,
                IFactoryDeviceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await registrationService.RecordVerificationAsync(
                        deviceId,
                        request,
                        httpContext.GetStaffActor(),
                        cancellationToken));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { errorCode = "factory_job_terminal", detail = exception.Message });
                }
            })
            .WithName("RecordFactoryDeviceVerification")
            .WithSummary("Record redacted factory acceptance evidence")
            .Produces<FactoryVerificationResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        factoryApi.MapPost("/devices/{deviceId:guid}/retry", async (
                Guid deviceId,
                HttpContext httpContext,
                IFactoryDeviceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await registrationService.RetryAsync(
                        deviceId,
                        httpContext.GetStaffActor(),
                        cancellationToken));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { errorCode = "factory_job_terminal", detail = exception.Message });
                }
            })
            .WithName("RetryFactoryDeviceProvisioning")
            .WithSummary("Reset a quarantined factory job for another acceptance attempt")
            .Produces<FactoryDeviceRegistration>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Unauthenticated-by-staff: the local factory workstation helper that actually flashes the
        // device has no staff session of its own. Possessing a valid, unexpired, single-use token
        // minted by a staff-authenticated register/retry call above is the entire authorization
        // check, mirroring how /api/v1/device/activate is safely unauthenticated-by-staff today.
        endpoints.MapPost("/api/v1/factory/flash-authorizations/verify", async (
                FlashAuthorizationVerificationRequest request,
                IFactoryFlashAuthorizationService flashAuthorizationService,
                CancellationToken cancellationToken) =>
            {
                var authorized = !string.IsNullOrWhiteSpace(request.Token)
                    && await flashAuthorizationService.VerifyAsync(
                        request.DeviceId,
                        request.Token,
                        cancellationToken);
                return authorized
                    ? Results.Ok(new { authorized = true })
                    : Results.Json(new { errorCode = "flash_authorization_invalid" }, statusCode: StatusCodes.Status403Forbidden);
            })
            .WithName("VerifyFactoryFlashAuthorization")
            .WithSummary("Redeem a factory flash-authorization token before the local helper flashes a device")
            .RequireRateLimiting(RateLimitPolicies.Activation)
            .Accepts<FlashAuthorizationVerificationRequest>("application/json")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    /// <summary>Maps the unauthenticated-by-staff endpoint a device itself calls to exchange its bootstrap credential for an operational one once commissioning is pending.</summary>
    public static IEndpointRouteBuilder MapBootstrapActivationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/device/activate", async (
                HttpContext httpContext,
                ActivateDeviceRequest request,
                IDeviceBootstrapActivationService activationService,
                CancellationToken cancellationToken) =>
            {
                var authorization = httpContext.Request.Headers.Authorization.ToString();
                if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { errorCode = "invalid_bootstrap_token" },
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var bootstrapToken = authorization["Bearer ".Length..].Trim();
                var result = await activationService.ActivateAsync(
                    bootstrapToken,
                    request,
                    cancellationToken);
                if (result.IsSuccess)
                {
                    return Results.Ok(result.Activation);
                }

                return result.Failure switch
                {
                    ActivationFailure.InvalidRequest => Results.ValidationProblem(
                        ToValidationDictionary(result.ValidationErrors)),
                    ActivationFailure.InvalidBootstrapToken => Results.Json(
                        new { errorCode = "invalid_bootstrap_token" },
                        statusCode: StatusCodes.Status401Unauthorized),
                    ActivationFailure.BootstrapUnavailable => Results.Json(
                        new { errorCode = "bootstrap_unavailable" },
                        statusCode: StatusCodes.Status403Forbidden),
                    ActivationFailure.NoPendingCommissioning => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "No pending commissioning session"),
                    ActivationFailure.ActivationAttemptMismatch => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Activation attempt mismatch"),
                    ActivationFailure.ActivationConflict => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Activation conflict"),
                    _ => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Activation conflict")
                };
            })
            .WithName("ActivateDevice")
            .WithSummary("Activate a commissioned bootstrap sensor")
            .WithDescription(
                "Exchanges a bootstrap credential for an operational credential hash, then creates installation and calibration for telemetry.")
            .RequireRateLimiting(RateLimitPolicies.Activation)
            .Accepts<ActivateDeviceRequest>("application/json")
            .Produces<ActivateDeviceResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps technician-facing endpoints for finding an eligible work order and creating, checking, or cancelling the commissioning session that reserves a sensor and tank for activation.</summary>
    public static RouteGroupBuilder MapCommissioningSessionEndpoints(
        this RouteGroupBuilder technicianApi)
    {
        technicianApi.MapGet("/installation-work-orders/{workOrderNumber}", async (
                string workOrderNumber,
                HttpContext httpContext,
                ICommissioningSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var result = await sessionService.FindWorkOrderAsync(
                    workOrderNumber,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result is null
                    ? Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Eligible salt-sensor work order not found")
                    : Results.Ok(result);
            })
            .WithName("GetInstallationWorkOrder")
            .WithSummary("Verify an eligible salt-sensor installation work order")
            .Produces<InstallationWorkOrderView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        technicianApi.MapPost("/work-order-commissioning-sessions", async (
                CreateWorkOrderCommissioningSessionRequest request,
                HttpContext httpContext,
                ICommissioningSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var result = await sessionService.CreateFromWorkOrderAsync(
                    request,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result.IsSuccess
                    ? Results.Created(
                        $"/api/v1/technician/commissioning-sessions/{result.Session!.SessionId:D}",
                        result.Session)
                    : ToSessionFailure(result);
            })
            .WithName("CreateWorkOrderCommissioningSession")
            .WithSummary("Assign a factory sensor from an installation work order")
            .Accepts<CreateWorkOrderCommissioningSessionRequest>("application/json")
            .Produces<CommissioningSessionView>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        technicianApi.MapPost("/commissioning-sessions", async (
                CreateCommissioningSessionRequest request,
                HttpContext httpContext,
                ICommissioningSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var result = await sessionService.CreateAsync(
                    request,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result.IsSuccess
                    ? Results.Created(
                        $"/api/v1/technician/commissioning-sessions/{result.Session!.SessionId:D}",
                        result.Session)
                    : ToSessionFailure(result);
            })
            .WithName("CreateCommissioningSession")
            .WithSummary("Reserve a sensor and tank for activation")
            .Accepts<CreateCommissioningSessionRequest>("application/json")
            .Produces<CommissioningSessionView>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        technicianApi.MapGet("/commissioning-sessions/{sessionId:guid}", async (
                Guid sessionId,
                HttpContext httpContext,
                ICommissioningSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var result = await sessionService.GetAsync(
                    sessionId,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Session) : ToSessionFailure(result);
            })
            .WithName("GetCommissioningSession")
            .WithSummary("Get dealer-scoped commissioning status")
            .Produces<CommissioningSessionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        technicianApi.MapPost("/commissioning-sessions/{sessionId:guid}/cancel", async (
                Guid sessionId,
                HttpContext httpContext,
                ICommissioningSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var result = await sessionService.CancelAsync(
                    sessionId,
                    httpContext.GetStaffActor(),
                    cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Session) : ToSessionFailure(result);
            })
            .WithName("CancelCommissioningSession")
            .WithSummary("Cancel a pending commissioning session")
            .Produces<CommissioningSessionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return technicianApi;
    }

    /// <summary>Maps a commissioning session failure reason to the corresponding HTTP problem response.</summary>
    private static IResult ToSessionFailure(CommissioningSessionResult result) =>
        result.Failure switch
        {
            CommissioningSessionFailure.InvalidRequest => Results.ValidationProblem(
                ToValidationDictionary(result.ValidationErrors)),
            CommissioningSessionFailure.DirectorySelectionNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "WaterFlex selection not found"),
            CommissioningSessionFailure.WorkOrderNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Eligible salt-sensor work order not found"),
            CommissioningSessionFailure.TankLocationRequired => Results.ValidationProblem(
                ToValidationDictionary(result.ValidationErrors)),
            CommissioningSessionFailure.DeviceNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Factory sensor not found"),
            CommissioningSessionFailure.SessionNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Commissioning session not found"),
            CommissioningSessionFailure.InvalidTechnician => Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Dealer technician identity required"),
            CommissioningSessionFailure.DeviceUnavailable => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Sensor is not available for commissioning"),
            CommissioningSessionFailure.DeviceAlreadyReserved => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Sensor or tank already has a pending commissioning session"),
            CommissioningSessionFailure.TankUnavailable => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Tank already has an active sensor"),
            CommissioningSessionFailure.SessionUnavailable => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Commissioning session is no longer pending"),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Commissioning session conflict")
        };

    /// <summary>Groups field-level provisioning errors into the shape ASP.NET Core's validation problem response expects, camel-casing field names to match the JSON request body.</summary>
    private static Dictionary<string, string[]> ToValidationDictionary(
        IReadOnlyList<ProvisioningValidationError> errors) =>
        errors
            .GroupBy(error => char.ToLowerInvariant(error.Field[0]) + error.Field[1..])
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Message).ToArray());
}
