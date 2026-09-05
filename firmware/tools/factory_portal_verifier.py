"""External Windows WLAN and HTTP verification for a freshly provisioned Nano."""

from __future__ import annotations

import ctypes
import html
import ipaddress
import json
import os
import socket
import subprocess
import time
from ctypes import wintypes


class GUID(ctypes.Structure):
    _fields_ = [("Data1", wintypes.DWORD), ("Data2", wintypes.WORD), ("Data3", wintypes.WORD), ("Data4", ctypes.c_ubyte * 8)]


class WLAN_INTERFACE_INFO(ctypes.Structure):
    _fields_ = [("InterfaceGuid", GUID), ("strInterfaceDescription", wintypes.WCHAR * 256), ("isState", wintypes.DWORD)]


class WLAN_INTERFACE_INFO_LIST(ctypes.Structure):
    _fields_ = [("dwNumberOfItems", wintypes.DWORD), ("dwIndex", wintypes.DWORD), ("InterfaceInfo", WLAN_INTERFACE_INFO * 1)]


class WLAN_CONNECTION_PARAMETERS(ctypes.Structure):
    _fields_ = [("wlanConnectionMode", wintypes.DWORD), ("strProfile", wintypes.LPCWSTR), ("pDot11Ssid", ctypes.c_void_p), ("pDesiredBssidList", ctypes.c_void_p), ("dot11BssType", wintypes.DWORD), ("dwFlags", wintypes.DWORD)]


class WindowsWlan:
    def __init__(self):
        if os.name != "nt":
            raise RuntimeError("Portal verification requires Windows WLAN support.")
        self.api = ctypes.windll.wlanapi
        negotiated = wintypes.DWORD(); self.handle = wintypes.HANDLE()
        self._check(self.api.WlanOpenHandle(2, None, ctypes.byref(negotiated), ctypes.byref(self.handle)), "open WLAN service")

    @staticmethod
    def _check(code: int, action: str) -> None:
        if code != 0: raise RuntimeError(f"Could not {action} (Windows error {code}).")

    def interface(self) -> tuple[GUID, str]:
        pointer = ctypes.POINTER(WLAN_INTERFACE_INFO_LIST)()
        self._check(self.api.WlanEnumInterfaces(self.handle, None, ctypes.byref(pointer)), "enumerate Wi-Fi adapters")
        try:
            count = pointer.contents.dwNumberOfItems
            if count != 1: raise RuntimeError(f"Expected one enabled Wi-Fi adapter; found {count}.")
            item = pointer.contents.InterfaceInfo[0]
            return GUID.from_buffer_copy(item.InterfaceGuid), item.strInterfaceDescription
        finally:
            self.api.WlanFreeMemory(pointer)

    def set_profile(self, guid: GUID, profile: str, passphrase: str) -> None:
        ssid = html.escape(profile)
        secret = html.escape(passphrase)
        xml = f'''<?xml version="1.0"?><WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>{ssid}</name><SSIDConfig><SSID><name>{ssid}</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>manual</connectionMode><MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{secret}</keyMaterial></sharedKey></security></MSM></WLANProfile>'''
        reason = wintypes.DWORD()
        self._check(self.api.WlanSetProfile(self.handle, ctypes.byref(guid), 0, xml, None, True, None, ctypes.byref(reason)), "create the temporary Wi-Fi profile")

    def connect(self, guid: GUID, profile: str) -> None:
        parameters = WLAN_CONNECTION_PARAMETERS(0, profile, None, None, 1, 0)
        self._check(self.api.WlanConnect(self.handle, ctypes.byref(guid), ctypes.byref(parameters), None), "connect the Wi-Fi adapter")

    def disconnect(self, guid: GUID) -> None:
        self._check(self.api.WlanDisconnect(self.handle, ctypes.byref(guid), None), "disconnect the Wi-Fi adapter")

    def delete_profile(self, guid: GUID, profile: str) -> None:
        self._check(self.api.WlanDeleteProfile(self.handle, ctypes.byref(guid), profile, None), "delete the temporary Wi-Fi profile")

    def close(self) -> None:
        if self.handle: self.api.WlanCloseHandle(self.handle, None); self.handle = None


