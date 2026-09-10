using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Api;

public static class WorkOrderEndpoints
{
    public static IEndpointRouteBuilder MapInstallationWorkOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/work-orders")
            .WithTags("Installation work orders")
            .RequireRateLimiting(RateLimitPolicies.Staff)
            .RequireStaffCapability(StaffCapability.WorkOrderManagement);

        group.MapPost("/", async (
                CreateInstallationWorkOrderRequest request,
                string? dealerExternalId,
                HttpContext context,
                IInstallationWorkOrderService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(request, context.GetStaffActor(), dealerExternalId, cancellationToken);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/work-orders/{result.WorkOrder!.Id}", result.WorkOrder)
                    : ToFailure(result);
            })
            .WithName("CreateInstallationWorkOrder")
            .Produces<InstallationWorkOrderManagementView>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/", async (
                string? dealerExternalId,
                HttpContext context,
                IInstallationWorkOrderService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ListAsync(context.GetStaffActor(), dealerExternalId, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.WorkOrders) : ToFailure(result.Failure, result.ValidationErrors);
            })
            .WithName("ListInstallationWorkOrders")
            .Produces<IReadOnlyList<InstallationWorkOrderManagementView>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelInstallationWorkOrderRequest request,
                string? dealerExternalId,
                HttpContext context,
                IInstallationWorkOrderService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CancelAsync(id, request, context.GetStaffActor(), dealerExternalId, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.WorkOrder) : ToFailure(result);
            })
            .WithName("CancelInstallationWorkOrder")
            .Produces<InstallationWorkOrderManagementView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static IResult ToFailure(InstallationWorkOrderResult result) =>
        ToFailure(result.Failure, result.ValidationErrors);

    private static IResult ToFailure(
        InstallationWorkOrderFailure failure,
        IReadOnlyList<ProvisioningValidationError> validationErrors) => failure switch
    {
        InstallationWorkOrderFailure.InvalidRequest => Results.ValidationProblem(
            validationErrors.GroupBy(error => error.Field)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray())),
        InstallationWorkOrderFailure.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound, title: "Installation work order not found"),
        InstallationWorkOrderFailure.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Installation work order cannot be cancelled",
            detail: "The order changed, is no longer open, or has an active commissioning session."),
        _ => Results.Forbid()
    };
}
