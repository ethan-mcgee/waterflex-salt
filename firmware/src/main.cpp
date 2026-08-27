/*
 * WaterFlex Plan C - Arduino Nano ESP32 salt-level sensor firmware.
 *
 * Hardware:
 *   - Arduino Nano ESP32 (ESP32-S3)
 *   - A0221AT / DYP-A02 controlled-UART waterproof ultrasonic sensor
 *
 * Sensor wiring through the ASX00061 Nano Connector Carrier UART port:
 *   Pin 1 VCC -> Nano 3V3
 *   Pin 2 GND -> Nano GND
 *   Pin 3 RX  -> carrier TX / Nano D1 (measurement trigger)
 *   Pin 4 TX  -> carrier RX / Nano D0 (distance response)
 *
 * Faulted reads update device health only and can never update operational fill.
 *
 * Responsibilities are split by module:
 *   config.h            - compile-time constants and NVS key names
 *   types.h              - shared value types (WifiProfile, DeviceConfig, ...)
 *   state.h/.cpp         - the device's mutable global state
 *   identity_utils.*     - id/encoding/URL helpers with no state dependency
 *   reset_control.*      - onboard double-RESET gesture and device restart
 *   storage.*            - NVS-backed profile, device config, and queue persistence
 *   sensor.*             - ultrasonic sensor read + framing
 *   device_activation.*  - factory-bootstrap self-activation and API verification
 *   telemetry.*          - clock sync, queued upload, and health reporting
 *   wifi_connection.*    - Wi-Fi connect lifecycle and auto-recovery
 *   captive_portal.*     - SoftAP setup portal (routes, DNS capture, lifecycle)
 *   recovery.*           - serial commands and the physical recovery button
 */

#include <Arduino.h>

#include "captive_portal.h"
#include "config.h"
#include "identity_utils.h"
#include "recovery.h"
#include "sensor.h"
#include "reset_control.h"
#include "state.h"
#include "storage.h"
#include "telemetry.h"
#include "wifi_connection.h"

namespace {

void initializeProvisioning() {
  ensurePrefsReady();

  bool onboardResetSetupRequested = false;
  if (onboardResetGestureIsArmed()) {
    disarmOnboardResetGesture();
    clearActiveProfile();
    clearDeviceConfig();
    onboardResetSetupRequested = true;
    Serial.println("onboard RESET gesture recognized; provisioning settings cleared");
  } else {
    armOnboardResetGesture();
    Serial.println("onboard RESET gesture armed for 10 seconds");
  }

  gHasActiveProfile = loadActiveProfile(&gActiveProfile);
  gDeviceConfig = loadDeviceConfig();
  gBootstrapToken = gPrefs.getString(kKeyBootstrapToken, "");
  gSerialNumber = gPrefs.getString(kKeySerialNumber, "");
  loadQueueState();
  gCandidateDeviceConfig = gDeviceConfig;

  Serial.printf("device hardwareId=%s wifiConfigured=%s tokenConfigured=%s bootstrapConfigured=%s\n",
                hardwareId().c_str(),
                gHasActiveProfile ? "true" : "false",
                gDeviceConfig.deviceToken.isEmpty() ? "false" : "true",
                (!gBootstrapToken.isEmpty() && !gSerialNumber.isEmpty()) ? "true" : "false");

  if (onboardResetSetupRequested) {
    startPortal("onboard_reset");
  } else if (!gHasActiveProfile || gDeviceConfig.deviceToken.isEmpty()) {
    startPortal(gHasActiveProfile ? "missing_device_token" : "first_boot");
  } else {
    connectWithSavedProfile();
  }
}

}  // namespace

void setup() {
  Serial.begin(115200);  // USB diagnostics
  pinMode(kRecoveryPin, INPUT_PULLUP);
  Serial1.setRxBufferSize(kSensorRxBufferSize);
  Serial1.begin(kSensorBaudRate, SERIAL_8N1, kSensorRxPin, kSensorTxPin);

  initializeProvisioning();
  gBootId = makeBootId();
  Serial.printf("bootId=%s\n", gBootId.c_str());

  // Tank calibration is owned by the commissioning session. Signed A/B OTA remains
  // gated by the production-security work instruction and is not enabled here.
}

void loop() {
  processSerialCommands();
  processOnboardResetGestureWindow();
  processRecoveryButton();
  processPortal();
  processWifiConnection();
  processAutoRecoveryPortal();
  processQueuedUploads();

  const SensorReadResult sensorRead = readSensor();
  if (sensorRead.isTrustworthy()) {
    Serial.printf("distance=%d mm\n", sensorRead.distanceMm);
  } else {
    Serial.printf("sensor read error fault=%s\n", sensorRead.faultCode);
  }
  processTelemetry(sensorRead);

  delay(1000);  // Bench cadence; production reports ~hourly plus events.
}
