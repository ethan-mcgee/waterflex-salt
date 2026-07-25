using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class DevelopmentWaterFlexCustomerDirectory : IWaterFlexCustomerDirectory
{
    private static readonly IReadOnlyList<WaterFlexCustomerOption> Customers =
    [
        new(
            "WF-C-10482",
            "10482",
            "North Ridge Apartments",
            [
                new(
                    "WF-L-10482-01",
                    "Building A mechanical room",
                    "1820 Ridgeview Ave, Madison, WI 53704",
                    [
                        new("WF-A-10482-S1", "Primary softener", 600),
                        new("WF-A-10482-S2", "Laundry softener", 350)
                    ]),
                new(
                    "WF-L-10482-02",
                    "Building B utility room",
                    "1828 Ridgeview Ave, Madison, WI 53704",
                    [new("WF-A-10482-S3", "Building B softener", 600)])
            ]),
        new(
            "WF-C-22017",
            "22017",
            "Baker Family Residence",
            [
                new(
                    "WF-L-22017-01",
                    "Main residence",
                    "7416 Meadow Run, Verona, WI 53593",
                    [new("WF-A-22017-S1", "Basement softener", 300)])
            ]),
        new(
            "WF-C-31804",
            "31804",
            "Lakeside Dental Group",
            [
                new(
                    "WF-L-31804-01",
                    "Lakeside clinic",
                    "440 Harbor Point Dr, Middleton, WI 53562",
                    [
                        new("WF-A-31804-S1", "Clinical supply softener", 450),
                        new("WF-A-31804-S2", "Boiler softener", 450)
                    ])
            ])
    ];

    public Task<IReadOnlyList<WaterFlexCustomerOption>> SearchAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var term = search?.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return Task.FromResult(Customers);
        }

        var matches = Customers
            .Where(customer =>
                Contains(customer.DisplayName, term)
                || Contains(customer.AccountNumber, term)
                || customer.Locations.Any(location =>
                    Contains(location.DisplayName, term)
                    || Contains(location.AddressSummary, term)))
            .ToArray();

        return Task.FromResult<IReadOnlyList<WaterFlexCustomerOption>>(matches);
    }

    public Task<WaterFlexCommissioningSelection?> ResolveAsync(
        string waterFlexCustomerId,
        string waterFlexLocationId,
        string waterFlexAssetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var customer = Customers.SingleOrDefault(candidate =>
            candidate.WaterFlexCustomerId.Equals(waterFlexCustomerId, StringComparison.Ordinal));
        var location = customer?.Locations.SingleOrDefault(candidate =>
            candidate.WaterFlexLocationId.Equals(waterFlexLocationId, StringComparison.Ordinal));
        var tank = location?.Tanks.SingleOrDefault(candidate =>
            candidate.WaterFlexAssetId.Equals(waterFlexAssetId, StringComparison.Ordinal));

        if (customer is null || location is null || tank is null)
        {
            return Task.FromResult<WaterFlexCommissioningSelection?>(null);
        }

        return Task.FromResult<WaterFlexCommissioningSelection?>(new(
            customer.WaterFlexCustomerId,
            customer.AccountNumber,
            customer.DisplayName,
            location.WaterFlexLocationId,
            location.DisplayName,
            location.AddressSummary,
            tank.WaterFlexAssetId,
            tank.Label,
            tank.CapacityPounds));
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}