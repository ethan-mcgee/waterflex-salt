#include "telemetry.h"

#include <ArduinoJson.h>
#include <HTTPClient.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <time.h>

#include "cloudflare_root_ca.h"
#include "config.h"
#include "identity_utils.h"
#include "state.h"
#include "storage.h"

namespace {

String telemetryBatchPayload(uint8_t* includedCount) {
  String body;
  body.reserve(180 * kUploadBatchSize);
  body += "{\"schemaVersion\":1,\"firmwareVersion\":\"";
  body += kFirmwareVersion;
  body += "\",\"readings\":[";
  *includedCount = 0;
  const uint8_t requested = min(gQueueCount, static_cast<uint8_t>(kUploadBatchSize));
  for (uint8_t i = 0; i < requested; ++i) {
    QueuedReading reading{};
    if (!readQueuedReading(i, &reading)) {
      break;
    }
    if (i > 0) body += ',';
    body += "{\"bootId\":\"";
    body += reading.bootId;
    body += "\",\"sequenceNumber\":";
    body += String(static_cast<unsigned long long>(reading.sequenceNumber));
    body += ",\"uptimeMilliseconds\":";
    body += String(reading.uptimeMilliseconds);
    body += ",\"rawDistanceMm\":";
    body += String(reading.rawDistanceMm);
    body += ",\"quality\":";
    body += String(reading.quality);
    body += ",\"sampleCount\":1,\"wifiRssiDbm\":";
    body += String(reading.wifiRssiDbm);
    body += ",\"errorFlags\":[]}";
    ++(*includedCount);
  }
  body += "]}";
  return body;
}

void scheduleUploadRetry() {
  gUploadFailureCount = min(static_cast<uint8_t>(gUploadFailureCount + 1), static_cast<uint8_t>(8));
  const uint32_t exponentialMs = min(kRetryMaximumMs, kRetryBaseMs << gUploadFailureCount);
  const uint32_t jitterMs = exponentialMs / 4 == 0 ? 0 : esp_random() % (exponentialMs / 4);
  gNextUploadAtMs = millis() + exponentialMs + jitterMs;
}

void uploadQueuedTelemetry() {
  if (gQueueCount == 0 || WiFi.status() != WL_CONNECTED) {
    return;
  }

  if (gDeviceConfig.apiUrl.isEmpty()) {
    Serial.println("telemetry skipped: api URL not configured");
    return;
  }

  if (gDeviceConfig.deviceToken.isEmpty()) {
    Serial.println("telemetry skipped: device token not configured");
    return;
  }

  if (!isApprovedOperationalApiUrl(gDeviceConfig.apiUrl)) {
    Serial.println("telemetry blocked: destination is not approved");
    return;
  }

  const bool usesHttps = gDeviceConfig.apiUrl.startsWith("https://");
  if (usesHttps && !ensureClockSynchronized()) {
    Serial.println("telemetry skipped: clock synchronization required for TLS");
    scheduleUploadRetry();
    return;
  }

  WiFiClient plainClient;
  WiFiClientSecure secureClient;
  WiFiClient *client = &plainClient;
  if (usesHttps) {
    secureClient.setCACert(kCloudflareRootCa);
    client = &secureClient;
  }

  HTTPClient http;
  if (!http.begin(*client, gDeviceConfig.apiUrl.c_str())) {
    Serial.println("telemetry begin failed");
    return;
  }

  http.addHeader("Content-Type", "application/json");
  http.addHeader("Authorization", String("Bearer ") + gDeviceConfig.deviceToken);
  uint8_t includedCount = 0;
  const String body = telemetryBatchPayload(&includedCount);
  if (includedCount == 0) {
    http.end();
    Serial.println("telemetry queue corrupt: no readable entries");
    return;
  }
  const int statusCode = http.POST(body);
  if (statusCode == 200) {
    JsonDocument acknowledgement;
    const DeserializationError jsonError = deserializeJson(acknowledgement, http.getString());
    const uint32_t nextIntervalSeconds = acknowledgement["nextReportIntervalSeconds"] | 0;
    uint8_t acknowledgedCount = 0;
    if (!jsonError) {
      JsonArray acknowledgements = acknowledgement["readings"].as<JsonArray>();
      // removeQueuedReadings() only ever drops from the queue head, so
      // acknowledgements must be consumed in the same head-first order they
      // were sent. Stop at the first bootId/sequence mismatch rather than
      // matching out of order, so a partial or reordered ack can't cause an
      // unacknowledged reading to be dropped.
      for (JsonObject acknowledgementReading : acknowledgements) {
        if (acknowledgedCount >= includedCount) break;
        QueuedReading expected{};
        if (!readQueuedReading(acknowledgedCount, &expected)) break;
        const char* acknowledgedBootId = acknowledgementReading["bootId"] | "";
        const uint64_t acknowledgedSequence = acknowledgementReading["sequenceNumber"] | UINT64_MAX;
        const char* acknowledgedStatus = acknowledgementReading["status"] | "";
        const bool statusAccepted = strcmp(acknowledgedStatus, "accepted") == 0
            || strcmp(acknowledgedStatus, "duplicate") == 0;
        if (strcmp(acknowledgedBootId, expected.bootId) != 0
            || acknowledgedSequence != expected.sequenceNumber
            || !statusAccepted) {
          break;
        }
        ++acknowledgedCount;
      }
    }
    if (!jsonError
        && nextIntervalSeconds >= kMinimumTelemetryIntervalSeconds
        && nextIntervalSeconds <= kMaximumTelemetryIntervalSeconds) {
      gTelemetryIntervalMs = nextIntervalSeconds * 1000UL;
    }
    if (acknowledgedCount == 0) {
      Serial.println("telemetry acknowledgement contained no readings");
      scheduleUploadRetry();
    } else {
      removeQueuedReadings(acknowledgedCount);
      gUploadFailureCount = 0;
      gNextUploadAtMs = 0;
    }
    Serial.printf("telemetry post status=%d acknowledged=%u queued=%u next=%lus\n",
                  statusCode, acknowledgedCount, gQueueCount,
                  static_cast<unsigned long>(gTelemetryIntervalMs / 1000UL));
  } else if (statusCode > 0) {
    Serial.printf("telemetry rejected status=%d queued=%u\n", statusCode, gQueueCount);
    scheduleUploadRetry();
  } else {
    Serial.printf("telemetry post failed error=%s\n", http.errorToString(statusCode).c_str());
    scheduleUploadRetry();
  }
  http.end();
}

bool sendDeviceHealth(bool sensorHealthy, const char* sensorFault = nullptr) {
  if (WiFi.status() != WL_CONNECTED || gDeviceConfig.deviceToken.isEmpty()) {
    return false;
  }

  const String healthUrl = deviceHealthUrl(gDeviceConfig.apiUrl);
  if (healthUrl.isEmpty()) {
    Serial.println("health skipped: telemetry URL does not end in /telemetry");
    return false;
  }

  const bool usesHttps = healthUrl.startsWith("https://");
  const bool clockSynchronized = time(nullptr) >= kMinimumValidEpoch;
  if (usesHttps && !clockSynchronized && !ensureClockSynchronized()) {
    Serial.println("health skipped: clock synchronization required for TLS");
    return false;
  }

  WiFiClient plainClient;
  WiFiClientSecure secureClient;
  WiFiClient *client = &plainClient;
  if (usesHttps) {
    secureClient.setCACert(kCloudflareRootCa);
    client = &secureClient;
  }

  HTTPClient http;
  if (!http.begin(*client, healthUrl.c_str())) {
    Serial.println("health begin failed");
    return false;
  }

  const int wifiRssiDbm = WiFi.RSSI() > 0 ? 0 : (WiFi.RSSI() < -127 ? -127 : WiFi.RSSI());
  String body;
  body.reserve(320);
  body += "{\"schemaVersion\":1,\"firmwareVersion\":\"";
  body += kFirmwareVersion;
  body += "\",\"uptimeMilliseconds\":";
  body += String(millis());
  body += ",\"sensorStatus\":\"";
  body += sensorHealthy ? "healthy" : "faulted";
  body += "\",\"sensorFault\":";
  if (sensorHealthy) {
    body += "null";
  } else {
    body += "\"";
    body += sensorFault == nullptr ? "invalidSignal" : sensorFault;
    body += "\"";
  }
  body += ",\"wifiRssiDbm\":";
  body += String(wifiRssiDbm);
  body += ",\"queuedReadingCount\":";
  body += String(gQueueCount);
  body += ",\"clockSynchronized\":";
  body += time(nullptr) >= kMinimumValidEpoch ? "true" : "false";
  body += ",\"droppedReadingCount\":";
  body += String(gDroppedReadingCount);
  body += "}";

  http.addHeader("Content-Type", "application/json");
  http.addHeader("Authorization", String("Bearer ") + gDeviceConfig.deviceToken);
  const int statusCode = http.POST(body);
  const bool accepted = statusCode == 200;
  if (accepted) {
    Serial.printf("health post status=%d sensor=%s\n", statusCode, sensorHealthy ? "healthy" : "faulted");
  } else if (statusCode > 0) {
    Serial.printf("health rejected status=%d\n", statusCode);
  } else {
    Serial.printf("health post failed error=%s\n", http.errorToString(statusCode).c_str());
  }
  http.end();
  return accepted;
}

}  // namespace

