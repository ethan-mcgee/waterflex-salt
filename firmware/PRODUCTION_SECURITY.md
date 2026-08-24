# ESP32-S3 production security gate

The pilot source defaults to HTTPS-only WaterFlex ingress and a factory-injected setup secret. Irreversible ESP32
eFuses are a controlled manufacturing operation, not part of `pio run` or ordinary USB upload.

## Required release evidence

1. Preserve the exact signed firmware binary, partition table, bootloader, source commit, signing-key identifier, and
   SHA-256 hashes in the release record.
2. Inject a unique random setup passphrase and bootstrap credential per device. Store only server-side hashes; keep
   plaintext factory material in the approved manufacturing secret system and never in Git or build logs.
3. On a sacrificial Nano ESP32, read and archive the current state without writing anything:

   ```powershell
   python -m espefuse --port COM4 summary
   python -m esptool --port COM4 flash_id
   ```

4. Validate encrypted NVS/flash, signed boot, recovery/rollback, credential rotation, and an authorized reflash on the
   sacrificial unit. Repeat on one canary from the actual manufacturing batch.
5. A second operator compares the proposed eFuse commands with Espressif's documentation for the exact ESP32-S3
   revision and approves the manufacturing work instruction.
6. Only the controlled factory station may burn eFuses. Never paste a generic eFuse command from this repository;
   the exact command depends on key blocks, chip revision, and the approved recovery policy.

## Pilot exception

Until this gate is signed off, pilot devices must remain physically controlled, inventoried, and recoverable. The
software protections in this repository do not substitute for secure boot or encrypted flash. A device that is lost,
tampered with, or leaves WaterFlex custody must have its operational credential revoked and be quarantined.
