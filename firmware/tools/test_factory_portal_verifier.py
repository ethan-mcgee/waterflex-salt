from __future__ import annotations

import unittest
from unittest.mock import patch

from factory_portal_verifier import verify_portal


class FakeWlan:
    last = None
    def __init__(self): self.calls = []; FakeWlan.last = self
    def interface(self): return object(), "Wi-Fi"
    def set_profile(self, guid, profile, passphrase): self.calls.append(("set", profile, passphrase))
    def connect(self, guid, profile): self.calls.append(("connect", profile))
    def disconnect(self, guid): self.calls.append(("disconnect",))
    def delete_profile(self, guid, profile): self.calls.append(("delete", profile))
    def close(self): self.calls.append(("close",))


class FakeSocket:
    def __enter__(self): return self
    def __exit__(self, *_): return False
    def settimeout(self, _): pass
    def bind(self, address): self.bound = address
    def connect(self, address): self.destination = address
    def sendall(self, _): pass
    def recv(self, _):
        if hasattr(self, "sent"): return b""
        self.sent = True
        return b'HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n<meta name="waterflex-portal" content="setup-v1">'


class PortalVerifierTests(unittest.TestCase):
    def test_binds_to_wifi_address_and_restores_previous_profile(self):
        states = iter([
            {"ethernetUp": True, "ipv4": [], "profile": "Office WiFi"},
            {"ethernetUp": True, "ipv4": ["192.168.4.2"], "profile": "WF-NANO-0042"},
        ])
        fake_socket = FakeSocket()
        with patch("factory_portal_verifier.socket.socket", return_value=fake_socket):
            result = verify_portal("WF-NANO-0042", "not-logged-secret", wlan_factory=FakeWlan, state_reader=lambda _: next(states))
        self.assertTrue(result["verified"])
        self.assertEqual(("192.168.4.2", 0), fake_socket.bound)
        self.assertIn(("delete", "WF-NANO-0042"), FakeWlan.last.calls)
        self.assertIn(("connect", "Office WiFi"), FakeWlan.last.calls)

    def test_cleanup_failure_quarantines(self):
        class BrokenCleanup(FakeWlan):
            def delete_profile(self, guid, profile): raise RuntimeError("cleanup")
        states = iter([{"ethernetUp": True, "ipv4": [], "profile": None}, {"ethernetUp": True, "ipv4": ["192.168.4.2"], "profile": "WF-NANO-0042"}])
        with patch("factory_portal_verifier.socket.socket", return_value=FakeSocket()):
            result = verify_portal("WF-NANO-0042", "not-logged-secret", wlan_factory=BrokenCleanup, state_reader=lambda _: next(states))
        self.assertFalse(result["verified"])
        self.assertIn("wlan_cleanup_profile_delete_failed", result["failureCategories"])


if __name__ == "__main__": unittest.main()
