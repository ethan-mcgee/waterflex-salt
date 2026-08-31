// Shared value types used across the WaterFlex firmware modules.
#pragma once

#include <Arduino.h>

#include "config.h"

struct WifiProfile {
  String ssid;
  String password;
};

struct DeviceConfig {
  String apiUrl;
  String deviceToken;
};

struct SensorReadResult {
  SensorReadResult(int distance, const char* fault) : distanceMm(distance), faultCode(fault) {}

  int distanceMm;
  const char* faultCode;

  bool isTrustworthy() const {
    return distanceMm >= kSensorMinimumDistanceMm && distanceMm <= kSensorMaximumDistanceMm;
  }
};

struct QueuedReading {
  char bootId[37];
  uint64_t sequenceNumber;
  uint32_t uptimeMilliseconds;
  int32_t rawDistanceMm;
  int16_t wifiRssiDbm;
  uint8_t quality;
};

enum class ProvisioningState {
  Unprovisioned,
  PortalIdle,
  PortalConnecting,
  PortalError,
  Active
};
