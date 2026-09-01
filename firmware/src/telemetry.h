// SNTP sync, queued telemetry upload, and device health reporting.
#pragma once

#include "types.h"

// Blocks (up to kClockSyncTimeoutMs) on SNTP until the system clock reaches
// a plausible epoch. TLS cert validation needs a roughly-correct clock, so
// this gates any HTTPS request. Returns true once the clock looks valid,
// including when it already was.
bool ensureClockSynchronized();
// Reports sensor health when it changed or the minimum health-report
// interval elapsed, then enqueues and (if due) uploads a trustworthy reading.
void processTelemetry(const SensorReadResult& sensorRead);
// Retries a pending queued upload once its backoff delay has elapsed.
void processQueuedUploads();
