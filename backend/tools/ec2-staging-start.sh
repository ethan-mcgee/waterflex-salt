#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.staging.yml}"
ENV_FILE="${ENV_FILE:-.env.staging}"
AWS_REGION="${AWS_REGION:-us-east-2}"
SECRET_ID="${SECRET_ID:-waterflex/staging/database/runtime}"
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

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Expected environment file ${ENV_FILE} at ${REPO_ROOT}." >&2
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

if [[ "${SECRET_VALUE}" =~ ^\{ ]]; then
  SECRET_VALUE="$(python3 - "${SECRET_VALUE}" <<'PY'
import json
import sys
raw = sys.argv[1]
try:
    data = json.loads(raw)
except json.JSONDecodeError as exc:
    raise SystemExit(f"Unable to parse JSON secret: {exc}") from exc

if isinstance(data, dict):
    for key in ("connectionString", "ConnectionStrings__SaltMonitor", "value"):
        value = data.get(key)
        if isinstance(value, str) and value.strip():
            print(value)
            break
    else:
        raise SystemExit("The JSON secret did not contain a connection string field.")
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

echo "Starting staging stack from ${COMPOSE_FILE}."
"${DOCKER_BIN}" compose -f "${COMPOSE_FILE}" --env-file "${ENV_FILE}" up -d --build
