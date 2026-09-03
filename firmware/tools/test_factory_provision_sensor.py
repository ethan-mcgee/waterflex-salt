from __future__ import annotations

import unittest
from types import SimpleNamespace
from unittest.mock import patch

from factory_provision_sensor import detect_port, enumerate_devices


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


if __name__ == "__main__":
    unittest.main()
