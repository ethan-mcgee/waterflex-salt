#include "recovery.h"

#include <Arduino.h>
#include <ArduinoJson.h>

#include "captive_portal.h"
#include "config.h"
#include "identity_utils.h"
#include "reset_control.h"
#include "state.h"
#include "storage.h"

namespace {

bool isValidFactorySerial(const String& serial) {
  constexpr char prefix[] = "WF-NANO-";
  if (!serial.startsWith(prefix) || serial.length() < strlen(prefix) + 4) return false;
  for (size_t i = strlen(prefix); i < serial.length(); ++i) {
    if (!isDigit(serial[i])) return false;
  }
  return true;
}

void printFactoryStatus() {
  ensurePrefsReady();
  const String serial = gPrefs.getString(kKeySerialNumber, "");
  const bool setupConfigured = !gPrefs.getString(kKeyPassphrase, "").isEmpty();
  const bool bootstrapConfigured = !gPrefs.getString(kKeyBootstrapToken, "").isEmpty();
  Serial.printf(
      "factory_status={\"serialNumber\":\"%s\",\"firmwareVersion\":\"%s\",\"setupConfigured\":%s,\"bootstrapConfigured\":%s,\"operationalCredentialConfigured\":%s,\"portalRunning\":%s}\n",
      jsonEscape(serial).c_str(),
      kFirmwareVersion,
      setupConfigured ? "true" : "false",
      bootstrapConfigured ? "true" : "false",
      gDeviceConfig.deviceToken.isEmpty() ? "false" : "true",
      gPortalRunning ? "true" : "false");
}

void provisionFactoryIdentity(const String& payload) {
  ensurePrefsReady();
  if (!gPrefs.getString(kKeySerialNumber, "").isEmpty()
      || !gPrefs.getString(kKeyPassphrase, "").isEmpty()
      || !gPrefs.getString(kKeyBootstrapToken, "").isEmpty()) {
    Serial.println("factory_provisioning_result={\"status\":\"rejected\",\"errorCode\":\"factory_identity_already_present\"}");
    return;
  }

  JsonDocument document;
  if (deserializeJson(document, payload)) {
    Serial.println("factory_provisioning_result={\"status\":\"rejected\",\"errorCode\":\"invalid_json\"}");
    return;
  }
  const String serial = String(document["serialNumber"] | "");
  const String setupPassphrase = String(document["setupPassphrase"] | "");
  const String bootstrapToken = String(document["bootstrapToken"] | "");
  if (!isValidFactorySerial(serial)
      || setupPassphrase.length() < 8 || setupPassphrase.length() > 63
      || !bootstrapToken.startsWith("wf_boot_") || bootstrapToken.indexOf('.') < 9) {
    Serial.println("factory_provisioning_result={\"status\":\"rejected\",\"errorCode\":\"invalid_factory_identity\"}");
    return;
  }

  const bool saved = gPrefs.putString(kKeySerialNumber, serial) == serial.length()
      && gPrefs.putString(kKeyPassphrase, setupPassphrase) == setupPassphrase.length()
      && gPrefs.putString(kKeyBootstrapToken, bootstrapToken) == bootstrapToken.length();
  if (!saved) {
    gPrefs.remove(kKeySerialNumber);
    gPrefs.remove(kKeyPassphrase);
    gPrefs.remove(kKeyBootstrapToken);
    Serial.println("factory_provisioning_result={\"status\":\"rejected\",\"errorCode\":\"nvs_write_failed\"}");
    return;
  }
  Serial.printf("factory_provisioning_result={\"status\":\"provisioned\",\"serialNumber\":\"%s\",\"firmwareVersion\":\"%s\"}\n",
                serial.c_str(), kFirmwareVersion);
  delay(250);
  restartDevice();
}

}  // namespace

void processSerialCommands() {
  static String inputBuffer;

  while (Serial.available() > 0) {
    const char c = static_cast<char>(Serial.read());
    if (c == '\r' || c == '\n') {
      String command = inputBuffer;
      inputBuffer = "";
      command.trim();
      if (command.isEmpty()) {
        continue;
      }

      if (command.startsWith("FACTORY_PROVISION ")) {
        provisionFactoryIdentity(command.substring(strlen("FACTORY_PROVISION ")));
        continue;
      }

      command.toUpperCase();
      if (command == "FACTORY_STATUS" || command == "FACTORYSTATUS") {
        printFactoryStatus();
      } else if (command == "FACTORY_RESET" || command == "FACTORYRESET" || command == "RESET") {
        Serial.println("factory reset command received");
        performFactoryReset();
      } else if (command == "PORTAL") {
        Serial.println("portal command received");
        startPortal("serial_portal");
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
      } else if (command == "PORTAL_PREVIEW" || command == "PORTALPREVIEW") {
        Serial.println("portal preview command received");
        startPortalPreview();
#endif
      } else {
        Serial.println("unknown command");
      }
      continue;
    }

    if (c >= 32 && c <= 126) {
      inputBuffer += c;
    }
  }
}

void processRecoveryButton() {
  const bool pressed = digitalRead(kRecoveryPin) == LOW;
  const uint32_t now = millis();

  if (pressed && !gRecoveryButtonDown) {
    gRecoveryButtonDown = true;
    gRecoveryPressedAtMs = now;
    gRecoveryPortalTriggered = false;
    gFactoryResetTriggered = false;
    return;
  }

  if (!pressed && gRecoveryButtonDown) {
    gRecoveryButtonDown = false;
    gRecoveryPressedAtMs = 0;
    return;
  }

  if (!pressed) {
    return;
  }

  const uint32_t heldMs = now - gRecoveryPressedAtMs;
  if (!gFactoryResetTriggered && heldMs >= kFactoryResetHoldMs) {
    gFactoryResetTriggered = true;
    performFactoryReset();
    return;
  }

  if (!gRecoveryPortalTriggered && heldMs >= kRecoveryPortalHoldMs) {
    gRecoveryPortalTriggered = true;
    startPortal("manual_recovery");
  }
}

void processOnboardResetGestureWindow() {
  if (gOnboardResetGestureArmedAtMs != 0
      && millis() - gOnboardResetGestureArmedAtMs >= kOnboardResetGestureWindowMs) {
    disarmOnboardResetGesture();
    Serial.println("onboard reset gesture window closed");
  }
}