bool ensureClockSynchronized() {
  if (time(nullptr) >= kMinimumValidEpoch) {
    return true;
  }

  configTime(0, 0, "time.cloudflare.com", "time.google.com", "pool.ntp.org");
  const uint32_t startedAtMs = millis();
  while (time(nullptr) < kMinimumValidEpoch
         && millis() - startedAtMs < kClockSyncTimeoutMs) {
    delay(100);
  }

  return time(nullptr) >= kMinimumValidEpoch;
}

void processTelemetry(const SensorReadResult& sensorRead) {
  const uint32_t now = millis();
  const bool sensorHealthy = sensorRead.isTrustworthy();
  const String sensorFault = sensorRead.faultCode == nullptr ? "" : sensorRead.faultCode;
  const bool healthChanged = !gHasReportedSensorHealth
      || sensorHealthy != gLastReportedSensorHealthy
      || sensorFault != gLastReportedSensorFault;
  if (healthChanged || gLastHealthAtMs == 0 || now - gLastHealthAtMs >= kMinimumHealthIntervalMs) {
    if (sendDeviceHealth(sensorHealthy, sensorRead.faultCode)) {
      gLastHealthAtMs = now;
      gHasReportedSensorHealth = true;
      gLastReportedSensorHealthy = sensorHealthy;
      gLastReportedSensorFault = sensorFault;
    }
  }

  if (!gTelemetryDue && now - gLastTelemetryAtMs < gTelemetryIntervalMs) {
    return;
  }

  if (!sensorRead.isTrustworthy()) {
    return;
  }

  gLastTelemetryAtMs = now;
  gTelemetryDue = false;
  enqueueReading(sensorRead.distanceMm);

  if (gNextUploadAtMs == 0 || static_cast<int32_t>(now - gNextUploadAtMs) >= 0) {
    uploadQueuedTelemetry();
  }
}

void processQueuedUploads() {
  if (gQueueCount == 0 || WiFi.status() != WL_CONNECTED) {
    return;
  }
  const uint32_t now = millis();
  if (gNextUploadAtMs == 0 || static_cast<int32_t>(now - gNextUploadAtMs) >= 0) {
    uploadQueuedTelemetry();
  }
}
