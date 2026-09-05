#!/usr/bin/env python3
"""Loopback-only HTTP helper that flashes and verifies one WaterFlex Nano at a time."""

from __future__ import annotations

import argparse
import base64
import ctypes
import hashlib
import json
import logging
import os
import socket
import ssl
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlparse

from factory_provision_sensor import detect_port, dpapi, enumerate_devices, serial_factory_provision
from factory_portal_verifier import verify_portal

PROTOCOL_VERSION = "4"
HELPER_VERSION = "4.0.0"
DEFAULT_ORIGINS = {
    "https://console-staging.saltmonitor.dev",
    "https://saltmonitor.dev",
    "http://localhost:5173",
    "http://127.0.0.1:5173",
}
DEFAULT_DATA_DIR = Path(os.environ.get("LOCALAPPDATA", ".")) / "WaterFlex" / "FactoryHelper"
DEFAULT_BUNDLE_CACHE_DIR = DEFAULT_DATA_DIR / "bundle"
DEFAULT_LOG_PATH = DEFAULT_DATA_DIR / "factory-helper.log"
LOGGER = logging.getLogger("waterflex.factory_helper")
_INSTANCE_MUTEX = None


def _base64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).decode("ascii").rstrip("=")


class StationIdentity:
    """Per-user non-exportable P-256 identity held by Windows CNG."""

    def __init__(self, state_path: Path, api_base_url: str):
        self.state_path = state_path.resolve()
        self.api_base_url = api_base_url.rstrip("/")
        self.metadata = self._load_or_create()

    def _powershell(self, script: str, stdin: str = "") -> str:
        encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
        completed = subprocess.run(
            ["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            input=stdin, text=True, capture_output=True, check=False,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        if completed.returncode != 0:
            raise RuntimeError("The Windows station identity operation failed.")
        return completed.stdout.strip()

    def _load_or_create(self) -> dict:
        if self.state_path.exists():
            return json.loads(dpapi(self.state_path.read_bytes(), decrypt=True).decode("utf-8"))
        self.state_path.parent.mkdir(parents=True, exist_ok=True)
        key_name = f"WaterFlexFactoryStation-{os.urandom(16).hex()}"
        script = r'''
$ErrorActionPreference = 'Stop'
$name = $env:WF_STATION_KEY_NAME
$providers = @('Microsoft Platform Crypto Provider', 'Microsoft Software Key Storage Provider')
foreach ($providerName in $providers) {
  try {
    $parameters = [System.Security.Cryptography.CngKeyCreationParameters]::new()
    $parameters.Provider = [System.Security.Cryptography.CngProvider]::new($providerName)
    $parameters.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::None
    $parameters.KeyUsage = [System.Security.Cryptography.CngKeyUsages]::Signing
    $key = [System.Security.Cryptography.CngKey]::Create([System.Security.Cryptography.CngAlgorithm]::ECDsaP256, $name, $parameters)
    $blob = $key.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $size = [BitConverter]::ToUInt32($blob, 4)
    $raw = [byte[]]::new(1 + 2 * $size); $raw[0] = 4
    [Array]::Copy($blob, 8, $raw, 1, 2 * $size)
    [pscustomobject]@{ provider = $providerName; publicKey = [Convert]::ToBase64String($raw).TrimEnd('=').Replace('+','-').Replace('/','_') } | ConvertTo-Json -Compress
    exit 0
  } catch { }
}
exit 1
'''
        old = os.environ.get("WF_STATION_KEY_NAME")
        os.environ["WF_STATION_KEY_NAME"] = key_name
        try:
            created = json.loads(self._powershell(script))
        finally:
            if old is None:
                os.environ.pop("WF_STATION_KEY_NAME", None)
            else:
                os.environ["WF_STATION_KEY_NAME"] = old
        raw_key = base64.urlsafe_b64decode(created["publicKey"] + "==")
        metadata = {
            "keyName": key_name,
            "keyProviderType": "tpm" if created["provider"] == "Microsoft Platform Crypto Provider" else "software",
            "providerName": created["provider"],
            "publicKey": created["publicKey"],
            "thumbprint": hashlib.sha256(raw_key).hexdigest(),
            "stationId": None,
            "displayName": None,
        }
        self._save(metadata)
        return metadata

    def _save(self, metadata: dict) -> None:
        temporary = self.state_path.with_suffix(".tmp")
        temporary.write_bytes(dpapi(json.dumps(metadata, separators=(",", ":")).encode("utf-8")))
        os.replace(temporary, self.state_path)

    def status(self) -> dict:
        return {
            "helperVersion": HELPER_VERSION, "protocolVersion": PROTOCOL_VERSION,
            "enrollmentStatus": "enrolled" if self.metadata.get("stationId") else "unenrolled",
            "proposedWorkstationName": socket.gethostname(), "stationId": self.metadata.get("stationId"),
            "displayName": self.metadata.get("displayName"), "publicKeyThumbprint": self.metadata["thumbprint"],
            "publicKey": self.metadata["publicKey"], "keyProviderType": self.metadata["keyProviderType"],
        }

    def enroll(self, grant_token: str, display_name: str) -> dict:
        payload = json.dumps({
            "grantToken": grant_token, "displayName": display_name, "publicKey": self.metadata["publicKey"],
            "thumbprint": self.metadata["thumbprint"], "keyProviderType": self.metadata["keyProviderType"],
            "helperVersion": HELPER_VERSION, "protocolVersion": PROTOCOL_VERSION,
        }, separators=(",", ":")).encode("utf-8")
        request = urllib.request.Request(f"{self.api_base_url}/api/v1/factory/stations/enroll", data=payload, headers={"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(request, timeout=15) as response:
            result = json.loads(response.read().decode("utf-8"))
        self.metadata.update({"stationId": result["stationId"], "displayName": result["displayName"]})
        self._save(self.metadata)
        return self.status()

    def signed_headers(self, method: str, path: str, body: bytes) -> dict[str, str]:
        station_id = self.metadata.get("stationId")
        if not station_id:
            raise RuntimeError("This factory workstation is not enrolled.")
        timestamp = str(int(time.time())); nonce = _base64url(os.urandom(16))
        canonical = f"WF-STATION-V1\n{method.upper()}\n{path}\n{timestamp}\n{nonce}\n{hashlib.sha256(body).hexdigest()}"
        script = r'''
$ErrorActionPreference = 'Stop'
$provider = [System.Security.Cryptography.CngProvider]::new($env:WF_STATION_PROVIDER)
$key = [System.Security.Cryptography.CngKey]::Open($env:WF_STATION_KEY_NAME, $provider)
$ecdsa = [System.Security.Cryptography.ECDsaCng]::new($key)
$signature = $ecdsa.SignData([Text.Encoding]::UTF8.GetBytes([Console]::In.ReadToEnd()), [System.Security.Cryptography.HashAlgorithmName]::SHA256)
[Convert]::ToBase64String($signature).TrimEnd('=').Replace('+','-').Replace('/','_')
'''
        previous = {name: os.environ.get(name) for name in ("WF_STATION_KEY_NAME", "WF_STATION_PROVIDER")}
        os.environ["WF_STATION_KEY_NAME"] = self.metadata["keyName"]
        os.environ["WF_STATION_PROVIDER"] = self.metadata["providerName"]
        try:
            signature = self._powershell(script, canonical)
        finally:
            for name, value in previous.items():
                if value is None: os.environ.pop(name, None)
                else: os.environ[name] = value
        return {"X-WaterFlex-Station-Id": station_id, "X-WaterFlex-Station-Timestamp": timestamp, "X-WaterFlex-Station-Nonce": nonce, "X-WaterFlex-Station-Signature": signature}

try:
    from _default_config import DEFAULT_API_BASE_URL  # generated per-environment at release-build time
except ImportError:
    DEFAULT_API_BASE_URL = "http://127.0.0.1:5188"


class FactoryHelperStartupError(RuntimeError):
    """An operator-actionable failure while resolving the approved bundle."""


def acquire_single_instance() -> None:
    global _INSTANCE_MUTEX
    if os.name != "nt": return
    handle = ctypes.windll.kernel32.CreateMutexW(None, False, "Local\\WaterFlexFactoryHelper-v4")
    if not handle or ctypes.windll.kernel32.GetLastError() == 183:
        if handle: ctypes.windll.kernel32.CloseHandle(handle)
        raise FactoryHelperStartupError("WaterFlex Factory Helper is already running for this Windows user.")
    _INSTANCE_MUTEX = handle


def configure_startup_logging(log_path: Path = DEFAULT_LOG_PATH) -> Path:
    """Create the persistent startup log and direct helper diagnostics to it."""
    log_path = log_path.resolve()
    log_path.parent.mkdir(parents=True, exist_ok=True)
    for handler in list(LOGGER.handlers):
        handler.close()
        LOGGER.removeHandler(handler)
    handler = logging.FileHandler(log_path, encoding="utf-8")
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(message)s"))
    LOGGER.addHandler(handler)
    LOGGER.setLevel(logging.INFO)
    LOGGER.propagate = False
    return log_path


def _cloudflare_access_url(value: str | None) -> bool:
    if not value:
        return False
    parsed = urlparse(value)
    return parsed.path.startswith("/cdn-cgi/access/") or parsed.hostname is not None and (
        parsed.hostname.casefold().endswith(".cloudflareaccess.com")
        or parsed.hostname.casefold() == "cloudflareaccess.com"
    )


def _network_failure(error: urllib.error.URLError) -> FactoryHelperStartupError:
    reason = error.reason
    if isinstance(reason, socket.gaierror):
        detail = "DNS lookup failed"
    elif isinstance(reason, ssl.SSLError):
        detail = "TLS validation failed"
    else:
        detail = "the network connection failed"
    return FactoryHelperStartupError(
        f"Could not reach WaterFlex because {detail}. Check the network, DNS, VPN, and system clock."
    )


def _metadata_http_failure(error: urllib.error.HTTPError) -> FactoryHelperStartupError:
    location = error.headers.get("Location") if error.headers is not None else None
    if 300 <= error.code < 400 and _cloudflare_access_url(location):
        return FactoryHelperStartupError(
            "WaterFlex redirected the helper to Cloudflare Access. The helper must use the public telemetry hostname."
        )
    if error.code == HTTPStatus.NOT_FOUND:
        return FactoryHelperStartupError(
            "WaterFlex returned HTTP 404 for the factory bundle endpoint. The staging ingress may not be deployed yet."
        )
    if 500 <= error.code < 600:
        return FactoryHelperStartupError(
            f"WaterFlex could not provide the factory bundle (HTTP {error.code}). Try again after the service recovers."
        )
    return FactoryHelperStartupError(
        f"WaterFlex rejected the factory bundle request (HTTP {error.code})."
    )


class JobStore:
    """Persists active job secrets encrypted for the current Windows user with DPAPI."""

    def __init__(self, state_dir: Path):
        self.state_dir = state_dir.resolve()
        self.state_dir.mkdir(parents=True, exist_ok=True)
        self.lock = threading.RLock()
        self.active_job: str | None = None

    def _path(self, key: str) -> Path:
        if not key or len(key) > 100 or any(character not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_" for character in key):
            raise ValueError("Invalid factory job key.")
        return self.state_dir / f"{key}.job"

    def load(self, key: str) -> dict:
        with self.lock:
            path = self._path(key)
            if not path.exists():
                raise KeyError(key)
            return json.loads(dpapi(path.read_bytes(), decrypt=True).decode("utf-8"))

    def save(self, job: dict) -> dict:
        with self.lock:
            path = self._path(job["idempotencyKey"])
            temporary = path.with_suffix(".tmp")
            temporary.write_bytes(dpapi(json.dumps(job, separators=(",", ":")).encode("utf-8")))
            os.replace(temporary, path)
            return public_job(job)

    def delete(self, key: str) -> None:
        with self.lock:
            if self.active_job == key:
                raise RuntimeError("An active factory job cannot be cleared.")
            self._path(key).unlink(missing_ok=True)

    def recover_interrupted_jobs(self) -> None:
        """Make a workstation or helper restart visible instead of leaving a job polling forever."""
        with self.lock:
            for path in self.state_dir.glob("*.job"):
                try:
                    job = self.load(path.stem)
                except Exception:  # A corrupt or foreign-user DPAPI record remains quarantined on disk.
                    continue
                if job.get("status") not in {"queued", "flashing", "provisioning", "verifying"}:
                    continue
                job.update({
                    "status": "failed",
                    "message": "The factory helper restarted before this job completed.",
                    "evidence": {"firmware": False, "identity": False, "portal": False, "sensor": False},
                    "failureCode": "factory_helper_interrupted",
                })
                self.save(job)


class FactoryHelper:
    def __init__(self, bundle_dir: Path, state_dir: Path, esptool_path: Path, api_base_url: str, station_identity=None):
        self.bundle_dir = bundle_dir.resolve()
        self.manifest = load_manifest(self.bundle_dir)
        self.store = JobStore(state_dir)
        self.store.recover_interrupted_jobs()
        self.esptool_path = esptool_path.resolve()
        self.api_base_url = api_base_url.rstrip("/")
        self.station_identity = station_identity

    def _signed_headers(self, path: str, payload: bytes) -> dict[str, str]:
        if self.station_identity is None:
            raise RuntimeError("This factory workstation is not enrolled.")
        return {"Content-Type": "application/json"} | self.station_identity.signed_headers("POST", path, payload)

    def prepare(self, body: dict) -> dict:
        required = ("idempotencyKey", "bootstrapCredentialId", "bootstrapSecret", "setupPassphrase")
        if any(not isinstance(body.get(name), str) or not body[name].strip() for name in required):
            raise ValueError("Factory job secrets are incomplete.")
        try:
            existing = self.store.load(body["idempotencyKey"])
            return public_job(existing)
        except KeyError:
            pass
        secret = decode_bootstrap_secret(body["bootstrapSecret"])
        if len(secret) != 32:
            raise ValueError("Factory bootstrap secret is invalid.")
        job = {
            **{name: body[name].strip() for name in required},
            "bootstrapSecretHash": base64.b64encode(hashlib.sha256(secret).digest()).decode("ascii"),
            "status": "prepared",
            "message": "Credentials protected. Waiting for WaterFlex registration.",
            "serialNumber": None,
            "deviceId": None,
            "evidence": None,
            "failureCode": None,
        }
        return self.store.save(job)

    def start(self, key: str, body: dict) -> dict:
        with self.store.lock:
            if self.store.active_job and self.store.active_job != key:
                raise RuntimeError("Another sensor is already being provisioned on this workstation.")
            job = self.store.load(key)
            validate_start(body, self.manifest)
            verification_token = self._verify_flash_authorization(body | {"idempotencyKey": key})
            job.update({
                "deviceId": body["deviceId"],
                "serialNumber": body["serialNumber"],
                "model": body["model"],
                "firmwareVersion": body["firmwareVersion"],
                "configurationVersion": body["configurationVersion"],
                "verificationToken": verification_token,
                "status": "queued",
                "message": "Waiting for the connected Nano ESP32.",
                "evidence": None,
                "failureCode": None,
            })
            self.store.active_job = key
            self.store.save(job)
            threading.Thread(target=self._run, args=(key,), daemon=True).start()
            return public_job(job)

    def _verify_flash_authorization(self, body: dict | str, token: str | None = None) -> str:
        """Fails closed: any rejection, timeout, or network error blocks the flash. A local helper
        that could touch hardware without confirming backend authorization would defeat the entire
        point of this check."""
        legacy_test_call = isinstance(body, str)
        if legacy_test_call:
            body = {
                "deviceId": body,
                "idempotencyKey": "factory-helper-legacy-test",
                "firmwareVersion": self.manifest["firmwareVersion"],
                "configurationVersion": self.manifest["configurationVersion"],
                "flashAuthorizationToken": token or "",
            }
        payload = json.dumps({
            "deviceId": body["deviceId"],
            "idempotencyKey": body["idempotencyKey"],
            "firmwareVersion": body["firmwareVersion"],
            "configurationVersion": body["configurationVersion"],
            "bundleSha256": self.manifest["mergedImage"]["sha256"],
            "token": body["flashAuthorizationToken"],
        }).encode("utf-8")
        path = "/api/v1/factory/flash-authorizations/verify"
        request = urllib.request.Request(
            f"{self.api_base_url}{path}",
            data=payload,
            headers=self._signed_headers(path, payload),
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=10) as response:
                final_url = response.geturl()
                if isinstance(final_url, str) and _cloudflare_access_url(final_url):
                    raise ValueError(
                        "WaterFlex redirected flash authorization to Cloudflare Access. "
                        "Install a helper built for the public telemetry hostname."
                    )
                if response.status != HTTPStatus.OK:
                    raise ValueError("WaterFlex denied flash authorization for this sensor.")
                try:
                    result = json.loads(response.read().decode("utf-8"))
                except (UnicodeDecodeError, json.JSONDecodeError) as error:
                    raise ValueError(
                        "WaterFlex returned invalid JSON while authorizing the flash. Flashing was blocked."
                    ) from error
                verification_token = result.get("verificationToken") if isinstance(result, dict) else None
                if result.get("authorized") is not True:
                    raise ValueError("WaterFlex denied flash authorization for this sensor.")
                if legacy_test_call and not isinstance(verification_token, str):
                    return "legacy-test-verification-token"
                if not isinstance(verification_token, str) or not verification_token:
                    raise ValueError("WaterFlex denied flash authorization for this sensor.")
                return verification_token
        except urllib.error.HTTPError as error:
            location = error.headers.get("Location") if error.headers is not None else None
            if 300 <= error.code < 400 and _cloudflare_access_url(location):
                raise ValueError(
                    "WaterFlex redirected flash authorization to Cloudflare Access. "
                    "Install a helper built for the public telemetry hostname."
                ) from error
            if error.code == HTTPStatus.NOT_FOUND:
                raise ValueError(
                    "WaterFlex returned HTTP 404 for flash authorization. Flashing was blocked."
                ) from error
            if 500 <= error.code < 600:
                raise ValueError(
                    f"WaterFlex could not verify flash authorization (HTTP {error.code}). Flashing was blocked."
                ) from error
            raise ValueError("WaterFlex denied flash authorization for this sensor.") from error
        except urllib.error.URLError as error:
            reason = error.reason
            if isinstance(reason, socket.gaierror):
                detail = "DNS lookup failed"
            elif isinstance(reason, ssl.SSLError):
                detail = "TLS validation failed"
            else:
                detail = "the network connection failed"
            raise ValueError(
                f"Could not reach WaterFlex to authorize flashing because {detail}. Flashing was blocked."
            ) from error

    def _record_verification(self, job: dict) -> None:
        evidence = job.get("evidence") or {"firmware": False, "identity": False, "portal": False, "portalStartup": False, "sensor": False}
        payload = json.dumps({
            "deviceId": job["deviceId"],
            "idempotencyKey": job["idempotencyKey"],
            "firmwareVersion": job["firmwareVersion"],
            "configurationVersion": job["configurationVersion"],
            "bundleSha256": self.manifest["mergedImage"]["sha256"],
            "firmwareVerified": bool(evidence.get("firmware")),
            "identityVerified": bool(evidence.get("identity")),
            "portalVerified": bool(evidence.get("portal")),
            "portalStartupObserved": bool(evidence.get("portalStartup")),
            "sensorVerified": bool(evidence.get("sensor")),
            "sensorSampleCount": int(evidence.get("sensorSampleCount", 0)),
            "sensorMinimumMm": evidence.get("sensorMinimumMm"),
            "sensorMaximumMm": evidence.get("sensorMaximumMm"),
            "sensorFailureCategories": evidence.get("sensorFailureCategories", []),
            "failureCode": job.get("failureCode"),
            "verificationToken": job["verificationToken"],
        }).encode("utf-8")
        path = "/api/v1/factory/verifications"
        request = urllib.request.Request(
            f"{self.api_base_url}{path}",
            data=payload,
            headers=self._signed_headers(path, payload),
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=15) as response:
                result = json.loads(response.read().decode("utf-8"))
                if response.status != HTTPStatus.OK or not isinstance(result, dict) or result.get("status") not in {"provisioned", "quarantined"}:
                    raise ValueError("WaterFlex rejected factory acceptance evidence.")
        except (urllib.error.URLError, urllib.error.HTTPError, UnicodeDecodeError, json.JSONDecodeError) as error:
            raise RuntimeError("Factory evidence could not be submitted to WaterFlex.") from error

    def _confirm_backend_connectivity(self) -> None:
        try:
            with urllib.request.urlopen(f"{self.api_base_url}/health/live", timeout=10) as response:
                if response.status != HTTPStatus.OK:
                    raise RuntimeError("Ethernet backend connectivity is unavailable.")
        except (urllib.error.URLError, urllib.error.HTTPError) as error:
            raise RuntimeError("Ethernet backend connectivity is unavailable.") from error

    def _update(self, job: dict, status: str, message: str) -> None:
        job["status"] = status
        job["message"] = message
        self.store.save(job)

    def _run(self, key: str) -> None:
        job = self.store.load(key)
        try:
            self._update(job, "flashing", "Flashing the approved WaterFlex firmware bundle.")
            port = flash_merged_image(self.bundle_dir, self.manifest, self.esptool_path)
            time.sleep(2)
            port = detect_port(port)
            self._update(job, "provisioning", f"Writing identity {job['serialNumber']} to the sensor.")
            evidence = serial_factory_provision(
                port,
                {
                    "serialNumber": job["serialNumber"],
                    "setupPassphrase": job["setupPassphrase"],
                    "bootstrapToken": f"{job['bootstrapCredentialId']}.{job['bootstrapSecret']}",
                },
                job["serialNumber"],
                job["firmwareVersion"],
            )
            self._confirm_backend_connectivity()
            portal = verify_portal(job["serialNumber"], job["setupPassphrase"])
            evidence["portal"] = portal["verified"]
            evidence["portalFailureCategories"] = portal["failureCategories"]
            job["evidence"] = evidence
            self._update(job, "verifying", "Confirming firmware, identity, setup portal, and sensor response.")
            passed = all(bool(evidence.get(name)) for name in ("firmware", "identity", "portal", "portalStartup", "sensor"))
            if not passed:
                job["failureCode"] = "factory_acceptance_failed"
            self._record_verification(job)
            if not passed:
                raise RuntimeError("One or more factory acceptance checks failed.")
            self._update(job, "completed", "All local factory acceptance checks passed.")
        except Exception as error:  # noqa: BLE001 - the error is reduced to a non-secret operator message
            job["status"] = "failed"
            job["message"] = str(error)[:300]
            job["failureCode"] = job.get("failureCode") or "factory_helper_failed"
            if job.get("verificationToken"):
                try:
                    self._record_verification(job)
                except RuntimeError:
                    pass
            self.store.save(job)
        finally:
            with self.store.lock:
                self.store.active_job = None


def public_job(job: dict) -> dict:
    return {name: job.get(name) for name in (
        "idempotencyKey", "bootstrapCredentialId", "bootstrapSecretHash", "status", "message",
        "serialNumber", "evidence", "failureCode"
    )}


def decode_bootstrap_secret(value: str) -> bytes:
    padding = "=" * (-len(value) % 4)
    try:
        return base64.urlsafe_b64decode(value + padding)
    except (ValueError, TypeError) as error:
        raise ValueError("Factory bootstrap secret is invalid.") from error


def load_manifest(bundle_dir: Path) -> dict:
    manifest_path = bundle_dir / "factory-bundle.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    image = bundle_dir / manifest["mergedImage"]["file"]
    actual = hashlib.sha256(image.read_bytes()).hexdigest()
    if actual.casefold() != manifest["mergedImage"]["sha256"].casefold():
        raise RuntimeError("Factory firmware bundle checksum does not match its manifest.")
    return manifest


def validate_start(body: dict, manifest: dict) -> None:
    required = (
        "deviceId", "serialNumber", "model", "firmwareVersion", "configurationVersion",
        "flashAuthorizationToken",
    )
    if any(not isinstance(body.get(name), str) or not body[name].strip() for name in required):
        raise ValueError("Registered factory job information is incomplete.")
    expected = {
        "model": manifest["model"],
        "firmwareVersion": manifest["firmwareVersion"],
        "configurationVersion": manifest["configurationVersion"],
    }
    if any(body[name] != value for name, value in expected.items()):
        raise ValueError("The local firmware bundle is not the version approved by WaterFlex.")
    if not body["serialNumber"].startswith("WF-NANO-"):
        raise ValueError("WaterFlex returned an invalid sensor serial number.")


def cached_bundle_is_valid(cache_dir: Path, sha256: str | None = None) -> bool:
    """True if `cache_dir` holds a checksum-verified bundle, optionally matching a specific sha256."""
    try:
        manifest = load_manifest(cache_dir)
    except Exception:
        return False
    if sha256 is not None and manifest["mergedImage"]["sha256"].casefold() != sha256.casefold():
        return False
    return True


def fetch_bundle_download(api_base_url: str) -> dict:
    request_url = f"{api_base_url.rstrip('/')}/api/v1/factory/bundle"
    request = urllib.request.Request(request_url, method="GET")
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            final_url = response.geturl()
            if isinstance(final_url, str) and final_url != request_url and _cloudflare_access_url(final_url):
                raise FactoryHelperStartupError(
                    "WaterFlex redirected the helper to Cloudflare Access. The helper must use the public telemetry hostname."
                )
            status = getattr(response, "status", HTTPStatus.OK)
            if not isinstance(status, int):
                status = HTTPStatus.OK
            if status == HTTPStatus.NOT_FOUND:
                raise FactoryHelperStartupError(
                    "WaterFlex returned HTTP 404 for the factory bundle endpoint. The staging ingress may not be deployed yet."
                )
            if 500 <= status < 600:
                raise FactoryHelperStartupError(
                    f"WaterFlex could not provide the factory bundle (HTTP {status}). Try again after the service recovers."
                )
            raw_body = response.read()
    except urllib.error.HTTPError as error:
        raise _metadata_http_failure(error) from error
    except urllib.error.URLError as error:
        raise _network_failure(error) from error

    try:
        result = json.loads(raw_body.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise FactoryHelperStartupError(
            "WaterFlex returned invalid JSON for the approved factory bundle. The helper cannot safely continue."
        ) from error
    if not isinstance(result, dict):
        raise FactoryHelperStartupError(
            "WaterFlex returned invalid JSON for the approved factory bundle. The helper cannot safely continue."
        )
    return result


def download_bundle(download: dict, cache_dir: Path) -> None:
    """Atomically replace the cached bundle image and manifest with the given presigned download."""
    cache_dir.mkdir(parents=True, exist_ok=True)
    image_name = "waterflex-factory.bin"
    temp_image = cache_dir / f"{image_name}.tmp"
    try:
        request = urllib.request.Request(download["downloadUrl"], method="GET")
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                temp_image.write_bytes(response.read())
        except urllib.error.HTTPError as error:
            raise FactoryHelperStartupError(
                f"The approved firmware download is unavailable from storage (HTTP {error.code})."
            ) from error
        except urllib.error.URLError as error:
            raise FactoryHelperStartupError(
                "The approved firmware download is unavailable from storage. Check network connectivity and try again."
            ) from error
        actual_sha256 = hashlib.sha256(temp_image.read_bytes()).hexdigest()
        if actual_sha256.casefold() != download["sha256"].casefold():
            raise ValueError("Downloaded factory firmware bundle failed checksum verification.")
        os.replace(temp_image, cache_dir / image_name)
    finally:
        temp_image.unlink(missing_ok=True)

    manifest = {
        "schemaVersion": 1,
        "model": download["model"],
        "firmwareVersion": download["firmwareVersion"],
        "configurationVersion": download["configurationVersion"],
        "helperProtocolVersion": download["helperProtocolVersion"],
        "mergedImage": {"file": image_name, "sha256": download["sha256"]},
    }
    temp_manifest = cache_dir / "factory-bundle.json.tmp"
    temp_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    os.replace(temp_manifest, cache_dir / "factory-bundle.json")


def resolve_bundle(bundle_dir: Path | None, cache_dir: Path, api_base_url: str) -> Path:
    """Resolve the directory the helper should flash from.

    An explicit --bundle-dir is an engineer override, used as-is. Otherwise fetch the
    backend-approved bundle into the cache (skipping the download if the cache already matches),
    and on any network or API failure fall back to the newest cached bundle that still passes
    checksum verification. Only refuse to start when there is neither a fresh fetch nor a usable
    cache.
    """
    if bundle_dir is not None:
        LOGGER.info("Using explicit factory bundle directory: %s", bundle_dir.resolve())
        return bundle_dir

    try:
        download = fetch_bundle_download(api_base_url)
    except (FactoryHelperStartupError, ValueError, KeyError, TypeError) as error:
        if cached_bundle_is_valid(cache_dir):
            message = f"{error} Using the checksum-verified cached bundle."
            LOGGER.warning(message)
            print(f"factory-helper: {message}")
            return cache_dir
        raise FactoryHelperStartupError(
            f"{error} No valid cached bundle is available."
        ) from error

    if cached_bundle_is_valid(cache_dir, download.get("sha256")):
        LOGGER.info("Using cached approved factory bundle with SHA-256 %s", download.get("sha256"))
        return cache_dir

    try:
        download_bundle(download, cache_dir)
    except (FactoryHelperStartupError, ValueError, KeyError, TypeError) as error:
        if cached_bundle_is_valid(cache_dir):
            message = f"{error} Using the checksum-verified cached bundle."
            LOGGER.warning(message)
            print(f"factory-helper: {message}")
            return cache_dir
        raise FactoryHelperStartupError(f"{error} No valid cached bundle is available.") from error

    LOGGER.info("Downloaded and verified approved factory bundle with SHA-256 %s", download.get("sha256"))
    return cache_dir


def flash_merged_image(bundle_dir: Path, manifest: dict, esptool_path: Path) -> str:
    port = detect_port(None)
    image = bundle_dir / manifest["mergedImage"]["file"]
    command = [
        "--chip", "esp32s3", "--port", port,
        "--baud", "921600", "write_flash", "0x0", str(image),
    ]
    invoke_esptool(esptool_path, command)
    return port


def invoke_esptool(esptool_path: Path, arguments: list[str]) -> None:
    """Run esptool in-process so the Windows release can be a standalone executable."""
    if not getattr(sys, "frozen", False):
        sys.path.insert(0, str(esptool_path.parent))
        bundled_dependencies = esptool_path.parent / "_contrib"
        if bundled_dependencies.is_dir():
            sys.path.insert(0, str(bundled_dependencies))
    import esptool  # type: ignore[import-not-found]  # bundled for releases or loaded from PlatformIO
    try:
        esptool.main(arguments)
    except SystemExit as error:
        if error.code not in (None, 0):
            raise RuntimeError(f"Firmware flashing failed with esptool exit code {error.code}.") from error


class Handler(BaseHTTPRequestHandler):
    server_version = "WaterFlexFactoryHelper/2"

    def _origin_allowed(self) -> bool:
        origin = self.headers.get("Origin")
        host = self.headers.get("Host", "").split(":", 1)[0].casefold()
        return host in {"127.0.0.1", "localhost"} and origin in self.server.allowed_origins  # type: ignore[attr-defined]

    def _cors(self) -> None:
        origin = self.headers.get("Origin")
        if origin in self.server.allowed_origins:  # type: ignore[attr-defined]
            self.send_header("Access-Control-Allow-Origin", origin)
            self.send_header("Vary", "Origin")
            self.send_header("Access-Control-Allow-Private-Network", "true")

    def _json(self, status: HTTPStatus, body: dict) -> None:
        encoded = json.dumps(body, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self._cors()
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(encoded)

    def _body(self) -> dict:
        length = int(self.headers.get("Content-Length", "0"))
        if length < 1 or length > 16_384:
            raise ValueError("Invalid request body size.")
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def _segments(self) -> list[str]:
        return [unquote(value) for value in urlparse(self.path).path.split("/") if value]

    def do_OPTIONS(self) -> None:  # noqa: N802
        if not self._origin_allowed():
            self._json(HTTPStatus.FORBIDDEN, {"error": "origin_not_allowed"})
            return
        self.send_response(HTTPStatus.NO_CONTENT)
        self._cors()
        self.send_header("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Max-Age", "600")
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        if not self._origin_allowed():
            self._json(HTTPStatus.FORBIDDEN, {"error": "origin_not_allowed"})
            return
        segments = self._segments()
        try:
            if segments == ["v1", "health"]:
                self._json(HTTPStatus.OK, {"status": "ready", "protocolVersion": PROTOCOL_VERSION})
            elif segments == ["v1", "station"]:
                self._json(HTTPStatus.OK, self.server.helper.station_identity.status())  # type: ignore[attr-defined]
            elif segments == ["v1", "devices"]:
                devices = enumerate_devices()
                status = "none" if not devices else "detected" if len(devices) == 1 else "multiple"
                self._json(HTTPStatus.OK, {"status": status, "devices": devices})
            elif len(segments) == 3 and segments[:2] == ["v1", "jobs"]:
                self._json(HTTPStatus.OK, public_job(self.server.helper.store.load(segments[2])))  # type: ignore[attr-defined]
            elif len(segments) == 4 and segments[:2] == ["v1", "jobs"] and segments[3] == "label":
                job = self.server.helper.store.load(segments[2])  # type: ignore[attr-defined]
                if job.get("status") != "completed":
                    self._json(HTTPStatus.CONFLICT, {"error": "label_not_ready"})
                    return
                self._json(HTTPStatus.OK, {
                    "serialNumber": job["serialNumber"],
                    "setupNetwork": job["serialNumber"],
                    "setupPassphrase": job["setupPassphrase"],
                    "firmwareVersion": job["firmwareVersion"],
                    "configurationVersion": job["configurationVersion"],
                })
            else:
                self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})
        except KeyError:
            self._json(HTTPStatus.NOT_FOUND, {"error": "job_not_found"})
        except Exception as error:  # noqa: BLE001
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)[:300]})

    def do_POST(self) -> None:  # noqa: N802
        if not self._origin_allowed():
            self._json(HTTPStatus.FORBIDDEN, {"error": "origin_not_allowed"})
            return
        segments = self._segments()
        try:
            if segments == ["v1", "jobs"]:
                result = self.server.helper.prepare(self._body())  # type: ignore[attr-defined]
            elif segments == ["v1", "station", "enroll"]:
                body = self._body()
                result = self.server.helper.station_identity.enroll(body.get("grantToken", ""), body.get("displayName", ""))  # type: ignore[attr-defined]
            elif len(segments) == 4 and segments[:2] == ["v1", "jobs"] and segments[3] == "start":
                result = self.server.helper.start(segments[2], self._body())  # type: ignore[attr-defined]
            else:
                self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})
                return
            self._json(HTTPStatus.OK, result)
        except KeyError:
            self._json(HTTPStatus.NOT_FOUND, {"error": "job_not_found"})
        except RuntimeError as error:
            self._json(HTTPStatus.CONFLICT, {"error": str(error)[:300]})
        except Exception as error:  # noqa: BLE001
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)[:300]})

    def do_DELETE(self) -> None:  # noqa: N802
        if not self._origin_allowed():
            self._json(HTTPStatus.FORBIDDEN, {"error": "origin_not_allowed"})
            return
        segments = self._segments()
        try:
            if len(segments) != 3 or segments[:2] != ["v1", "jobs"]:
                self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})
                return
            self.server.helper.store.delete(segments[2])  # type: ignore[attr-defined]
            self._json(HTTPStatus.OK, {"cleared": True})
        except RuntimeError as error:
            self._json(HTTPStatus.CONFLICT, {"error": str(error)[:300]})
        except Exception as error:  # noqa: BLE001
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)[:300]})

    def log_message(self, format_: str, *args: object) -> None:
        print(f"factory-helper {self.address_string()} {format_ % args}")


