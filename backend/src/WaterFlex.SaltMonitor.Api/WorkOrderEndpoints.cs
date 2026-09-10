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
            .RequireStaffRole(StaffRole.DealerAdministrator);

        group.MapPost("/", async (
                CreateInstallationWorkOrderRequest request,
                HttpContext context,
                IInstallationWorkOrderService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(request, context.GetStaffActor(), cancellationToken);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/work-orders/{result.WorkOrder!.Id}", result.WorkOrder)
                    : ToFailure(result);
            })
            .WithName("CreateInstallationWorkOrder")
            .Produces<InstallationWorkOrderManagementView>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/", async (
                HttpContext context,
                IInstallationWorkOrderService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(context.GetStaffActor(), cancellationToken)))
            .WithName("ListInstallationWorkOrders")
            .Produces<IReadOnlyList<InstallationWorkOrderManagementView>>();

        group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelInstallationWorkOrderRequest request,
                HttpContext context,
                IInstallationWorkOrderService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CancelAsync(id, request, context.GetStaffActor(), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.WorkOrder) : ToFailure(result);
            })
            .WithName("CancelInstallationWorkOrder")
            .Produces<InstallationWorkOrderManagementView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static IResult ToFailure(InstallationWorkOrderResult result) => result.Failure switch
    {
        InstallationWorkOrderFailure.InvalidRequest => Results.ValidationProblem(
            result.ValidationErrors.GroupBy(error => error.Field)
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
