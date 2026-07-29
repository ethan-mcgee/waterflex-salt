using WaterFlex.SaltMonitor.Provisioning;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class DevelopmentInstallationWorkOrderDirectory : IInstallationWorkOrderDirectory
{
    private static readonly IReadOnlyList<InstallationWorkOrder> WorkOrders =
    [
        new(
            "WO-82417",
            "WF-D-NORTH-STAR",
            "WF-C-10482",
            "WF-L-10482-01",
            "WF-A-10482-S1",
            "North Ridge Apartments",
            "Building A mechanical room",
            "1820 Ridgeview Ave, Madison, WI 53704",
            "Primary softener"),
        new(
            "WO-82418",
            "WF-D-NORTH-STAR",
            "WF-C-22017",
            "WF-L-22017-01",
            "WF-A-22017-S1",
            "Baker Family Residence",
            "Main residence",
            "7416 Meadow Run, Verona, WI 53593",
            null),
        new(
            "WO-93104",
            "WF-D-LAKES-WATER",
            "WF-C-31804",
            "WF-L-31804-01",
            "WF-A-31804-S1",
            "Lakeside Dental Group",
            "Lakeside clinic",
            "440 Harbor Point Dr, Middleton, WI 53562",
            "Clinical supply softener")
    ];

    public Task<InstallationWorkOrder?> FindEligibleAsync(
        string workOrderNumber,
        string dealerExternalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedNumber = workOrderNumber.Trim().ToUpperInvariant();
        var workOrder = WorkOrders.SingleOrDefault(candidate =>
            candidate.WorkOrderNumber == normalizedNumber
            && candidate.DealerExternalId == dealerExternalId);
        return Task.FromResult(workOrder);
    }
}