// SNTP sync, queued telemetry upload, and device health reporting.
#pragma once

#include "types.h"

bool ensureClockSynchronized();
void processTelemetry(const SensorReadResult& sensorRead);
void processQueuedUploads();
