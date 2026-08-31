#include "storage.h"

#include <Arduino.h>

#include "config.h"
#include "reset_control.h"
#include "state.h"

void ensurePrefsReady() {
  if (gPrefsInitialized) {
    return;
  }
  gPrefs.begin(kNvsNamespace, false);
  gPrefsInitialized = true;
}

void saveActiveProfile(const WifiProfile& profile) {
  ensurePrefsReady();
  gPrefs.putString(kKeySsid, profile.ssid);
  gPrefs.putString(kKeyPassword, profile.password);
  gPrefs.putBool(kKeyHidden, profile.hidden);
}

void saveDeviceConfig(const DeviceConfig& config) {
  ensurePrefsReady();
  gPrefs.putString(kKeyApiUrl, config.apiUrl);
  gPrefs.putString(kKeyDeviceToken, config.deviceToken);
}

void clearActiveProfile() {
  ensurePrefsReady();
  gPrefs.remove(kKeySsid);
  gPrefs.remove(kKeyPassword);
  gPrefs.remove(kKeyHidden);
}

void clearDeviceConfig() {
  ensurePrefsReady();
  gPrefs.remove(kKeyApiUrl);
  gPrefs.remove(kKeyDeviceToken);
}

void clearProvisioningState() {
  ensurePrefsReady();
  gPrefs.clear();
  gActiveProfile = WifiProfile{};
  gDeviceConfig = DeviceConfig{};
  gHasActiveProfile = false;
  gCandidateProfile = WifiProfile{};
  gCandidateDeviceConfig = DeviceConfig{};
  gCandidateApplyOnSuccess = false;
  gHasCandidateProfile = false;
  gLastError = "";
  gQueueHead = 0;
  gQueueCount = 0;
  gDroppedReadingCount = 0;
  gReadingSequenceNumber = 0;
}

void performFactoryReset() {
  clearProvisioningState();
  gLastError = "factory_reset";
  Serial.println("factory reset requested");
  delay(200);
  restartDevice();
}

bool loadActiveProfile(WifiProfile* profile) {
  const String ssid = gPrefs.getString(kKeySsid, "");
  if (ssid.isEmpty()) {
    return false;
  }
  profile->ssid = ssid;
  profile->password = gPrefs.getString(kKeyPassword, "");
  profile->hidden = gPrefs.getBool(kKeyHidden, false);
  return true;
}

DeviceConfig loadDeviceConfig() {
  DeviceConfig config;
  config.apiUrl = gPrefs.getString(kKeyApiUrl, kDefaultTelemetryUrl);
  config.deviceToken = gPrefs.getString(kKeyDeviceToken, "");
  return config;
}

String queueSlotKey(uint8_t slot) {
  char key[5];
  snprintf(key, sizeof(key), "q%02u", static_cast<unsigned int>(slot));
  return String(key);
}

void loadQueueState() {
  ensurePrefsReady();
  gQueueHead = gPrefs.getUChar(kKeyQueueHead, 0) % kQueueCapacity;
  gQueueCount = min(static_cast<size_t>(gPrefs.getUChar(kKeyQueueCount, 0)), kQueueCapacity);
  gDroppedReadingCount = gPrefs.getULong(kKeyDroppedCount, 0);
  gReadingSequenceNumber = gPrefs.getULong64(kKeyNextSequence, 0);
}

void persistQueueMetadata() {
  gPrefs.putUChar(kKeyQueueHead, gQueueHead);
  gPrefs.putUChar(kKeyQueueCount, gQueueCount);
  gPrefs.putULong(kKeyDroppedCount, gDroppedReadingCount);
  gPrefs.putULong64(kKeyNextSequence, gReadingSequenceNumber);
}

bool readQueuedReading(uint8_t offset, QueuedReading* reading) {
  if (offset >= gQueueCount) {
    return false;
  }
  const uint8_t slot = (gQueueHead + offset) % kQueueCapacity;
  const String key = queueSlotKey(slot);
  return gPrefs.getBytesLength(key.c_str()) == sizeof(QueuedReading)
      && gPrefs.getBytes(key.c_str(), reading, sizeof(QueuedReading)) == sizeof(QueuedReading);
}

void removeQueuedReadings(uint8_t count) {
  count = min(count, gQueueCount);
  for (uint8_t i = 0; i < count; ++i) {
    const String key = queueSlotKey(gQueueHead);
    gPrefs.remove(key.c_str());
    gQueueHead = (gQueueHead + 1) % kQueueCapacity;
    --gQueueCount;
  }
  persistQueueMetadata();
}

void enqueueReading(int distanceMm) {
  ensurePrefsReady();
  if (gQueueCount == kQueueCapacity) {
    removeQueuedReadings(1);
    ++gDroppedReadingCount;
  }

  QueuedReading reading{};
  strlcpy(reading.bootId, gBootId.c_str(), sizeof(reading.bootId));
  reading.sequenceNumber = gReadingSequenceNumber++;
  reading.uptimeMilliseconds = millis();
  reading.rawDistanceMm = distanceMm;
  const int rssi = WiFi.status() == WL_CONNECTED ? WiFi.RSSI() : -127;
  reading.wifiRssiDbm = constrain(rssi, -127, 0);
  reading.quality = 90;

  const uint8_t slot = (gQueueHead + gQueueCount) % kQueueCapacity;
  const String key = queueSlotKey(slot);
  if (gPrefs.putBytes(key.c_str(), &reading, sizeof(reading)) != sizeof(reading)) {
    ++gDroppedReadingCount;
    persistQueueMetadata();
    Serial.println("telemetry queue write failed");
    return;
  }
  ++gQueueCount;
  persistQueueMetadata();
}
