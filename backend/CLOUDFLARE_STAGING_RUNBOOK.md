# Cloudflare staging ingress runbook

This runbook activates the public staging topology:

```text
Cloudflare -> EC2:443 -> Nginx web container -> API container:8080 -> private RDS
```

Public hostnames:

- `console-staging.saltmonitor.dev` serves the staff UI and same-origin API. A Cloudflare Access application must
  protect the entire hostname.
- `telemetry-staging.saltmonitor.dev` exposes only `GET /health`, `POST /api/v1/device/activate`, and
  `POST /api/v1/device/telemetry`. Nginx returns 404 for the staff UI and all other API paths.

The EC2 origin is `3.142.69.53`. Both Cloudflare DNS records must be proxied (orange cloud). Never commit a TLS
certificate private key, Cloudflare API token, device credential, or database credential.

## 1. Create the Cloudflare origin certificate

In the Cloudflare dashboard, open **saltmonitor.dev -> SSL/TLS -> Origin Server -> Create Certificate**.

Use:

```text
Private key type: RSA (2048)
Hostnames: saltmonitor.dev, *.saltmonitor.dev
Validity: 15 years
```

Cloudflare displays the private key only when the certificate is created. Save both PEM values directly into a
secure password manager or into the EC2 files in the next section. Do not send the private key through chat,
email, issue trackers, or source control.

## 2. Install the certificate on EC2

Connect through AWS Systems Manager Session Manager or SSH. Create the protected directory and files with a
root-owned editor:

```bash
sudo install -d -o root -g root -m 0755 /etc/waterflex/tls
sudoedit /etc/waterflex/tls/origin.pem
sudoedit /etc/waterflex/tls/origin.key
sudo chown root:root /etc/waterflex/tls/origin.pem /etc/waterflex/tls/origin.key
sudo chmod 0644 /etc/waterflex/tls/origin.pem
sudo chmod 0600 /etc/waterflex/tls/origin.key
```

Paste the **Origin Certificate** into `origin.pem` and the **Private Key** into `origin.key`. Preserve the PEM
begin/end lines. Validate only the non-secret certificate metadata:

```bash
sudo openssl x509 -in /etc/waterflex/tls/origin.pem -noout -subject -issuer -dates
```

The Compose file uses those paths by default. Custom paths may be set in `/etc/waterflex/deployment.env`:

```text
WATERFLEX_TLS_CERTIFICATE_PATH=/absolute/path/origin.pem
WATERFLEX_TLS_PRIVATE_KEY_PATH=/absolute/path/origin.key
```

## 3. Restrict the EC2 security group

For application security group `sg-058771441e15f0d04`:

1. Add inbound TCP 443 rules for Cloudflare's published IPv4 and IPv6 ranges.
2. Remove public inbound rules for TCP 3000 and TCP 5188.
3. Keep SSH restricted to the administrator IP, or use Systems Manager and remove public SSH.
4. Do not add a public PostgreSQL rule. RDS remains private and accepts PostgreSQL only from the application
   security group.

Use the current ranges from <https://www.cloudflare.com/ips/>. Cloudflare changes these ranges infrequently but
they are external configuration and must be reviewed periodically. Restricting port 443 to Cloudflare prevents a
caller from bypassing Cloudflare Access by connecting directly to the Elastic IP with a forged Host header.

## 4. Publish and deploy

The staging image publisher requires a clean Git worktree so the image tag identifies the exact source revision.
After review and commit, publish from the Windows workstation:

```powershell
.\backend\tools\build-and-push-staging-images.ps1
```

Update `ECR_REGISTRY` and `IMAGE_TAG` in `/etc/waterflex/deployment.env`, update the EC2 checkout to that same
commit, and restart the service:

```bash
cd /home/ubuntu/waterflex-salt
git pull --ff-only
sudo systemctl restart waterflex-api.service
sudo systemctl status waterflex-api.service --no-pager
sudo journalctl -u waterflex-api.service -n 100 --no-pager
docker compose -f docker-compose.staging.yml ps
```

The startup script fails before deployment if either TLS file is missing. It also runs `nginx -t` and checks the
container-only health endpoint after the stack starts.

## 5. Enable strict Cloudflare TLS

After the Nginx container is healthy and EC2 accepts Cloudflare traffic on port 443:

1. Open **saltmonitor.dev -> SSL/TLS -> Overview**.
2. Set the encryption mode to **Full (strict)**.
3. Enable **Always Use HTTPS** under Edge Certificates.
4. Never use Flexible mode; it leaves the Cloudflare-to-origin hop unencrypted and conflicts with application
   HTTPS handling.

## 6. Verify

Run these tests from a workstation. Do not use `--insecure` for public verification.

```bash
curl -i https://telemetry-staging.saltmonitor.dev/health
curl -i https://telemetry-staging.saltmonitor.dev/api/v1/ops/dealers
curl -i -X POST https://telemetry-staging.saltmonitor.dev/api/v1/device/telemetry \
  -H 'Content-Type: application/json' \
  --data '{}'
```

Expected results:

- Health returns HTTP 200.
- The operations route returns HTTP 404 on the telemetry hostname.
- Telemetry without a bearer token returns HTTP 401.
- `https://console-staging.saltmonitor.dev` requires Cloudflare Access before displaying the console.
- A valid commissioned-device request returns HTTP 200 and persists a reading in RDS.
- TCP 3000 and TCP 5188 are unreachable from the internet.

Inspect Cloudflare Access logs, Nginx logs, API logs, and RDS metrics after the valid device test. `/health` is
process liveness only and does not prove database connectivity.
