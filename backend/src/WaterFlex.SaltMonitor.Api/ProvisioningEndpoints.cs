using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Api;

public static class ProvisioningEndpoints
{
    public static IEndpointRouteBuilder MapDevelopmentFactoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var factoryApi = endpoints.MapGroup("/api/v1/factory")
            .WithTags("Factory provisioning")
            .RequireDevelopmentFactoryIdentity();

        factoryApi.MapPost("/devices", async (
                RegisterFactoryDeviceRequest request,
                HttpContext httpContext,
                IFactoryDeviceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                var result = await registrationService.RegisterAsync(
                    request,
                    httpContext.GetDevelopmentFactoryOperator(),
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

        return endpoints;
    }

    public static RouteGroupBuilder MapCommissioningSessionEndpoints(
        this RouteGroupBuilder technicianApi)
    {
        technicianApi.MapPost("/commissioning-sessions", async (
                CreateCommissioningSessionRequest request,
                HttpContext httpContext,
                ICommissioningSessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                var result = await sessionService.CreateAsync(
                    request,
                    httpContext.GetDevelopmentActor(),
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
                    httpContext.GetDevelopmentActor(),
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
                    httpContext.GetDevelopmentActor(),
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

    private static IResult ToSessionFailure(CommissioningSessionResult result) =>
        result.Failure switch
        {
            CommissioningSessionFailure.InvalidRequest => Results.ValidationProblem(
                ToValidationDictionary(result.ValidationErrors)),
            CommissioningSessionFailure.DirectorySelectionNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "WaterFlex selection not found"),
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

    private static Dictionary<string, string[]> ToValidationDictionary(
        IReadOnlyList<ProvisioningValidationError> errors) =>
        errors
            .GroupBy(error => char.ToLowerInvariant(error.Field[0]) + error.Field[1..])
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Message).ToArray());
}