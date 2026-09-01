#include "device_activation.h"

#include <ArduinoJson.h>
#include <HTTPClient.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <mbedtls/sha256.h>

#include "cloudflare_root_ca.h"
#include "config.h"
#include "identity_utils.h"
#include "state.h"
#include "telemetry.h"

bool activateCandidateDevice(DeviceConfig* config) {
  if (gBootstrapToken.isEmpty() || gSerialNumber.isEmpty()
      || !isApprovedOperationalApiUrl(config->apiUrl)
      || WiFi.status() != WL_CONNECTED
      || !ensureClockSynchronized()) {
    return false;
  }

  ensurePrefsReady();
  String attemptId = gPrefs.getString(kKeyActivationAttempt, "");
  String credentialId = gPrefs.getString(kKeyOperationalCredential, "");
  String operationalSecret = gPrefs.getString(kKeyOperationalSecret, "");
  String operationalSecretHash = gPrefs.getString(kKeyOperationalSecretHash, "");
  // Stage the attempt id and generated credential in NVS before the network
  // call. If the device resets mid-request, the next attempt reuses this
  // same staged credential instead of minting a new one the server would
  // treat as a distinct device.
  if (attemptId.isEmpty() || credentialId.isEmpty()
      || operationalSecret.isEmpty() || operationalSecretHash.isEmpty()) {
    uint8_t secret[32];
    for (size_t i = 0; i < sizeof(secret); i += sizeof(uint32_t)) {
      const uint32_t randomValue = esp_random();
      memcpy(secret + i, &randomValue, sizeof(randomValue));
    }
    uint8_t hash[32];
    if (mbedtls_sha256_ret(secret, sizeof(secret), hash, 0) != 0) return false;
    operationalSecret = base64Encode(secret, sizeof(secret), true);
    operationalSecretHash = base64Encode(hash, sizeof(hash), false);
    attemptId = makeBootId();
    char credentialSuffix[25];
    snprintf(credentialSuffix, sizeof(credentialSuffix), "%08lx%08lx%08lx",
             static_cast<unsigned long>(esp_random()),
             static_cast<unsigned long>(esp_random()),
             static_cast<unsigned long>(esp_random()));
    credentialId = String("wf_dev_") + credentialSuffix;
    if (operationalSecret.isEmpty() || operationalSecretHash.isEmpty()) return false;
    gPrefs.putString(kKeyActivationAttempt, attemptId);
    gPrefs.putString(kKeyOperationalCredential, credentialId);
    gPrefs.putString(kKeyOperationalSecret, operationalSecret);
    gPrefs.putString(kKeyOperationalSecretHash, operationalSecretHash);
  }

  const String url = activationUrl(config->apiUrl);
  if (url.isEmpty()) return false;
  WiFiClientSecure secureClient;
  secureClient.setCACert(kCloudflareRootCa);
  HTTPClient http;
  if (!http.begin(secureClient, url.c_str())) return false;

  JsonDocument request;
  request["activationAttemptId"] = attemptId;
  request["serialNumber"] = gSerialNumber;
  request["firmwareVersion"] = kFirmwareVersion;
  request["configurationVersion"] = "pilot-v1";
  request["operationalCredentialId"] = credentialId;
  request["operationalSecretHash"] = operationalSecretHash;
  String body;
  serializeJson(request, body);

  http.addHeader("Content-Type", "application/json");
  http.addHeader("Authorization", String("Bearer ") + gBootstrapToken);
  const int statusCode = http.POST(body);
  JsonDocument response;
  const bool responseParsed = statusCode == 200
      && !deserializeJson(response, http.getString());
  http.end();
  if (!responseParsed
      || String(response["operationalCredentialId"] | "") != credentialId) {
    Serial.printf("activation rejected status=%d\n", statusCode);
    return false;
  }

  config->deviceToken = credentialId + "." + operationalSecret;
  gPrefs.remove(kKeyBootstrapToken);
  gPrefs.remove(kKeyActivationAttempt);
  gPrefs.remove(kKeyOperationalCredential);
  gPrefs.remove(kKeyOperationalSecret);
  gPrefs.remove(kKeyOperationalSecretHash);
  gBootstrapToken = "";
  Serial.println("bootstrap activation accepted; operational credential staged");
  return true;
}

bool verifyCandidateOperationalApi(const DeviceConfig& config) {
  if (WiFi.status() != WL_CONNECTED
      || WiFi.localIP() == IPAddress(0, 0, 0, 0)
      || config.deviceToken.isEmpty()
      || !isApprovedOperationalApiUrl(config.apiUrl)) {
    return false;
  }
  const bool usesHttps = config.apiUrl.startsWith("https://");
  if (usesHttps && !ensureClockSynchronized()) {
    return false;
  }

  const String healthUrl = deviceHealthUrl(config.apiUrl);
  if (healthUrl.isEmpty()) return false;

  WiFiClient plainClient;
  WiFiClientSecure secureClient;
  WiFiClient* client = &plainClient;
  if (usesHttps) {
    secureClient.setCACert(kCloudflareRootCa);
    client = &secureClient;
  }

  HTTPClient http;
  if (!http.begin(*client, healthUrl.c_str())) return false;
  http.addHeader("Content-Type", "application/json");
  http.addHeader("Authorization", String("Bearer ") + config.deviceToken);
  const String body = String("{\"schemaVersion\":1,\"firmwareVersion\":\"")
      + kFirmwareVersion
      + "\",\"uptimeMilliseconds\":" + String(millis())
      + ",\"sensorStatus\":\"unknown\",\"sensorFault\":null,\"wifiRssiDbm\":"
      + String(constrain(WiFi.RSSI(), -127, 0))
      + ",\"queuedReadingCount\":" + String(gQueueCount)
      + ",\"clockSynchronized\":true,\"droppedReadingCount\":"
      + String(gDroppedReadingCount) + "}";
  const int statusCode = http.POST(body);
  http.end();
  return statusCode == 200;
}
