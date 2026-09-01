using System.Security.Cryptography;
using System.Text;

namespace WaterFlex.SaltMonitor.Api;

/// <summary>
/// Stand-in factory authentication for local development: a shared key configured out-of-band, checked
/// in constant time, instead of a real factory-floor identity system.
/// </summary>
public static class DevelopmentFactoryIdentity
{
    public const string KeyHeaderName = "X-WaterFlex-Factory-Key";
    public const string OperatorHeaderName = "X-WaterFlex-Factory-Operator";
    private const string OperatorItemKey = "WaterFlex.FactoryOperator";

    /// <summary>Adds an endpoint filter requiring the configured factory key and an operator identifier on every request in the group.</summary>
    public static RouteGroupBuilder RequireDevelopmentFactoryIdentity(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var expectedKey = httpContext.RequestServices
                .GetRequiredService<IConfiguration>()["FactoryProvisioning:DevelopmentKey"];
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Factory provisioning is not configured");
            }

            var presentedKey = httpContext.Request.Headers[KeyHeaderName].ToString();
            var operatorId = httpContext.Request.Headers[OperatorHeaderName].ToString().Trim();
            if (!SecureEquals(presentedKey, expectedKey) || operatorId is not { Length: > 0 and <= 200 })
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Factory identity required");
            }

            httpContext.Items[OperatorItemKey] = operatorId;
            return await next(context);
        });
        return group;
    }

    public static string GetDevelopmentFactoryOperator(this HttpContext context) =>
        context.Items.TryGetValue(OperatorItemKey, out var value) && value is string operatorId
            ? operatorId
            : throw new InvalidOperationException("Factory operator was not resolved for this endpoint.");

    /// <summary>Compares the two values by their SHA-256 hashes in fixed time, so key length and content don't leak through timing.</summary>
    private static bool SecureEquals(string presented, string expected)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }
}