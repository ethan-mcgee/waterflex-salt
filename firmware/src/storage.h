// NVS-backed persistence: active Wi-Fi/device config and the telemetry queue.
#pragma once

#include "types.h"

// Persists the SSID/password pair to NVS as the device's active Wi-Fi profile.
void saveActiveProfile(const WifiProfile& profile);
// Persists the API URL/device token pair to NVS as the device's active config.
void saveDeviceConfig(const DeviceConfig& config);
// Removes the stored Wi-Fi profile, including the legacy hidden-network key.
void clearActiveProfile();
// Removes the stored device config (API URL and device token).
void clearDeviceConfig();
// Wipes profile, device config, activation/operational-credential staging keys,
// and queue metadata from NVS, then resets the matching in-memory state to
// defaults. Does not touch factory identity (serial number, bootstrap token).
void clearProvisioningState();
// Clears all provisioning state and restarts the device, returning it to the
// captive-portal first-boot flow.
void performFactoryReset();
// Loads the stored Wi-Fi profile into `profile`. Returns false (leaving
// `profile` untouched) when no SSID has ever been saved.
bool loadActiveProfile(WifiProfile* profile);
// Loads the stored device config, defaulting the API URL to the built-in
// telemetry endpoint when none has been saved.
DeviceConfig loadDeviceConfig();

// Loads queue head/count/dropped-count/next-sequence metadata from NVS into
// the in-memory queue state. Call once at boot before touching the queue.
void loadQueueState();
// Writes the in-memory queue head/count/dropped-count/next-sequence back to NVS.
void persistQueueMetadata();
// Reads the queued reading at `offset` slots past the queue head into
// `reading`. Returns false if `offset` is out of bounds or the stored slot
// is missing/corrupt.
bool readQueuedReading(uint8_t offset, QueuedReading* reading);
// Drops the oldest `count` queued readings (clamped to the current queue
// size) and persists the updated queue metadata.
void removeQueuedReadings(uint8_t count);
// Appends a reading for `distanceMm` to the upload queue, evicting the
// oldest entry first if the queue is already at capacity.
void enqueueReading(int distanceMm);
