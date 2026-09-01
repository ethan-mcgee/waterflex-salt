// A0221AT / DYP-A02 controlled-UART ultrasonic sensor reads.
#pragma once

#include "types.h"

// Drains any stale UART bytes, triggers the controlled-UART sensor, and
// waits up to kSensorReadTimeoutMs for one valid distance frame. Returns a
// negative distanceMm with a faultCode ("readTimeout", "invalidSignal", or
// "outOfRange") when no valid frame arrives in time.
SensorReadResult readSensor();
