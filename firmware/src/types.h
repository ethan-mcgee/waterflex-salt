// Shared value types used across the WaterFlex firmware modules.
#pragma once

#include <Arduino.h>

#include "config.h"

// The customer Wi-Fi network credentials the device joins for normal operation.
struct WifiProfile {
  String ssid;
  String password;
};

// Where and how the device reports telemetry: the API base URL and the
// bearer credential issued by activation (or entered directly).
struct DeviceConfig {
  String apiUrl;
  String deviceToken;
};

// One sensor read attempt's outcome: either a distance in millimeters, or a
// negative distance paired with a faultCode explaining why.
struct SensorReadResult {
  SensorReadResult(int distance, const char* fault) : distanceMm(distance), faultCode(fault) {}

  int distanceMm;
  const char* faultCode;

  // True when the read produced a real, in-range distance. Only trustworthy
  // reads are enqueued for telemetry; faulted reads update device health only.
  bool isTrustworthy() const {
    return distanceMm >= kSensorMinimumDistanceMm && distanceMm <= kSensorMaximumDistanceMm;
  }
};

// One sensor reading staged in the NVS-backed upload queue, in the fixed
// binary layout stored per queue slot.
struct QueuedReading {
  char bootId[37];
  uint64_t sequenceNumber;
  uint32_t uptimeMilliseconds;
  int32_t rawDistanceMm;
  int16_t wifiRssiDbm;
  uint8_t quality;
};

// The device's top-level provisioning/connectivity state, driving both the
// portal's status API and main.cpp's dispatch of what to run each loop.
enum class ProvisioningState {
  Unprovisioned,     // No active Wi-Fi profile or device token yet.
  PortalIdle,         // Portal is up, waiting for the setup form to be submitted.
  PortalConnecting,   // A candidate or saved profile's Wi-Fi connect is in flight.
  PortalError,        // The last connect/activation/verification attempt failed.
  Active               // Connected with a working profile and device token.
};
