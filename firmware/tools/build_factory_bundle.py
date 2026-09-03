#!/usr/bin/env python3
"""Create a checksum-pinned factory bundle from the production PlatformIO build."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build-dir", type=Path, default=Path(".pio/build/arduino_nano_esp32_factory_production"))
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--firmware-version")
    parser.add_argument("--configuration-version", default="factory-v2")
    parser.add_argument("--esptool", type=Path)
    args = parser.parse_args()
    firmware_version = args.firmware_version or read_firmware_version(Path("src/config.h"))
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    images = [
        ("0x0", args.build_dir / "bootloader.bin"),
        ("0x8000", args.build_dir / "partitions.bin"),
        ("0x10000", args.build_dir / "firmware.bin"),
    ]
    missing = [str(path) for _, path in images if not path.exists()]
    if missing:
        raise SystemExit(f"Missing PlatformIO build artifacts: {', '.join(missing)}")
    merged = output / "waterflex-factory.bin"
    platformio_home = Path(os.environ.get("PLATFORMIO_CORE_DIR", Path.home() / ".platformio"))
    esptool_path = args.esptool or platformio_home / "packages" / "tool-esptoolpy" / "esptool.py"
    if not esptool_path.exists():
        raise SystemExit("PlatformIO esptool.py was not found; pass --esptool explicitly.")
    subprocess.run([
        sys.executable, str(esptool_path), "--chip", "esp32s3", "merge_bin", "-o", str(merged),
        *[value for pair in images for value in (pair[0], str(pair[1]))],
    ], check=True)
    manifest = {
        "schemaVersion": 1,
        "model": "Arduino Nano ESP32",
        "firmwareVersion": firmware_version,
        "configurationVersion": args.configuration_version,
        "helperProtocolVersion": "2",
        "mergedImage": {"file": merged.name, "sha256": hashlib.sha256(merged.read_bytes()).hexdigest()},
    }
    (output / "factory-bundle.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    for name in ("factory_helper.py", "factory_provision_sensor.py", "start-factory-helper.ps1"):
        shutil.copy2(Path(__file__).with_name(name), output / name)
    tools = output / "tools"
    tools.mkdir(exist_ok=True)
    shutil.copy2(esptool_path, tools / "esptool.py")
    shutil.copytree(esptool_path.parent / "esptool", tools / "esptool", dirs_exist_ok=True)
    shutil.copytree(esptool_path.parent / "_contrib", tools / "_contrib", dirs_exist_ok=True)
    return 0


def read_firmware_version(config_path: Path) -> str:
    match = re.search(r'kFirmwareVersion\[\]\s*=\s*"([^"]+)"', config_path.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit(f"Unable to read kFirmwareVersion from {config_path}.")
    return match.group(1)


if __name__ == "__main__":
    raise SystemExit(main())
