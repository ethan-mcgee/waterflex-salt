#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.staging.yml}"
AWS_REGION="${AWS_REGION:-us-east-2}"
SECRET_ID="${SECRET_ID:-waterflex/staging/database/runtime}"
DB_NAME="${DB_NAME:-waterflex_salt_staging}"
DOCKER_BIN="${DOCKER_BIN:-docker}"

cd "${REPO_ROOT}"

if ! command -v "${DOCKER_BIN}" >/dev/null 2>&1; then
  echo "Docker is required but was not found in PATH." >&2
  exit 1
fi

if ! command -v aws >/dev/null 2>&1; then
  echo "AWS CLI is required but was not found in PATH." >&2
  exit 1
fi

if [[ -z "${ECR_REGISTRY:-}" || -z "${IMAGE_TAG:-}" ]]; then
  echo "ECR_REGISTRY and IMAGE_TAG must be configured in /etc/waterflex/deployment.env." >&2
  exit 1
fi

if [[ -z "${AWS_REGION}" ]]; then
  echo "AWS_REGION must be set to the target region." >&2
  exit 1
fi

echo "Retrieving runtime connection string from ${SECRET_ID} in ${AWS_REGION}."
SECRET_VALUE="$(aws secretsmanager get-secret-value --region "${AWS_REGION}" --secret-id "${SECRET_ID}" --query SecretString --output text)"

if [[ -z "${SECRET_VALUE}" ]]; then
  echo "The AWS Secrets Manager lookup returned an empty value." >&2
  exit 1
fi

if command -v python3 >/dev/null 2>&1; then
  PYTHON_BIN="python3"
elif command -v python >/dev/null 2>&1; then
  PYTHON_BIN="python"
else
  echo "Python is required to parse the AWS secret payload." >&2
  exit 1
fi

if [[ "${SECRET_VALUE}" =~ ^\{ ]]; then
  SECRET_VALUE="$("${PYTHON_BIN}" - "${SECRET_VALUE}" "${DB_NAME}" <<'PY'
import json
import sys
raw = sys.argv[1]
default_database = sys.argv[2]

if not raw or not raw.strip():
    print("")
    sys.exit(0)

try:
    data = json.loads(raw)
except json.JSONDecodeError:
    print(raw)
    sys.exit(0)

if not isinstance(data, dict):
    print(raw)
    sys.exit(0)

normalized = {str(key).lower(): value for key, value in data.items()}

for key in ("connectionstring", "connectionstrings__saltmonitor", "value", "uri", "url"):
  value = normalized.get(key)
    if isinstance(value, str) and value.strip():
        print(value)
        sys.exit(0)

host = normalized.get("host") or normalized.get("hostname") or normalized.get("server")
port = normalized.get("port")
dbname = normalized.get("dbname") or normalized.get("database") or default_database
username = normalized.get("username") or normalized.get("user")
password = normalized.get("password")

if host and username and password and dbname:
    port_value = port if port else 5432
    print(
        f"Host={host};Port={port_value};Database={dbname};Username={username};Password={password};SSL Mode=VerifyFull;Root Certificate=/etc/ssl/certs/aws-rds-global-bundle.pem"
    )
else:
    print(raw)
PY
)"
fi

if [[ -z "${SECRET_VALUE}" ]]; then
  echo "The resolved runtime connection string is empty." >&2
  exit 1
fi

export ConnectionStrings__SaltMonitor="${SECRET_VALUE}"
export AWS_REGION
export SECRET_ID

echo "Authenticating Docker to ${ECR_REGISTRY}."
aws ecr get-login-password --region "${AWS_REGION}" \
  | "${DOCKER_BIN}" login --username AWS --password-stdin "${ECR_REGISTRY}"

echo "Pulling staging images tagged ${IMAGE_TAG}."
"${DOCKER_BIN}" compose -f "${COMPOSE_FILE}" pull

echo "Starting staging stack from ${COMPOSE_FILE}."
"${DOCKER_BIN}" compose -f "${COMPOSE_FILE}" up -d --no-build --remove-orphans
