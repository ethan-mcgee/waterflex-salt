# Staff access provisioning

WaterFlex stores the authoritative role and dealer scope. Amazon Cognito supplies email and password authentication. Cloudflare Access protects the console and separates exact-email membership into `WaterFlex-Privileged` and `WaterFlex-Dealer` groups.

## Role matrix

| Role | Console access | MFA | Administration scope |
| --- | --- | --- | --- |
| WaterFlex administrator | Fleet, alerts, staff | Cloudflare independent TOTP required | All staff |
| WaterFlex employee | Fleet and alerts | Cloudflare independent TOTP required | None |
| Dealer administrator | Provisioning and staff | Email and password | Own dealer |
| Dealer technician | Provisioning | Email and password | None |

Any standards-compliant TOTP application, including Duo Mobile, may store the Cloudflare TOTP seed. Duo Push is not part of this design.

## Provisioning sequence

1. An authorized administrator creates an invitation with a reason.
2. The API commits the invitation, audit event, and idempotent outbox item in one database transaction.
3. The worker creates the Cognito user, reconciles both Cloudflare exact-email groups, and marks the invitation ready.
4. The invited email owner signs in through Cloudflare and activates the matching invitation. Issuer and immutable subject are then bound to the WaterFlex identity.
5. Suspensions fail closed in WaterFlex first, then disable and globally sign out Cognito before Cloudflare reconciliation.
6. Role changes and reactivation remain inactive in `deprovisioning` until external synchronization and global sign-out complete.

## First administrator

Set `StaffProvisioning__BootstrapAdministratorEmail` only for an empty installation. The worker creates a one-time WaterFlex administrator invitation if no staff identities or invitations exist. Remove the setting after activation. There is no public bootstrap endpoint.

## Deployment prerequisites

- Create a Cognito user pool with email sign-in, temporary-password invitation delivery, and a strong password policy.
- Configure the console app client with `ALLOW_USER_PASSWORD_AUTH`, `ALLOW_USER_SRP_AUTH`, and `ALLOW_REFRESH_TOKEN_AUTH`. Do not enable `ALLOW_USER_AUTH`: choice-based authentication separates the email and password prompts in Managed Login, even when password is the only available choice.
- Configure Cognito as the Cloudflare Access identity provider.
- The worker discovers or creates `WaterFlex-Privileged` and `WaterFlex-Dealer` reusable Access groups; explicit group IDs remain optional overrides.
- Require Cloudflare independent TOTP in the privileged policy; do not require it in the dealer policy.
- Give the worker task/instance role only Cognito `AdminGetUser`, `AdminCreateUser`, `AdminEnableUser`, `AdminDisableUser`, and `AdminUserGlobalSignOut` for this pool.
- Store a Cloudflare token limited to Access group read/edit in AWS Secrets Manager and inject it into the worker at deployment.
- Apply database migrations before enabling the worker.

## Cognito Managed Login

The console uses Cognito Managed Login. Cognito owns the credential form; WaterFlex must never collect or proxy staff passwords. The first sign-in page presents email and password together. Account recovery, required temporary-password replacement, and Cloudflare's independent TOTP remain later steps when applicable.

For staging, the expected configuration is:

| Setting | Value |
| --- | --- |
| Region | `us-east-2` |
| User pool | `us-east-2_LR2vyJKuQ` |
| App client | `waterflex-console-staging` (`5dgnp1egc7kj0ptepkjofpn896`) |
| Sign-in identifier | Email |
| Branding | Managed Login |
| Identity provider | Cognito user pool directory |
| OAuth grant | Authorization code |
| Callback | `https://broad-mountain-76be.cloudflareaccess.com/cdn-cgi/access/callback` |
| Scopes | `email`, `openid`, `phone`, `profile` |

Before and after changing the app client, authenticate the AWS CLI and run the validation script. Pass `-SnapshotPath` before a change to save a sanitized rollback record; the script deliberately excludes the client secret.

```powershell
.\backend\tools\verify-cognito-login.ps1 `
  -UserPoolId us-east-2_LR2vyJKuQ `
  -ClientId 5dgnp1egc7kj0ptepkjofpn896 `
  -SnapshotPath .\cognito-login-staging.snapshot.json
```

The snapshot is operational evidence and can contain environment identifiers and URLs. Store it with release evidence, not in source control.

To roll back the combined credential form, restore the captured `ExplicitAuthFlows` on the same app client. Do not change the user-pool domain, Managed Login style, callback URL, OAuth grant, scopes, or Cloudflare identity-provider configuration during this rollback.

## Acceptance gates

- A role/capability matrix test proves both inheritance and denial paths.
- PostgreSQL integration tests prove dealer isolation, duplicate invitation rejection, audit/outbox atomicity, concurrency, and last-administrator protection.
- Adapter tests prove retry/idempotency and that group reconciliation removes stale membership.
- API tests prove authentication, authorization, activation, mutation-header protection, and safe error responses.
- UI tests prove role-aware navigation and invitation/state-change behavior.
- A private-browser staging test proves that email and password appear on one Cognito page, forgotten-password and temporary-password flows still work, privileged roles receive Cloudflare TOTP, dealer roles do not, and authentication returns to the console without a redirect loop.
- Staging acceptance requires four test users, TOTP enrollment for both WaterFlex roles, denial of dealer access to fleet APIs, denial of WaterFlex employees to staff administration, promotion/demotion session revocation, recovery, and reconciliation evidence.
