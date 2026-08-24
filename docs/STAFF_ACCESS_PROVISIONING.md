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
- Configure Cognito as the Cloudflare Access identity provider.
- The worker discovers or creates `WaterFlex-Privileged` and `WaterFlex-Dealer` reusable Access groups; explicit group IDs remain optional overrides.
- Require Cloudflare independent TOTP in the privileged policy; do not require it in the dealer policy.
- Give the worker task/instance role only Cognito `AdminGetUser`, `AdminCreateUser`, `AdminEnableUser`, `AdminDisableUser`, and `AdminUserGlobalSignOut` for this pool.
- Store a Cloudflare token limited to Access group read/edit in AWS Secrets Manager and inject it into the worker at deployment.
- Apply database migrations before enabling the worker.

## Acceptance gates

- A role/capability matrix test proves both inheritance and denial paths.
- PostgreSQL integration tests prove dealer isolation, duplicate invitation rejection, audit/outbox atomicity, concurrency, and last-administrator protection.
- Adapter tests prove retry/idempotency and that group reconciliation removes stale membership.
- API tests prove authentication, authorization, activation, mutation-header protection, and safe error responses.
- UI tests prove role-aware navigation and invitation/state-change behavior.
- Staging acceptance requires four test users, TOTP enrollment for both WaterFlex roles, denial of dealer access to fleet APIs, denial of WaterFlex employees to staff administration, promotion/demotion session revocation, recovery, and reconciliation evidence.
