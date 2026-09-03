from __future__ import annotations

import base64
import hashlib
import json
import tempfile
import unittest
import os
import socket
import ssl
import threading
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path
from unittest.mock import MagicMock, patch

from factory_helper import (
    FactoryHelper,
    Handler,
    LOGGER,
    configure_startup_logging,
    public_job,
    resolve_bundle,
    run,
)


class FactoryHelperTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        image = self.root / "waterflex-factory.bin"
        image.write_bytes(b"approved firmware")
        (self.root / "factory-bundle.json").write_text(json.dumps({
            "schemaVersion": 1,
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.1",
            "configurationVersion": "factory-v2",
            "helperProtocolVersion": "2",
            "mergedImage": {"file": image.name, "sha256": hashlib.sha256(image.read_bytes()).hexdigest()},
        }), encoding="utf-8")
        self.esptool = self.root / "esptool.py"
        self.esptool.write_text("", encoding="utf-8")
        self.helper = FactoryHelper(self.root, self.root / "jobs", self.esptool, "http://127.0.0.1:9")

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @unittest.skipUnless(os.name == "nt", "DPAPI is available only on Windows")
    def test_prepared_job_is_dpapi_protected_and_public_view_is_redacted(self) -> None:
        bootstrap_secret = base64.urlsafe_b64encode(bytes(range(32))).decode("ascii").rstrip("=")
        result = self.helper.prepare({
            "idempotencyKey": "factory-test-job-0001",
            "bootstrapCredentialId": "wf_boot_test_0001",
            "bootstrapSecret": bootstrap_secret,
            "setupPassphrase": "setup-value",
        })

        encrypted = (self.root / "jobs" / "factory-test-job-0001.job").read_bytes()
        self.assertNotIn(bootstrap_secret.encode("ascii"), encrypted)
        self.assertNotIn("bootstrapSecret", result)
        self.assertEqual("wf_boot_test_0001", result["bootstrapCredentialId"])
        self.assertEqual(
            base64.b64encode(hashlib.sha256(bytes(range(32))).digest()).decode("ascii"),
            result["bootstrapSecretHash"],
        )
        self.assertEqual("prepared", result["status"])

    @unittest.skipUnless(os.name == "nt", "DPAPI is available only on Windows")
    def test_interrupted_job_is_failed_when_helper_restarts(self) -> None:
        bootstrap_secret = base64.urlsafe_b64encode(bytes(range(32))).decode("ascii").rstrip("=")
        self.helper.prepare({
            "idempotencyKey": "factory-interrupted-job-0001",
            "bootstrapCredentialId": "wf_boot_interrupted_0001",
            "bootstrapSecret": bootstrap_secret,
            "setupPassphrase": "setup-value",
        })
        job = self.helper.store.load("factory-interrupted-job-0001")
        job["status"] = "flashing"
        self.helper.store.save(job)

        restarted = FactoryHelper(self.root, self.root / "jobs", self.esptool, "http://127.0.0.1:9")
        recovered = restarted.store.load("factory-interrupted-job-0001")

        self.assertEqual("failed", recovered["status"])
        self.assertEqual("factory_helper_interrupted", recovered["failureCode"])

    def test_manifest_checksum_is_required(self) -> None:
        (self.root / "waterflex-factory.bin").write_bytes(b"tampered")
        with self.assertRaisesRegex(RuntimeError, "checksum"):
            FactoryHelper(self.root, self.root / "other-jobs", self.esptool, "http://127.0.0.1:9")

    @unittest.skipUnless(os.name == "nt", "DPAPI is available only on Windows")
    def test_start_blocks_the_flash_when_backend_denies_the_authorization_token(self) -> None:
        self._prepare_job("factory-denied-job-0001")

        with patch("factory_helper.urllib.request.urlopen", side_effect=urllib.error.HTTPError(
            "url", 403, "Forbidden", None, None,
        )):
            with self.assertRaisesRegex(ValueError, "denied"):
                self.helper.start("factory-denied-job-0001", self._start_body())

    @unittest.skipUnless(os.name == "nt", "DPAPI is available only on Windows")
    def test_start_blocks_the_flash_when_backend_is_unreachable(self) -> None:
        self._prepare_job("factory-unreachable-job-0001")

        with patch("factory_helper.urllib.request.urlopen", side_effect=urllib.error.URLError("no route")):
            with self.assertRaisesRegex(ValueError, "reach WaterFlex"):
                self.helper.start("factory-unreachable-job-0001", self._start_body())

    def test_verify_flash_authorization_succeeds_when_backend_grants_the_token(self) -> None:
        with patch("factory_helper.urllib.request.urlopen") as mock_urlopen:
            mock_urlopen.return_value.__enter__.return_value.status = 200
            mock_urlopen.return_value.__enter__.return_value.geturl.return_value = (
                "http://127.0.0.1:9/api/v1/factory/flash-authorizations/verify"
            )
            mock_urlopen.return_value.__enter__.return_value.read.return_value = b'{"authorized":true}'
            self.helper._verify_flash_authorization(  # noqa: SLF001 - exercising the fail-closed check directly
                "11111111-1111-1111-1111-111111111111", "wf_flash_test.not-a-real-secret",
            )

    def test_verify_flash_authorization_blocks_cloudflare_access_redirect(self) -> None:
        with patch("factory_helper.urllib.request.urlopen") as mock_urlopen:
            mock_urlopen.return_value.__enter__.return_value.status = 200
            mock_urlopen.return_value.__enter__.return_value.geturl.return_value = (
                "https://example.cloudflareaccess.com/cdn-cgi/access/login"
            )
            with self.assertRaisesRegex(ValueError, "Cloudflare Access"):
                self.helper._verify_flash_authorization(  # noqa: SLF001
                    "11111111-1111-1111-1111-111111111111", "wf_flash_test.not-a-real-secret",
                )

    def _prepare_job(self, idempotency_key: str) -> None:
        bootstrap_secret = base64.urlsafe_b64encode(bytes(range(32))).decode("ascii").rstrip("=")
        self.helper.prepare({
            "idempotencyKey": idempotency_key,
            "bootstrapCredentialId": "wf_boot_test_0001",
            "bootstrapSecret": bootstrap_secret,
            "setupPassphrase": "setup-value",
        })

    @staticmethod
    def _start_body() -> dict:
        return {
            "deviceId": "11111111-1111-1111-1111-111111111111",
            "serialNumber": "WF-NANO-0001",
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.1",
            "configurationVersion": "factory-v2",
            "flashAuthorizationToken": "wf_flash_test.not-a-real-secret",
        }

    def test_public_job_never_returns_credentials(self) -> None:
        view = public_job({
            "idempotencyKey": "job",
            "status": "prepared",
            "message": "ready",
            "bootstrapSecret": "secret",
            "setupPassphrase": "passphrase",
        })
        self.assertNotIn("bootstrapSecret", view)
        self.assertNotIn("setupPassphrase", view)


class ResolveBundleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.cache_dir = self.root / "cache"
        self.override_dir = self.root / "override"
        self.override_dir.mkdir()

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def _write_cached_bundle(cache_dir: Path, content: bytes = b"cached firmware") -> str:
        cache_dir.mkdir(parents=True, exist_ok=True)
        image = cache_dir / "waterflex-factory.bin"
        image.write_bytes(content)
        sha256 = hashlib.sha256(content).hexdigest()
        (cache_dir / "factory-bundle.json").write_text(json.dumps({
            "schemaVersion": 1,
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.1",
            "configurationVersion": "factory-v2",
            "helperProtocolVersion": "2",
            "mergedImage": {"file": image.name, "sha256": sha256},
        }), encoding="utf-8")
        return sha256

    def test_explicit_bundle_dir_is_used_as_is_without_contacting_the_backend(self) -> None:
        with patch("factory_helper.urllib.request.urlopen") as mock_urlopen:
            result = resolve_bundle(self.override_dir, self.cache_dir, "http://127.0.0.1:9")
        mock_urlopen.assert_not_called()
        self.assertEqual(self.override_dir, result)

    def test_matching_cache_is_reused_without_downloading_the_image(self) -> None:
        sha256 = self._write_cached_bundle(self.cache_dir)
        download_response = json.dumps({
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.1",
            "configurationVersion": "factory-v2",
            "helperProtocolVersion": "2",
            "downloadUrl": "http://example.invalid/should-not-be-fetched",
            "sha256": sha256,
        }).encode("utf-8")

        with patch("factory_helper.urllib.request.urlopen") as mock_urlopen:
            mock_urlopen.return_value.__enter__.return_value.read.return_value = download_response
            result = resolve_bundle(None, self.cache_dir, "http://127.0.0.1:9")

        self.assertEqual(1, mock_urlopen.call_count)  # only the /bundle metadata call, not an image download
        self.assertEqual(self.cache_dir, result)

    def test_stale_cache_is_replaced_by_a_fresh_download(self) -> None:
        self._write_cached_bundle(self.cache_dir, content=b"old firmware")
        new_content = b"new firmware"
        new_sha256 = hashlib.sha256(new_content).hexdigest()
        bundle_response = json.dumps({
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.2",
            "configurationVersion": "factory-v2",
            "helperProtocolVersion": "2",
            "downloadUrl": "http://example.invalid/waterflex-factory.bin",
            "sha256": new_sha256,
        }).encode("utf-8")

        responses = [bundle_response, new_content]

        def fake_urlopen(*_args, **_kwargs):
            context = unittest.mock.MagicMock()
            context.__enter__.return_value.read.return_value = responses.pop(0)
            return context

        with patch("factory_helper.urllib.request.urlopen", side_effect=fake_urlopen):
            result = resolve_bundle(None, self.cache_dir, "http://127.0.0.1:9")

        self.assertEqual(self.cache_dir, result)
        self.assertEqual(new_content, (self.cache_dir / "waterflex-factory.bin").read_bytes())

    def test_checksum_mismatch_on_download_falls_back_to_the_existing_cache(self) -> None:
        self._write_cached_bundle(self.cache_dir, content=b"good cached firmware")
        bundle_response = json.dumps({
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.2",
            "configurationVersion": "factory-v2",
            "helperProtocolVersion": "2",
            "downloadUrl": "http://example.invalid/waterflex-factory.bin",
            "sha256": "0" * 64,  # will never match the downloaded bytes below
        }).encode("utf-8")
        responses = [bundle_response, b"corrupted in transit"]

        def fake_urlopen(*_args, **_kwargs):
            context = unittest.mock.MagicMock()
            context.__enter__.return_value.read.return_value = responses.pop(0)
            return context

        with patch("factory_helper.urllib.request.urlopen", side_effect=fake_urlopen):
            result = resolve_bundle(None, self.cache_dir, "http://127.0.0.1:9")

        self.assertEqual(self.cache_dir, result)
        self.assertEqual(b"good cached firmware", (self.cache_dir / "waterflex-factory.bin").read_bytes())

    def test_backend_unreachable_falls_back_to_a_valid_cache(self) -> None:
        self._write_cached_bundle(self.cache_dir)

        with patch("factory_helper.urllib.request.urlopen", side_effect=urllib.error.URLError("no route")):
            result = resolve_bundle(None, self.cache_dir, "http://127.0.0.1:9")

        self.assertEqual(self.cache_dir, result)

    def test_backend_unreachable_with_no_cache_raises(self) -> None:
        with patch(
            "factory_helper.urllib.request.urlopen",
            side_effect=urllib.error.URLError(socket.gaierror("host not found")),
        ):
            with self.assertRaisesRegex(RuntimeError, "DNS lookup failed"):
                resolve_bundle(None, self.cache_dir, "http://127.0.0.1:9")

    def test_tls_failure_with_no_cache_is_reported_separately(self) -> None:
        with patch(
            "factory_helper.urllib.request.urlopen",
            side_effect=urllib.error.URLError(ssl.SSLError("certificate verify failed")),
        ):
            with self.assertRaisesRegex(RuntimeError, "TLS validation failed"):
                resolve_bundle(None, self.cache_dir, "https://telemetry-staging.saltmonitor.dev")

    def test_cloudflare_access_redirect_is_reported_separately(self) -> None:
        response = MagicMock()
        response.__enter__.return_value.geturl.return_value = (
            "https://example.cloudflareaccess.com/cdn-cgi/access/login"
        )
        with patch("factory_helper.urllib.request.urlopen", return_value=response):
            with self.assertRaisesRegex(RuntimeError, "redirected.*Cloudflare Access"):
                resolve_bundle(None, self.cache_dir, "https://console-staging.saltmonitor.dev")

    def test_bundle_endpoint_404_is_reported_separately(self) -> None:
        error = urllib.error.HTTPError("url", 404, "Not Found", {}, None)
        with patch("factory_helper.urllib.request.urlopen", side_effect=error):
            with self.assertRaisesRegex(RuntimeError, "HTTP 404"):
                resolve_bundle(None, self.cache_dir, "https://telemetry-staging.saltmonitor.dev")

    def test_bundle_endpoint_5xx_is_reported_separately(self) -> None:
        error = urllib.error.HTTPError("url", 503, "Unavailable", {}, None)
        with patch("factory_helper.urllib.request.urlopen", side_effect=error):
            with self.assertRaisesRegex(RuntimeError, "HTTP 503"):
                resolve_bundle(None, self.cache_dir, "https://telemetry-staging.saltmonitor.dev")

    def test_invalid_bundle_json_is_reported_separately(self) -> None:
        response = MagicMock()
        response.__enter__.return_value.geturl.return_value = (
            "https://telemetry-staging.saltmonitor.dev/api/v1/factory/bundle"
        )
        response.__enter__.return_value.status = 200
        response.__enter__.return_value.read.return_value = b"<html>not json</html>"
        with patch("factory_helper.urllib.request.urlopen", return_value=response):
            with self.assertRaisesRegex(RuntimeError, "invalid JSON"):
                resolve_bundle(None, self.cache_dir, "https://telemetry-staging.saltmonitor.dev")

    def test_unavailable_s3_download_is_reported_separately(self) -> None:
        metadata = json.dumps({
            "model": "Arduino Nano ESP32",
            "firmwareVersion": "wf-uart-pilot-0.2",
            "configurationVersion": "factory-v2",
            "helperProtocolVersion": "2",
            "downloadUrl": "https://example.invalid/waterflex-factory.bin",
            "sha256": "0" * 64,
        }).encode("utf-8")
        metadata_response = MagicMock()
        metadata_response.__enter__.return_value.geturl.return_value = (
            "https://telemetry-staging.saltmonitor.dev/api/v1/factory/bundle"
        )
        metadata_response.__enter__.return_value.status = 200
        metadata_response.__enter__.return_value.read.return_value = metadata
        unavailable = urllib.error.HTTPError("url", 503, "Unavailable", {}, None)
        with patch(
            "factory_helper.urllib.request.urlopen",
            side_effect=[metadata_response, unavailable],
        ):
            with self.assertRaisesRegex(RuntimeError, "unavailable from storage.*HTTP 503"):
                resolve_bundle(None, self.cache_dir, "https://telemetry-staging.saltmonitor.dev")


class StartupDiagnosticsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self) -> None:
        for handler in list(LOGGER.handlers):
            handler.close()
            LOGGER.removeHandler(handler)
        self.temporary.cleanup()

    def test_startup_log_is_created(self) -> None:
        log_path = self.root / "WaterFlex" / "FactoryHelper" / "factory-helper.log"
        configure_startup_logging(log_path)

        self.assertTrue(log_path.is_file())

    def test_frozen_exe_startup_failure_displays_windows_error(self) -> None:
        log_path = self.root / "factory-helper.log"
        with (
            patch("factory_helper.main", side_effect=RuntimeError("startup exploded")),
            patch("factory_helper.show_frozen_startup_error") as show_dialog,
            patch("factory_helper.sys.frozen", True, create=True),
        ):
            result = run(log_path)

        self.assertEqual(1, result)
        show_dialog.assert_called_once_with("startup exploded")
        self.assertIn("startup exploded", log_path.read_text(encoding="utf-8"))


class DeviceEndpointTests(unittest.TestCase):
    @staticmethod
    def _request(origin: str, host: str = "127.0.0.1") -> tuple[int, dict, dict]:
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        server.allowed_origins = {"https://console-staging.saltmonitor.dev"}  # type: ignore[attr-defined]
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            request = urllib.request.Request(
                f"http://127.0.0.1:{server.server_port}/v1/devices",
                headers={"Origin": origin, "Host": host},
            )
            try:
                with urllib.request.urlopen(request, timeout=2) as response:
                    return response.status, json.loads(response.read()), dict(response.headers)
            except urllib.error.HTTPError as error:
                return error.code, json.loads(error.read()), dict(error.headers)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)

    def test_devices_endpoint_returns_none_detected_and_multiple(self) -> None:
        cases = [
            ([], "none"),
            ([{"port": "COM4", "description": "Arduino Nano ESP32"}], "detected"),
            ([
                {"port": "COM4", "description": "Arduino Nano ESP32"},
                {"port": "COM7", "description": "USB JTAG/serial debug unit"},
            ], "multiple"),
        ]
        for devices, expected_status in cases:
            with self.subTest(expected_status), patch("factory_helper.enumerate_devices", return_value=devices):
                status, body, headers = self._request("https://console-staging.saltmonitor.dev")
            self.assertEqual(200, status)
            self.assertEqual({"status": expected_status, "devices": devices}, body)
            self.assertEqual("https://console-staging.saltmonitor.dev", headers["Access-Control-Allow-Origin"])

    def test_devices_endpoint_rejects_unapproved_origin(self) -> None:
        with patch("factory_helper.enumerate_devices") as enumerate_mock:
            status, body, _ = self._request("https://evil.example")
        self.assertEqual(403, status)
        self.assertEqual({"error": "origin_not_allowed"}, body)
        enumerate_mock.assert_not_called()

    def test_devices_endpoint_rejects_non_loopback_host(self) -> None:
        with patch("factory_helper.enumerate_devices") as enumerate_mock:
            status, body, _ = self._request("https://console-staging.saltmonitor.dev", "attacker.example")
        self.assertEqual(403, status)
        self.assertEqual({"error": "origin_not_allowed"}, body)
        enumerate_mock.assert_not_called()


if __name__ == "__main__":
    unittest.main()