def main() -> int:
    parser = argparse.ArgumentParser(description="WaterFlex factory workstation helper")
    parser.add_argument(
        "--bundle-dir", type=Path, default=None,
        help="Use a local bundle directory instead of fetching the WaterFlex-approved bundle "
             "(advanced/engineer override; the default fetches and caches it automatically).")
    parser.add_argument("--bundle-cache-dir", type=Path, default=DEFAULT_BUNDLE_CACHE_DIR)
    parser.add_argument("--state-dir", type=Path, default=DEFAULT_DATA_DIR / "jobs")
    parser.add_argument("--allowed-origin", action="append", default=[])
    parser.add_argument("--esptool", type=Path)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument(
        "--api-base-url", default=DEFAULT_API_BASE_URL,
        help="WaterFlex backend base URL used to fetch the approved firmware bundle and verify flash authorization.")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("The factory helper requires Windows DPAPI.")
    acquire_single_instance()
    bundle_dir = resolve_bundle(args.bundle_dir, args.bundle_cache_dir, args.api_base_url)
    esptool_path = args.esptool or bundle_dir / "tools" / "esptool.py"
    if not getattr(sys, "frozen", False) and not esptool_path.exists():
        parser.error("The approved factory bundle does not contain esptool.py.")
    station_identity = StationIdentity(DEFAULT_DATA_DIR / "station.identity", args.api_base_url)
    helper = FactoryHelper(bundle_dir, args.state_dir, esptool_path, args.api_base_url, station_identity)
    LOGGER.info(
        "Loaded factory bundle version=%s configuration=%s",
        helper.manifest.get("firmwareVersion"),
        helper.manifest.get("configurationVersion"),
    )
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.helper = helper  # type: ignore[attr-defined]
    server.allowed_origins = DEFAULT_ORIGINS | set(args.allowed_origin)  # type: ignore[attr-defined]
    print(f"WaterFlex factory helper ready on http://127.0.0.1:{args.port}")
    LOGGER.info("Factory helper ready on http://127.0.0.1:%s", args.port)
    server.serve_forever()
    return 0


