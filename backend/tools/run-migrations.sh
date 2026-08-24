#!/bin/sh
set -eu

# Read the connection string from standard input so it is not retained in the
# container command, environment metadata, or the host process list.
IFS= read -r ConnectionStrings__SaltMonitor
if [ -z "${ConnectionStrings__SaltMonitor}" ]; then
  echo "Migration connection string was empty." >&2
  exit 64
fi

export ConnectionStrings__SaltMonitor
exec /app/waterflex-migrate
