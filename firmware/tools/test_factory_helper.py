from __future__ import annotations

import base64
import hashlib
import json
import tempfile
import unittest
import os
from pathlib import Path

from factory_helper import FactoryHelper, public_job


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
            "helperProtocolVersion": "1",
            "mergedImage": {"file": image.name, "sha256": hashlib.sha256(image.read_bytes()).hexdigest()},
        }), encoding="utf-8")
        self.esptool = self.root / "esptool.py"
        self.esptool.write_text("", encoding="utf-8")
        self.helper = FactoryHelper(self.root, self.root / "jobs", self.esptool)

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

        restarted = FactoryHelper(self.root, self.root / "jobs", self.esptool)
        recovered = restarted.store.load("factory-interrupted-job-0001")

        self.assertEqual("failed", recovered["status"])
        self.assertEqual("factory_helper_interrupted", recovered["failureCode"])

    def test_manifest_checksum_is_required(self) -> None:
        (self.root / "waterflex-factory.bin").write_bytes(b"tampered")
        with self.assertRaisesRegex(RuntimeError, "checksum"):
            FactoryHelper(self.root, self.root / "other-jobs", self.esptool)

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


if __name__ == "__main__":
    unittest.main()