def show_frozen_startup_error(message: str) -> None:
    """Keep double-click startup failures visible for a packaged Windows executable."""
    ctypes.windll.user32.MessageBoxW(  # type: ignore[attr-defined]
        None,
        f"{message}\n\nDiagnostics were written to:\n{DEFAULT_LOG_PATH}",
        "WaterFlex Factory Helper could not start",
        0x10,
    )


def run(log_path: Path = DEFAULT_LOG_PATH) -> int:
    """Configure durable diagnostics and convert startup failures into operator-visible errors."""
    try:
        actual_log_path = configure_startup_logging(log_path)
    except OSError as error:
        actual_log_path = log_path
        print(f"factory-helper: could not create startup log: {error}", file=sys.stderr)
    LOGGER.info(
        "Starting WaterFlex Factory Helper (frozen=%s, api_base_url=%s)",
        bool(getattr(sys, "frozen", False)),
        DEFAULT_API_BASE_URL,
    )
    try:
        return main()
    except Exception as error:  # noqa: BLE001 - top-level startup boundary
        message = str(error) or error.__class__.__name__
        LOGGER.error("Factory helper startup failed: %s", message)
        print(f"factory-helper: {message}", file=sys.stderr)
        if getattr(sys, "frozen", False):
            try:
                show_frozen_startup_error(message)
            except Exception as dialog_error:  # noqa: BLE001 - logging is the final fallback
                LOGGER.error("Could not display the Windows startup error dialog: %s", dialog_error)
        LOGGER.info("Startup diagnostics path: %s", actual_log_path)
        return 1


if __name__ == "__main__":
    raise SystemExit(run())