def _powershell(script: str) -> object:
    encoded = __import__("base64").b64encode(script.encode("utf-16-le")).decode("ascii")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], text=True, capture_output=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    if result.returncode != 0: raise RuntimeError("Could not inspect Windows network adapter state.")
    return json.loads(result.stdout)


def network_state(interface_name: str) -> dict:
    escaped = interface_name.replace("'", "''")
    return _powershell(f'''$wifi=Get-NetAdapter | Where-Object {{$_.Name -eq '{escaped}' -or $_.InterfaceDescription -eq '{escaped}'}} | Select-Object -First 1; if($null -eq $wifi){{throw 'Wi-Fi adapter missing'}}; $ethernet=@(Get-NetAdapter -Physical | Where-Object {{$_.Status -eq 'Up' -and $_.NdisPhysicalMedium -eq 14}}); $ip=@(Get-NetIPAddress -InterfaceAlias $wifi.Name -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {{$_.IPAddress -notlike '169.254.*'}} | Select-Object -ExpandProperty IPAddress); $profile=(Get-NetConnectionProfile -InterfaceAlias $wifi.Name -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Name); [pscustomobject]@{{ethernetUp=($ethernet.Count -gt 0); wifiStatus=$wifi.Status; ipv4=$ip; profile=$profile}} | ConvertTo-Json -Compress''')


def verify_portal(ssid: str, passphrase: str, timeout_seconds: int = 30, wlan_factory=WindowsWlan, state_reader=network_state) -> dict:
    """Associate externally, bind HTTP to the Wi-Fi address, and always remove the secret-bearing profile."""
    wlan = wlan_factory(); guid = None; profile_created = False; previous_profile = None; failures: list[str] = []; verified = False; local_ip = None
    try:
        guid, interface_name = wlan.interface()
        initial = state_reader(interface_name)
        previous_profile = initial.get("profile")
        if not initial.get("ethernetUp"): raise RuntimeError("Ethernet backend connectivity is required before portal verification.")
        wlan.set_profile(guid, ssid, passphrase); profile_created = True; wlan.connect(guid, ssid)
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            state = state_reader(interface_name)
            local_ip = next((value for value in state.get("ipv4", []) if state.get("profile") == ssid and ipaddress.ip_address(value) in ipaddress.ip_network("192.168.4.0/24")), None)
            if local_ip: break
            time.sleep(1)
        if not local_ip: raise RuntimeError("The Nano Wi-Fi connection did not receive a 192.168.4.x address.")
        request = b"GET / HTTP/1.1\r\nHost: 192.168.4.1\r\nConnection: close\r\n\r\n"
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as client:
            client.settimeout(10); client.bind((local_ip, 0)); client.connect(("192.168.4.1", 80)); client.sendall(request)
            response = b""
            while True:
                part = client.recv(8192)
                if not part: break
                response += part
        headers, _, body = response.partition(b"\r\n\r\n")
        if not headers.startswith(b"HTTP/1.1 200") and not headers.startswith(b"HTTP/1.0 200"): raise RuntimeError("The Nano portal did not return HTTP 200.")
        if b"text/html" not in headers.lower(): raise RuntimeError("The Nano portal did not return HTML.")
        if b'<meta name="waterflex-portal" content="setup-v1">' not in body: raise RuntimeError("The Nano portal marker was missing.")
        verified = True
    except RuntimeError as error:
        failures.append(str(error))
    finally:
        if guid is not None:
            try: wlan.disconnect(guid)
            except Exception: failures.append("wlan_cleanup_disconnect_failed")
            if profile_created:
                try: wlan.delete_profile(guid, ssid)
                except Exception: failures.append("wlan_cleanup_profile_delete_failed")
            if previous_profile and previous_profile != ssid:
                try: wlan.connect(guid, previous_profile)
                except Exception: failures.append("wlan_restore_previous_profile_failed")
        wlan.close()
    return {"verified": verified and not failures, "sourceAddress": local_ip if verified and not failures else None, "failureCategories": failures}
