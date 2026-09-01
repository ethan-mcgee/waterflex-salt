#!/usr/bin/env python3
"""Windows DPAPI and Nano serial primitives shared by the factory helper."""

from __future__ import annotations

import ctypes
import json
import os
import re
import time
from ctypes import wintypes

import serial
from serial.tools import list_ports

FACTORY_BAUD = 115200


class DataBlob(ctypes.Structure):
    _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]


def dpapi(value: bytes, decrypt: bool = False) -> bytes:
    """Protect or unprotect bytes for the current Windows user."""
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


def detect_port(explicit_port: str | None) -> str:
    """Require exactly one connected Nano-like serial device unless an explicit port still exists."""
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


def serial_factory_provision(port: str, identity: dict, expected_serial: str, expected_firmware: str) -> dict:
    """Inject factory identity over USB serial and collect end-of-line evidence."""
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
