// NVS-backed persistence: active Wi-Fi/device config and the telemetry queue.
#pragma once

#include "types.h"

void saveActiveProfile(const WifiProfile& profile);
void saveDeviceConfig(const DeviceConfig& config);
void clearActiveProfile();
void clearDeviceConfig();
void clearProvisioningState();
void performFactoryReset();
bool loadActiveProfile(WifiProfile* profile);
DeviceConfig loadDeviceConfig();

void loadQueueState();
void persistQueueMetadata();
bool readQueuedReading(uint8_t offset, QueuedReading* reading);
void removeQueuedReadings(uint8_t count);
void enqueueReading(int distanceMm);
