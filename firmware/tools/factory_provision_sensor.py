#!/usr/bin/env python3
"""Provision one WaterFlex Nano through the factory API and USB serial port."""

from __future__ import annotations

import argparse
import base64
import ctypes
import hashlib
import json
import os
import re
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from ctypes import wintypes
from pathlib import Path

import serial
from serial.tools import list_ports


FIRMWARE_ENVIRONMENT = "arduino_nano_esp32"
FACTORY_BAUD = 115200


class DataBlob(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]


def dpapi(value: bytes, decrypt: bool = False) -> bytes:
    if os.name != "nt":
        raise RuntimeError("Factory job protection requires Windows DPAPI.")
    source = ctypes.create_string_buffer(value)
    source_blob = DataBlob(len(value), ctypes.cast(source, ctypes.POINTER(ctypes.c_char)))
    output_blob = DataBlob()
    crypt32 = ctypes.windll.crypt32
    if decrypt:
        function = crypt32.CryptUnprotectData
        function.argtypes = [
            ctypes.POINTER(DataBlob), ctypes.POINTER(wintypes.LPWSTR), ctypes.POINTER(DataBlob),
            ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(DataBlob),
        ]
        function.restype = wintypes.BOOL
        ok = function(ctypes.byref(source_blob), None, None, None, None, 1, ctypes.byref(output_blob))
    else:
        function = crypt32.CryptProtectData
        function.argtypes = [
            ctypes.POINTER(DataBlob), wintypes.LPCWSTR, ctypes.POINTER(DataBlob),
            ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(DataBlob),
        ]
        function.restype = wintypes.BOOL
        ok = function(ctypes.byref(source_blob), "WaterFlex factory job", None, None, None, 1, ctypes.byref(output_blob))
    if not ok:
        raise ctypes.WinError()
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(output_blob.pbData)


