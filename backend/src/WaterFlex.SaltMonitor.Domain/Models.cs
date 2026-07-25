namespace WaterFlex.SaltMonitor.Domain.Model;

/// <summary>A normalized sensor reading attributed to a registered device by the server.</summary>
public sealed record SensorReading(
    string TenantId,
    string DeviceId,
    DateTimeOffset SourceTimestamp,
    int RawDistanceMm,
    int Quality,
    bool Online);

/// <summary>Usable tank depth measured vertically from the sensor face to the tank bottom.</summary>
public sealed record TankCalibration(int TankDepthMm);

/// <summary>Request to open a WaterFlex/RouteFlex delivery ticket.</summary>
public sealed record DeliveryTicketRequest(
    string TenantId,
    string WaterFlexCustomerRef,
    string DeviceId,
    double FillPercent,
    double ThresholdPercent,
    DateTimeOffset ReadingTimestamp,
    string IdempotencyKey);

/// <summary>Result of a delivery-ticket creation attempt.</summary>
public sealed record DeliveryTicketResult(string ExternalTicketId, string Status, DateTimeOffset CreatedAt);
