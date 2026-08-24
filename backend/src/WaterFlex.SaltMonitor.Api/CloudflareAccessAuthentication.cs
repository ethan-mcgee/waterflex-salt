using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WaterFlex.SaltMonitor.Domain.Security;
using WaterFlex.SaltMonitor.Infrastructure.Persistence;
using WaterFlex.SaltMonitor.Operations;

namespace WaterFlex.SaltMonitor.Api;

public sealed class CloudflareAccessOptions
{
    public const string SectionName = "CloudflareAccess";
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

public interface ICloudflareAccessTokenValidator
{
    Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken);
}

public sealed class CloudflareAccessTokenValidator(
    IHttpClientFactory httpClientFactory,
    IOptions<CloudflareAccessOptions> options,
    TimeProvider timeProvider) : ICloudflareAccessTokenValidator
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyCollection<SecurityKey> signingKeys = [];
    private DateTimeOffset refreshAfterUtc;

    public async Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!TryNormalizeIssuer(configured.Issuer, out var issuer)
            || string.IsNullOrWhiteSpace(configured.Audience)
            || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var keys = await GetSigningKeysAsync(issuer, false, cancellationToken);
        var principal = await ValidateWithKeysAsync(token, issuer, configured.Audience, keys);
        if (principal is not null)
        {
            return principal;
        }

        keys = await GetSigningKeysAsync(issuer, true, cancellationToken);
        return await ValidateWithKeysAsync(token, issuer, configured.Audience, keys);
    }

    private async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        string issuer,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && signingKeys.Count > 0 && timeProvider.GetUtcNow() < refreshAfterUtc)
        {
            return signingKeys;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && signingKeys.Count > 0 && timeProvider.GetUtcNow() < refreshAfterUtc)
            {
                return signingKeys;
            }

            var client = httpClientFactory.CreateClient(nameof(CloudflareAccessTokenValidator));
            var jwksUri = new Uri(new Uri(issuer), "/cdn-cgi/access/certs");
            var json = await client.GetStringAsync(jwksUri, cancellationToken);
            var refreshedKeys = new JsonWebKeySet(json).GetSigningKeys().ToArray();
            if (refreshedKeys.Length == 0)
            {
                throw new SecurityTokenInvalidSigningKeyException("Cloudflare returned no signing keys.");
            }

            signingKeys = refreshedKeys;
            refreshAfterUtc = timeProvider.GetUtcNow().AddHours(6);
            return signingKeys;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static async Task<ClaimsPrincipal?> ValidateWithKeysAsync(
        string token,
        string issuer,
        string audience,
        IReadOnlyCollection<SecurityKey> keys)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name"
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        return result.IsValid && result.ClaimsIdentity is not null
            ? new ClaimsPrincipal(result.ClaimsIdentity)
            : null;
    }

    private static bool TryNormalizeIssuer(string value, out string issuer)
    {
        issuer = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.EndsWith(".cloudflareaccess.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        issuer = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}

public sealed class StaffAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHostEnvironment environment,
    IDevelopmentIdentityDirectory developmentDirectory,
    ICloudflareAccessTokenValidator accessTokenValidator,
    SaltMonitorDbContext dbContext)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "WaterFlexStaff";
    public const string AccessAssertionHeader = "Cf-Access-Jwt-Assertion";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsDevelopment())
        {
            var developmentUser = Request.Headers[DevelopmentIdentity.HeaderName].ToString();
            var developmentActor = string.IsNullOrWhiteSpace(developmentUser)
                ? null
                : developmentDirectory.Resolve(developmentUser);
            return developmentActor is null
                ? AuthenticateResult.NoResult()
                : Success(developmentActor, "development", developmentActor.UserId, null);
        }

        var assertion = Request.Headers[AccessAssertionHeader].ToString();
        if (string.IsNullOrWhiteSpace(assertion))
        {
            return AuthenticateResult.NoResult();
        }

        var accessPrincipal = await accessTokenValidator.ValidateAsync(assertion, Context.RequestAborted);
        var issuer = accessPrincipal?.FindFirstValue("iss");
        var subject = accessPrincipal?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return AuthenticateResult.Fail("Cloudflare Access assertion is invalid.");
        }

        var record = await dbContext.StaffIdentities
            .AsNoTracking()
            .Include(identity => identity.Dealer)
            .SingleOrDefaultAsync(
                identity => identity.Issuer == issuer
                    && identity.Subject == subject
                    && identity.IsActive,
                Context.RequestAborted);
        if (record is null)
        {
            var email = accessPrincipal?.FindFirstValue(ClaimTypes.Email) ?? accessPrincipal?.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(email)) return AuthenticateResult.Fail("Cloudflare Access identity is not authorized.");
            var normalizedEmail = email.Trim().ToUpperInvariant();
            var invitation = await dbContext.StaffInvitations.AsNoTracking().SingleOrDefaultAsync(
                item => item.NormalizedEmail == normalizedEmail
                    && item.Status == StaffInvitationStatus.Ready
                    && item.ExpiresAtUtc > DateTimeOffset.UtcNow,
                Context.RequestAborted);
            return invitation is null
                ? AuthenticateResult.Fail("Cloudflare Access identity is not authorized.")
                : ActivationCandidate(issuer, subject, email, invitation.Id);
        }

        var actor = new StaffActor(
            record.Id.ToString("D"),
            record.DisplayName,
            record.Role,
            record.Dealer?.ExternalId,
            record.Dealer?.DisplayName);
        return Success(actor, issuer, subject, record.Email);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { errorCode = "staff_identity_required" });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new { errorCode = "staff_role_not_permitted" });
    }

    private static AuthenticateResult Success(StaffActor actor, string issuer, string subject, string? email)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actor.UserId),
            new(ClaimTypes.Name, actor.DisplayName),
            new(ClaimTypes.Role, actor.Role.ToString()),
            new("staff_issuer", issuer),
            new("staff_subject", subject)
        };
        if (!string.IsNullOrWhiteSpace(email)) claims.Add(new(ClaimTypes.Email, email));
        if (!string.IsNullOrWhiteSpace(actor.DealerExternalId)) claims.Add(new("dealer_external_id", actor.DealerExternalId));
        if (!string.IsNullOrWhiteSpace(actor.DealerName)) claims.Add(new("dealer_name", actor.DealerName));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private static AuthenticateResult ActivationCandidate(string issuer, string subject, string email, Guid invitationId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Email, email),
            new Claim("staff_issuer", issuer), new Claim("staff_subject", subject),
            new Claim("staff_activation_candidate", "true"), new Claim("staff_invitation_id", invitationId.ToString("D"))
        };
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}
