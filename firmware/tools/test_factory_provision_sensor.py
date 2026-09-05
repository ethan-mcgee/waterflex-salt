from __future__ import annotations

import unittest
from types import SimpleNamespace
from unittest.mock import patch

from factory_provision_sensor import detect_port, enumerate_devices, serial_factory_provision


def serial_port(
    device: str,
    description: str = "",
    manufacturer: str = "",
    vid: int | None = None,
    pid: int | None = None,
):
    return SimpleNamespace(device=device, description=description, manufacturer=manufacturer, vid=vid, pid=pid)


class DeviceEnumerationTests(unittest.TestCase):
    def test_zero_matching_ports_ignores_unrelated_serial_devices(self) -> None:
        ports = [serial_port("COM2", "Standard Bluetooth link", "Microsoft")]
        self.assertEqual([], enumerate_devices(ports))

    def test_one_matching_port_includes_port_and_description(self) -> None:
        ports = [serial_port("COM4", "Arduino Nano ESP32", "Arduino LLC")]
        with patch("factory_provision_sensor.serial.Serial") as serial_mock:
            self.assertEqual(
                [{"port": "COM4", "description": "Arduino Nano ESP32"}],
                enumerate_devices(ports),
            )
        serial_mock.assert_not_called()

    def test_multiple_descriptor_variants_are_matched(self) -> None:
        ports = [
            serial_port("COM4", "Arduino Nano ESP32"),
            serial_port("COM7", "USB JTAG/serial debug unit", "Espressif"),
        ]
        self.assertEqual(["COM4", "COM7"], [device["port"] for device in enumerate_devices(ports)])

    def test_nano_application_usb_id_matches_windows_generic_description(self) -> None:
        ports = [serial_port("COM4", "USB Serial Device (COM4)", "Microsoft", 0x2341, 0x0070)]
        self.assertEqual(
            [{"port": "COM4", "description": "USB Serial Device (COM4)"}],
            enumerate_devices(ports),
        )

    def test_nano_bootloader_usb_id_is_also_matched(self) -> None:
        ports = [serial_port("COM3", "USB JTAG/serial debug unit", "Microsoft", 0x303A, 0x1001)]
        self.assertEqual(["COM3"], [device["port"] for device in enumerate_devices(ports)])

    def test_detect_port_still_requires_exactly_one_candidate_at_flash_time(self) -> None:
        with patch("factory_provision_sensor.list_ports.comports", return_value=[
            serial_port("COM4", "Arduino Nano ESP32"),
            serial_port("COM7", "ESP32 USB JTAG"),
        ]):
            with self.assertRaisesRegex(RuntimeError, "Expected exactly one"):
                detect_port(None)

    def test_detect_port_preserves_an_explicit_port_that_still_exists(self) -> None:
        with patch("factory_provision_sensor.list_ports.comports", return_value=[
            serial_port("COM9", "Generic USB serial device"),
        ]):
            self.assertEqual("com9", detect_port("com9"))


class SerialAcceptanceTests(unittest.TestCase):
    def test_five_in_range_samples_pass_without_stability_tolerance(self) -> None:
        connection = FakeSerial([
            'factory_status={"serialNumber":"WF-NANO-0042","firmwareVersion":"1.0.0","portalRunning":true}',
            "distance=30 mm", "distance=4500 mm", "distance=100 mm", "distance=3000 mm", "distance=900 mm",
        ])
        with patch("factory_provision_sensor.serial.Serial", return_value=connection), patch("factory_provision_sensor.time.sleep"):
            evidence = serial_factory_provision("COM4", {}, "WF-NANO-0042", "1.0.0")
        self.assertTrue(evidence["sensor"])
        self.assertEqual(5, evidence["sensorSampleCount"])
        self.assertEqual(30, evidence["sensorMinimumMm"])
        self.assertEqual(4500, evidence["sensorMaximumMm"])

    def test_out_of_range_and_insufficient_samples_quarantine(self) -> None:
        connection = FakeSerial([
            'factory_status={"serialNumber":"WF-NANO-0042","firmwareVersion":"1.0.0","portalRunning":true}',
            "distance=29 mm", "distance=100 mm", "distance=4501 mm",
        ])
        ticks = iter(range(100))
        with patch("factory_provision_sensor.serial.Serial", return_value=connection), patch("factory_provision_sensor.time.sleep"), patch("factory_provision_sensor.time.monotonic", side_effect=lambda: next(ticks)):
            evidence = serial_factory_provision("COM4", {}, "WF-NANO-0042", "1.0.0")
        self.assertFalse(evidence["sensor"])
        self.assertEqual(1, evidence["sensorSampleCount"])
        self.assertIn("out_of_range", evidence["sensorFailureCategories"])
        self.assertIn("insufficient_samples", evidence["sensorFailureCategories"])


class FakeSerial:
    def __init__(self, lines): self.lines = [f"{line}\n".encode() for line in lines]
    def __enter__(self): return self
    def __exit__(self, *_): return False
    def write(self, _): pass
    def readline(self): return self.lines.pop(0) if self.lines else b""


if __name__ == "__main__":
    unittest.main()
