using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using WaterFlex.SaltMonitor.Ingestion;

namespace WaterFlex.SaltMonitor.Api;

public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDeviceTokenValidator tokenValidator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceToken";
    private const string FailureItemKey = "DeviceTokenFailure";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        var result = await tokenValidator.ValidateAsync(token, Context.RequestAborted);
        if (!result.IsValid)
        {
            Context.Items[FailureItemKey] = result.Failure;
            return AuthenticateResult.Fail("The device token is invalid.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.DeviceId!.Value.ToString("D")),
            new Claim("device_id", result.DeviceId.Value.ToString("D")),
            new Claim("device_credential_record_id", result.CredentialRecordId!.Value.ToString("D"))
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var failure = Context.Items.TryGetValue(FailureItemKey, out var value)
            ? value as DeviceTokenFailure?
            : null;
        var forbidden = failure is DeviceTokenFailure.Revoked or DeviceTokenFailure.DeviceUnavailable;

        Response.StatusCode = forbidden
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status401Unauthorized;

        return Response.WriteAsJsonAsync(new
        {
            errorCode = forbidden ? "device_unavailable" : "invalid_device_token"
        });
    }
}