def save_job(path: Path, job: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(dpapi(json.dumps(job, separators=(",", ":")).encode("utf-8")))


def load_or_create_job(path: Path) -> dict:
    if path.exists():
        return json.loads(dpapi(path.read_bytes(), decrypt=True).decode("utf-8"))
    secret = secrets.token_bytes(32)
    job = {
        "idempotencyKey": str(uuid.uuid4()),
        "bootstrapCredentialId": f"wf_boot_{secrets.token_hex(12)}",
        "bootstrapSecret": base64.urlsafe_b64encode(secret).decode("ascii").rstrip("="),
        "bootstrapSecretHash": base64.b64encode(hashlib.sha256(secret).digest()).decode("ascii"),
        "setupPassphrase": secrets.token_urlsafe(18),
    }
    save_job(path, job)
    return job


def api_json(method: str, url: str, headers: dict[str, str], body: dict | None = None) -> dict:
    request = urllib.request.Request(
        url,
        data=None if body is None else json.dumps(body).encode("utf-8"),
        method=method,
        headers={"Accept": "application/json", "Content-Type": "application/json", **headers},
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Factory API returned HTTP {error.code}: {detail}") from error


def detect_port(explicit_port: str | None) -> str:
    available = list(list_ports.comports())
    if explicit_port and any(port.device.casefold() == explicit_port.casefold() for port in available):
        return explicit_port
    candidates = [
        port.device
        for port in available
        if any(marker in f"{port.description} {port.manufacturer}".lower()
               for marker in ("arduino", "nano esp32", "esp32", "usb jtag"))
    ]
    if len(candidates) != 1:
        raise RuntimeError(f"Expected exactly one Nano ESP32 serial port; found {candidates or 'none'}.")
    return candidates[0]


def firmware_version(firmware_dir: Path) -> str:
    config = (firmware_dir / "src" / "config.h").read_text(encoding="utf-8")
    match = re.search(r'kFirmwareVersion\[\]\s*=\s*"([^"]+)"', config)
    if not match:
        raise RuntimeError("Unable to read kFirmwareVersion from firmware/src/config.h.")
    return match.group(1)


def run_platformio(pio: Path, firmware_dir: Path, port: str) -> str:
    command = [str(pio), "run", "-e", FIRMWARE_ENVIRONMENT, "-t", "upload", "--upload-port", port]
    first = subprocess.run(command, cwd=firmware_dir, check=False)
    if first.returncode == 0:
        return port
    time.sleep(2)
    bootloader_port = detect_port(None)
    if bootloader_port.casefold() == port.casefold():
        first.check_returncode()
    retry = [str(pio), "run", "-e", FIRMWARE_ENVIRONMENT, "-t", "upload", "--upload-port", bootloader_port]
    subprocess.run(retry, cwd=firmware_dir, check=True)
    return bootloader_port


def serial_factory_provision(port: str, identity: dict, expected_serial: str, expected_firmware: str) -> dict:
    deadline = time.monotonic() + 45
    evidence = {"identity": False, "portal": False, "sensor": False, "firmware": False}
    with serial.Serial(port, FACTORY_BAUD, timeout=0.5) as connection:
        time.sleep(2)
        payload = json.dumps(identity, separators=(",", ":"))
        connection.write(f"FACTORY_PROVISION {payload}\n".encode("utf-8"))
        provision_sent_at = time.monotonic()
        while time.monotonic() < deadline:
            line = connection.readline().decode("utf-8", errors="replace").strip()
            if not line:
                if time.monotonic() - provision_sent_at > 8 and not evidence["identity"]:
                    connection.write(b"FACTORY_STATUS\n")
                    provision_sent_at = time.monotonic()
                continue
            if line.startswith("factory_provisioning_result=") and '"status":"rejected"' in line:
                if "factory_identity_already_present" not in line:
                    raise RuntimeError(line)
                connection.write(b"FACTORY_STATUS\n")
            if line.startswith("factory_status="):
                status = json.loads(line.split("=", 1)[1])
                evidence["identity"] = status.get("serialNumber") == expected_serial
                evidence["firmware"] = status.get("firmwareVersion") == expected_firmware
                evidence["portal"] = bool(status.get("portalRunning"))
                if status.get("operationalCredentialConfigured"):
                    raise RuntimeError("Factory unit unexpectedly contains an operational credential.")
            elif line.startswith("portal started ssid="):
                evidence["portal"] = f"ssid={expected_serial} " in line
            elif re.fullmatch(r"distance=\d+ mm", line):
                evidence["sensor"] = True
            elif f"serialNumber={expected_serial}" in line and f"firmwareVersion={expected_firmware}" in line:
                evidence["identity"] = evidence["firmware"] = True
            if all(evidence.values()):
                return evidence
        raise RuntimeError(f"Factory verification timed out: {evidence}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Flash and provision one WaterFlex Nano ESP32")
    parser.add_argument("--base-url", default="http://127.0.0.1:5188")
    parser.add_argument("--factory-key", default=os.environ.get("WATERFLEX_FACTORY_KEY"))
    parser.add_argument("--operator", required=True)
    parser.add_argument("--port")
    parser.add_argument("--pio", required=True, type=Path)
    parser.add_argument("--job-state", required=True, type=Path)
    parser.add_argument("--label-out", required=True, type=Path)
    args = parser.parse_args()
    if not args.factory_key:
        parser.error("--factory-key or WATERFLEX_FACTORY_KEY is required")

    firmware_dir = Path(__file__).resolve().parents[1]
    version = firmware_version(firmware_dir)
    job = load_or_create_job(args.job_state.resolve())
    headers = {
        "X-WaterFlex-Factory-Key": args.factory_key,
        "X-WaterFlex-Factory-Operator": args.operator,
    }
    registration = api_json(
        "POST",
        args.base_url.rstrip("/") + "/api/v1/factory/devices",
        headers,
        {
            "idempotencyKey": job["idempotencyKey"],
            "model": "Arduino Nano ESP32",
            "bootstrapCredentialId": job["bootstrapCredentialId"],
            "bootstrapSecretHash": job["bootstrapSecretHash"],
            "firmwareVersion": version,
            "configurationVersion": "factory-v2",
        },
    )
    serial_number = registration["serialNumber"]
    port = detect_port(args.port)
    port = run_platformio(args.pio.resolve(), firmware_dir, port)
    time.sleep(2)
    port = detect_port(port)
    verification_url = args.base_url.rstrip("/") + f'/api/v1/factory/devices/{registration["deviceId"]}/verification'
    try:
        evidence = serial_factory_provision(
            port,
            {
                "serialNumber": serial_number,
                "setupPassphrase": job["setupPassphrase"],
                "bootstrapToken": f'{job["bootstrapCredentialId"]}.{job["bootstrapSecret"]}',
            },
            serial_number,
            version,
        )
        verification = api_json(
            "POST",
            verification_url,
            headers,
            {
                "firmwareVerified": evidence["firmware"],
                "identityVerified": evidence["identity"],
                "portalVerified": evidence["portal"],
                "sensorVerified": evidence["sensor"],
                "firmwareVersion": version,
                "failureCode": None,
            },
        )
    except Exception as error:
        try:
            api_json(
                "POST",
                verification_url,
                headers,
                {
                    "firmwareVerified": False,
                    "identityVerified": False,
                    "portalVerified": False,
                    "sensorVerified": False,
                    "firmwareVersion": version,
                    "failureCode": "factory_tool_verification_failed",
                },
            )
        except Exception:
            pass
        raise error
    args.label_out.parent.mkdir(parents=True, exist_ok=True)
    args.label_out.write_text(json.dumps({
        "serialNumber": serial_number,
        "setupNetwork": serial_number,
        "setupPassphrase": job["setupPassphrase"],
    }, indent=2), encoding="utf-8")
    os.chmod(args.label_out, 0o600)
    print("PASS")
    print(f"Serial: {serial_number}")
    print(f"Firmware: {version}")
    print(f"Backend inventory: {verification['status']}")
    print(f"Label payload: {args.label_out.resolve()}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1)
