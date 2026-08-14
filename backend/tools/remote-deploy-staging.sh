#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# -ne 8 ]]; then
  echo "Usage: $0 <bundle-s3-uri> <bundle-sha256> <ecr-registry> <image-tag> <api-digest> <worker-digest> <web-digest> <migrations-digest>" >&2
  exit 64
fi

BUNDLE_URI="$1"
EXPECTED_SHA256="$2"
ECR_REGISTRY_VALUE="$3"
IMAGE_TAG_VALUE="$4"
API_DIGEST="$5"
WORKER_DIGEST="$6"
WEB_DIGEST="$7"
MIGRATIONS_DIGEST="$8"
DEPLOY_ROOT="/home/ubuntu/waterflex-salt"
ENV_FILE="/etc/waterflex/deployment.env"

[[ "${BUNDLE_URI}" =~ ^s3://[A-Za-z0-9._/-]+$ ]] || { echo "Invalid S3 bundle URI." >&2; exit 64; }
[[ "${EXPECTED_SHA256}" =~ ^[a-f0-9]{64}$ ]] || { echo "Invalid bundle SHA-256." >&2; exit 64; }
[[ "${ECR_REGISTRY_VALUE}" =~ ^[0-9]{12}\.dkr\.ecr\.[a-z0-9-]+\.amazonaws\.com$ ]] || { echo "Invalid ECR registry." >&2; exit 64; }
[[ "${IMAGE_TAG_VALUE}" =~ ^[a-f0-9]{40}$ ]] || { echo "Image tag must be a full Git commit SHA." >&2; exit 64; }
for digest in "${API_DIGEST}" "${WORKER_DIGEST}" "${WEB_DIGEST}" "${MIGRATIONS_DIGEST}"; do
  [[ "${digest}" =~ ^sha256:[a-f0-9]{64}$ ]] || { echo "Image digest must be sha256 followed by 64 lowercase hex characters." >&2; exit 64; }
done
[[ -f "${ENV_FILE}" ]] || { echo "Missing ${ENV_FILE}." >&2; exit 1; }

for command in aws awk docker install python3 sha256sum stat systemctl tar; do
  command -v "${command}" >/dev/null 2>&1 || { echo "Required command not found: ${command}" >&2; exit 1; }
done

read_environment_value() {
  local key="$1"
  awk -F= -v key="${key}" '$1 == key { sub(/^[^=]*=/, ""); print; exit }' "${ENV_FILE}"
}

AWS_REGION_VALUE="$(read_environment_value AWS_REGION)"
AWS_REGION_VALUE="${AWS_REGION_VALUE:-us-east-2}"
MIGRATION_SECRET_ID="$(read_environment_value MIGRATION_SECRET_ID)"
MIGRATION_SECRET_ID="${MIGRATION_SECRET_ID:-waterflex/staging/database/migrator}"

TEMP_DIR="$(mktemp -d /tmp/waterflex-deploy.XXXXXX)"
BACKUP_DIR="/opt/waterflex/deploy-backups/${IMAGE_TAG_VALUE}"
cleanup() { rm -rf -- "${TEMP_DIR}"; }
trap cleanup EXIT

mkdir -p "${BACKUP_DIR}" "${DEPLOY_ROOT}/backend/tools" "${DEPLOY_ROOT}/web/nginx"
cp -a "${ENV_FILE}" "${BACKUP_DIR}/deployment.env"
for relative_path in docker-compose.staging.yml backend/tools/ec2-staging-start.sh web/nginx/staging.conf; do
  if [[ -f "${DEPLOY_ROOT}/${relative_path}" ]]; then
    mkdir -p "${BACKUP_DIR}/$(dirname "${relative_path}")"
    cp -a "${DEPLOY_ROOT}/${relative_path}" "${BACKUP_DIR}/${relative_path}"
  fi
done

aws s3 cp "${BUNDLE_URI}" "${TEMP_DIR}/deployment-bundle.tar.gz" --only-show-errors
ACTUAL_SHA256="$(sha256sum "${TEMP_DIR}/deployment-bundle.tar.gz" | awk '{print $1}')"
[[ "${ACTUAL_SHA256}" == "${EXPECTED_SHA256}" ]] || { echo "Deployment bundle checksum mismatch." >&2; exit 1; }
tar -xzf "${TEMP_DIR}/deployment-bundle.tar.gz" -C "${TEMP_DIR}"

install -m 0644 "${TEMP_DIR}/docker-compose.staging.yml" "${DEPLOY_ROOT}/docker-compose.staging.yml"
install -m 0755 "${TEMP_DIR}/backend/tools/ec2-staging-start.sh" "${DEPLOY_ROOT}/backend/tools/ec2-staging-start.sh"
install -m 0644 "${TEMP_DIR}/web/nginx/staging.conf" "${DEPLOY_ROOT}/web/nginx/staging.conf"

set_environment_value() {
  local key="$1"
  local value="$2"
  local replacement_file="${TEMP_DIR}/deployment.env"
  awk -v key="${key}" -v value="${value}" '
    BEGIN { found = 0 }
    $0 ~ "^" key "=" { print key "=" value; found = 1; next }
    { print }
    END { if (!found) print key "=" value }
  ' "${ENV_FILE}" > "${replacement_file}"
  install -m 0600 "${replacement_file}" "${ENV_FILE}"
}

rollback() {
  echo "Deployment failed; restoring the previous release files." >&2
  install -m 0600 "${BACKUP_DIR}/deployment.env" "${ENV_FILE}"
  for relative_path in docker-compose.staging.yml backend/tools/ec2-staging-start.sh web/nginx/staging.conf; do
    if [[ -f "${BACKUP_DIR}/${relative_path}" ]]; then
      install -m "$(stat -c '%a' "${BACKUP_DIR}/${relative_path}")" \
        "${BACKUP_DIR}/${relative_path}" "${DEPLOY_ROOT}/${relative_path}"
    fi
  done
  systemctl restart waterflex-api.service || true
}

deployment_error() {
  local status="$1"
  trap - ERR
  rollback
  exit "${status}"
}

trap 'deployment_error $?' ERR

set_environment_value ECR_REGISTRY "${ECR_REGISTRY_VALUE}"
set_environment_value IMAGE_TAG "${IMAGE_TAG_VALUE}"
set_environment_value API_IMAGE "${ECR_REGISTRY_VALUE}/waterflex-api@${API_DIGEST}"
set_environment_value WORKER_IMAGE "${ECR_REGISTRY_VALUE}/waterflex-worker@${WORKER_DIGEST}"
set_environment_value WEB_IMAGE "${ECR_REGISTRY_VALUE}/waterflex-web@${WEB_DIGEST}"

echo "Applying database migrations with the dedicated migrator secret."
MIGRATION_SECRET="$(aws secretsmanager get-secret-value \
  --region "${AWS_REGION_VALUE}" \
  --secret-id "${MIGRATION_SECRET_ID}" \
  --query SecretString \
  --output text)"
[[ -n "${MIGRATION_SECRET}" ]] || { echo "Migrator secret lookup returned an empty value." >&2; rollback; exit 1; }

if [[ "${MIGRATION_SECRET}" =~ ^\{ ]]; then
  MIGRATION_CONNECTION_STRING="$(python3 -c '
import json
import sys

data = json.load(sys.stdin)
normalized = {str(key).lower(): value for key, value in data.items()}
for key in ("connectionstring", "connectionstrings__saltmonitor", "value", "uri", "url"):
    value = normalized.get(key)
    if isinstance(value, str) and value.strip():
        print(value)
        raise SystemExit(0)

host = normalized.get("host") or normalized.get("hostname") or normalized.get("server")
port = normalized.get("port") or 5432
database = normalized.get("dbname") or normalized.get("database") or "waterflex_salt_staging"
username = normalized.get("username") or normalized.get("user")
password = normalized.get("password")
if not all((host, database, username, password)):
    raise SystemExit("Migrator secret does not contain connection fields.")
print(f"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/aws-rds-global-bundle.pem")
' <<<"${MIGRATION_SECRET}")"
else
  MIGRATION_CONNECTION_STRING="${MIGRATION_SECRET}"
fi
unset MIGRATION_SECRET

aws ecr get-login-password --region "${AWS_REGION_VALUE}" \
  | docker login --username AWS --password-stdin "${ECR_REGISTRY_VALUE}" >/dev/null
MIGRATION_IMAGE="${ECR_REGISTRY_VALUE}/waterflex-migrations@${MIGRATIONS_DIGEST}"
docker pull "${MIGRATION_IMAGE}"
systemctl stop waterflex-api.service
if ! printf '%s\n' "${MIGRATION_CONNECTION_STRING}" | docker run --rm --interactive \
  --volume /etc/ssl/certs/aws-rds-global-bundle.pem:/etc/ssl/certs/aws-rds-global-bundle.pem:ro \
  "${MIGRATION_IMAGE}"; then
  unset MIGRATION_CONNECTION_STRING
  rollback
  exit 1
fi
unset MIGRATION_CONNECTION_STRING

if ! systemctl restart waterflex-api.service; then
  rollback
  exit 1
fi
if ! systemctl is-active --quiet waterflex-api.service; then
  rollback
  exit 1
fi

cd "${DEPLOY_ROOT}"
docker compose -f docker-compose.staging.yml ps
trap - ERR
echo "WaterFlex staging deployed image tag ${IMAGE_TAG_VALUE}."
