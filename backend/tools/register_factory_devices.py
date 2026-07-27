#!/usr/bin/env python3
"""Batch-register factory devices from CSV into WaterFlex backend."""

from __future__ import annotations

import argparse
import base64
import csv
import hashlib
import json
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path


@dataclass
class RegistrationResult:
    serial_number: str
    hardware_id: str
    status_code: int
    outcome: str
    response: str


def sha256_base64(value: str) -> str:
    digest = hashlib.sha256(value.encode("utf-8")).digest()
    return base64.b64encode(digest).decode("ascii")


def normalize_hardware_id(value: str) -> str:
    return "".join(ch for ch in value.upper() if ch not in " :-")


def post_json(url: str, headers: dict[str, str], body: dict) -> tuple[int, str]:
    data = json.dumps(body).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        method="POST",
        headers={"Content-Type": "application/json", **headers},
    )

    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            payload = response.read().decode("utf-8")
            return response.status, payload
    except urllib.error.HTTPError as error:
        payload = error.read().decode("utf-8", errors="replace")
        return error.code, payload


def load_rows(path: Path) -> list[dict[str, str]]:
    with path.open("r", newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        rows = [dict(row) for row in reader]
    return rows


def build_payload(row: dict[str, str], default_model: str, default_fw: str, default_cfg: str) -> dict:
    serial_number = row["serialNumber"].strip().upper()
    hardware_id = normalize_hardware_id(row["hardwareId"])
    model = (row.get("model") or default_model).strip()
    firmware_version = (row.get("firmwareVersion") or default_fw).strip()
    configuration_version = (row.get("configurationVersion") or default_cfg).strip()

    credential_id = row.get("bootstrapCredentialId", "").strip()
    if not credential_id:
        credential_id = f"wf_boot_{serial_number.lower().replace('-', '_')}"

    plaintext = row.get("bootstrapSecretPlaintext", "").strip()
    secret_hash = row.get("bootstrapSecretHash", "").strip()
    if plaintext:
        secret_hash = sha256_base64(plaintext)

    if not secret_hash:
        raise ValueError("Row must include bootstrapSecretPlaintext or bootstrapSecretHash")

    return {
        "serialNumber": serial_number,
        "hardwareId": hardware_id,
        "model": model,
        "bootstrapCredentialId": credential_id,
        "bootstrapSecretHash": secret_hash,
        "firmwareVersion": firmware_version,
        "configurationVersion": configuration_version,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Batch factory device registration")
    parser.add_argument("--csv", required=True, help="Input CSV path")
    parser.add_argument("--base-url", default="http://127.0.0.1:5188", help="Backend base URL")
    parser.add_argument("--factory-key", required=True, help="FactoryProvisioning development key")
    parser.add_argument("--factory-operator", required=True, help="Factory operator id header value")
    parser.add_argument("--default-model", default="Arduino Nano ESP32")
    parser.add_argument("--default-firmware", default="wf-dev-telemetry-0.1")
    parser.add_argument("--default-config", default="factory-v1")
    parser.add_argument("--max-retries", type=int, default=2)
    parser.add_argument("--audit-out", default="factory-registration-audit.csv")
    args = parser.parse_args()

    input_path = Path(args.csv).resolve()
    rows = load_rows(input_path)
    if not rows:
        print("No rows in CSV.")
        return 1

    endpoint = args.base_url.rstrip("/") + "/api/v1/factory/devices"
    headers = {
        "X-WaterFlex-Factory-Key": args.factory_key,
        "X-WaterFlex-Factory-Operator": args.factory_operator,
    }

    results: list[RegistrationResult] = []

    for row in rows:
        payload = build_payload(
            row,
            args.default_model,
            args.default_firmware,
            args.default_config,
        )
        serial_number = payload["serialNumber"]
        hardware_id = payload["hardwareId"]

        status_code = 0
        response_text = ""
        for attempt in range(args.max_retries + 1):
            status_code, response_text = post_json(endpoint, headers, payload)
            if status_code in (200, 201, 409):
                break
            if attempt < args.max_retries:
                time.sleep(1.0 * (attempt + 1))

        outcome = "registered" if status_code in (200, 201) else "failed"
        if status_code == 409:
            outcome = "conflict"

        results.append(
            RegistrationResult(
                serial_number=serial_number,
                hardware_id=hardware_id,
                status_code=status_code,
                outcome=outcome,
                response=response_text,
            )
        )
        print(f"{serial_number} -> {status_code} ({outcome})")

    audit_path = Path(args.audit_out).resolve()
    with audit_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=["serialNumber", "hardwareId", "statusCode", "outcome", "response"],
        )
        writer.writeheader()
        for result in results:
            writer.writerow(
                {
                    "serialNumber": result.serial_number,
                    "hardwareId": result.hardware_id,
                    "statusCode": result.status_code,
                    "outcome": result.outcome,
                    "response": result.response,
                }
            )

    registered = sum(1 for result in results if result.outcome == "registered")
    conflicts = sum(1 for result in results if result.outcome == "conflict")
    failed = sum(1 for result in results if result.outcome == "failed")

    print(f"registered={registered} conflicts={conflicts} failed={failed}")
    print(f"audit={audit_path}")

    return 0 if failed == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
