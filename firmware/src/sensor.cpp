#include "sensor.h"

#include <Arduino.h>

#include "a02yyuw_uart_parser.h"
#include "config.h"

namespace {
waterflex::A02YYUWFrameParser gSensorParser;
}  // namespace

SensorReadResult readSensor() {
  // The A0221AT is the controlled-UART A02 variant. It returns one frame only
  // after RX sees serial activity. Discard any response left over from an
  // interrupted read so that each result belongs to the trigger below.
  while (Serial1.available() > 0) {
    Serial1.read();
  }
  gSensorParser.reset();
  if (Serial1.write(kSensorTriggerByte) != 1) {
    return {-1, "readTimeout"};
  }
  Serial1.flush();

  const uint32_t startedAtMs = millis();
  bool sawAnyByte = false;
  bool sawInvalidChecksum = false;
  bool sawOutOfRange = false;
  int latestValidDistanceMm = -1;

  while (millis() - startedAtMs < kSensorReadTimeoutMs) {
    while (Serial1.available() > 0) {
      const int rawValue = Serial1.read();
      if (rawValue < 0) {
        break;
      }

      sawAnyByte = true;
      int distanceMm = -1;
      switch (gSensorParser.consume(static_cast<uint8_t>(rawValue), &distanceMm)) {
        case waterflex::A02YYUWFrameStatus::Valid:
          // A controlled-UART trigger should produce one response frame.
          latestValidDistanceMm = distanceMm;
          break;
        case waterflex::A02YYUWFrameStatus::InvalidChecksum:
          sawInvalidChecksum = true;
          break;
        case waterflex::A02YYUWFrameStatus::OutOfRange:
          sawOutOfRange = true;
          break;
        case waterflex::A02YYUWFrameStatus::Incomplete:
          break;
      }
    }
    if (latestValidDistanceMm >= 0) {
      return {latestValidDistanceMm, nullptr};
    }
    delay(1);
  }

  // Do not carry an indefinitely partial frame across read windows. At 9600
  // baud a complete four-byte frame takes only a few milliseconds.
  gSensorParser.reset();
  if (sawOutOfRange) {
    return {-1, "outOfRange"};
  }
  if (sawInvalidChecksum || sawAnyByte) {
    return {-1, "invalidSignal"};
  }
  return {-1, "readTimeout"};
}
