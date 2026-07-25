/*
 * WaterFlex Plan C - Arduino Nano ESP32 salt-level sensor firmware (skeleton).
 *
 * Hardware:
 *   - Arduino Nano ESP32 (ESP32-S3)
 *   - DFRobot A02YYUW (SEN0311) ultrasonic sensor, UART @ 9600 8N1
 *
 * Wiring (see AI-Plans/plan-c-arduino-nano-esp32.md):
 *   A02YYUW VCC -> 3V3
 *   A02YYUW GND -> GND
 *   A02YYUW TX  -> D4  (Serial1 RX)
 *   A02YYUW RX  -> not connected (floating = processed/stable output mode)
 *
 * This skeleton implements the verified UART read path and leaves clearly marked
 * TODOs for Wi-Fi provisioning, HTTPS telemetry, calibration, and OTA.
 */

#include <Arduino.h>

namespace {
constexpr int kSensorRxPin = D4;      // A02YYUW TX -> Nano RX
constexpr int kSensorTxPin = D5;      // Assigned to Serial1 but not physically connected
constexpr uint32_t kSensorBaud = 9600;
constexpr uint8_t kFrameHeader = 0xFF;
constexpr uint32_t kReadTimeoutMs = 200;

// Returns distance in millimetres, or -1 on an invalid or timed-out frame.
int readDistanceMm() {
  const uint32_t start = millis();
  while (millis() - start < kReadTimeoutMs) {
    if (Serial1.read() != kFrameHeader) {
      continue;
    }
    uint8_t buf[3];
    if (Serial1.readBytes(buf, 3) != 3) {
      return -1;
    }
    const uint8_t checksum = static_cast<uint8_t>(kFrameHeader + buf[0] + buf[1]);
    if (checksum != buf[2]) {
      return -1;  // Bad checksum
    }
    return (static_cast<int>(buf[0]) << 8) | buf[1];
  }
  return -1;  // Timeout
}
}  // namespace

void setup() {
  Serial.begin(115200);  // USB diagnostics
  Serial1.begin(kSensorBaud, SERIAL_8N1, kSensorRxPin, kSensorTxPin);
  Serial1.setTimeout(kReadTimeoutMs);

  // TODO(plan-c C2): load persisted config; start SoftAP captive-portal provisioning on first boot.
  // TODO(plan-c C2): connect to the customer's 2.4 GHz Wi-Fi.
  // TODO(plan-c C2): establish HTTPS with a unique per-device bearer token.
  // TODO(plan-c C2): implement tank-depth calibration and secure OTA with rollback.
}

void loop() {
  const int distanceMm = readDistanceMm();
  if (distanceMm >= 0) {
    Serial.printf("distance=%d mm\n", distanceMm);
    // TODO(plan-c C2): median-filter samples, score quality, queue them durably, and POST
    // bounded telemetry batches (distance, quality, timestamp, firmware, RSSI, health) over HTTPS.
  } else {
    Serial.println("sensor read error");
  }

  delay(1000);  // Bench cadence; production reports ~hourly plus events.
}
