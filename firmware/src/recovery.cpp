#include "recovery.h"

#include <Arduino.h>

#include "captive_portal.h"
#include "config.h"
#include "reset_control.h"
#include "state.h"
#include "storage.h"

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

      command.toUpperCase();
      if (command == "FACTORY_RESET" || command == "FACTORYRESET" || command == "RESET") {
        Serial.println("factory reset command received");
        performFactoryReset();
      } else if (command == "PORTAL") {
        Serial.println("portal command received");
        startPortal("serial_portal");
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
