using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Api;

public static class StaffAccessEndpoints
{
    public static IEndpointRouteBuilder MapStaffAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/staff-admin")
            .WithTags("Staff administration")
            .RequireRateLimiting(RateLimitPolicies.Staff)
            .RequireStaffCapability(StaffCapability.StaffAdministration);

        group.MapGet("/staff", async (HttpContext context, IStaffAccessService service, CancellationToken ct) =>
            Results.Ok(await service.ListStaffAsync(context.GetStaffActor(), ct)))
            .WithName("ListStaffMembers");

        group.MapGet("/invitations", async (HttpContext context, IStaffAccessService service, CancellationToken ct) =>
            Results.Ok(await service.ListInvitationsAsync(context.GetStaffActor(), ct)))
            .WithName("ListStaffInvitations");

        group.MapPost("/invitations", async (CreateStaffInvitationRequest request, HttpContext context, IStaffAccessService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Results.Created($"/api/v1/staff-admin/invitations", await service.CreateInvitationAsync(request, context.GetStaffActor(), ct))))
            .WithName("CreateStaffInvitation");

        group.MapPut("/staff/{staffId:guid}/role", async (Guid staffId, ChangeStaffRoleRequest request, HttpContext context, IStaffAccessService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Results.Ok(await service.ChangeRoleAsync(staffId, request, context.GetStaffActor(), ct))))
            .WithName("ChangeStaffRole");

        group.MapPost("/staff/{staffId:guid}/suspend", async (Guid staffId, ChangeStaffStateRequest request, HttpContext context, IStaffAccessService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Results.Ok(await service.SuspendAsync(staffId, request, context.GetStaffActor(), ct))))
            .WithName("SuspendStaffIdentity");

        group.MapPost("/staff/{staffId:guid}/reactivate", async (Guid staffId, ChangeStaffStateRequest request, HttpContext context, IStaffAccessService service, CancellationToken ct) =>
            await ExecuteAsync(async () => Results.Ok(await service.ReactivateAsync(staffId, request, context.GetStaffActor(), ct))))
            .WithName("ReactivateStaffIdentity");

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (StaffAccessValidationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["staff"] = [exception.Message] }); }
        catch (StaffAccessForbiddenException exception) { return Results.Json(new { errorCode = "staff_scope_forbidden", detail = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
        catch (StaffAccessConflictException exception) { return Results.Conflict(new { errorCode = "staff_access_conflict", detail = exception.Message }); }
    }
}
