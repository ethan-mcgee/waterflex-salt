/*
 * WaterFlex Plan C - Arduino Nano ESP32 salt-level sensor firmware.
 *
 * Hardware:
 *   - Arduino Nano ESP32 (ESP32-S3)
 *   - A0221AT / DYP-A02 controlled-UART waterproof ultrasonic sensor
 *
 * Sensor wiring through the ASX00061 Nano Connector Carrier UART port:
 *   Pin 1 VCC -> Nano 3V3
 *   Pin 2 GND -> Nano GND
 *   Pin 3 RX  -> carrier TX / Nano D1 (measurement trigger)
 *   Pin 4 TX  -> carrier RX / Nano D0 (distance response)
 *
 * Faulted reads update device health only and can never update operational fill.
 */

#include <Arduino.h>
#include <ArduinoJson.h>
#include <DNSServer.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <WebServer.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <esp_mac.h>
#include <mbedtls/base64.h>
#include <mbedtls/sha256.h>
#include <time.h>

#include "a02yyuw_uart_parser.h"
#include "cloudflare_root_ca.h"

#ifndef WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
#define WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING 0
#endif

namespace {
constexpr int kSensorRxPin = D0;  // Carrier RX <- sensor TX (white)
constexpr int kSensorTxPin = D1;  // Carrier TX -> sensor RX (yellow)
constexpr uint32_t kSensorBaudRate = 9600;
constexpr uint8_t kSensorTriggerByte = 0x00;
constexpr uint32_t kSensorReadTimeoutMs = 250;
constexpr size_t kSensorRxBufferSize = 256;
constexpr int kSensorMinimumDistanceMm = waterflex::kA02YYUWMinimumDistanceMm;
constexpr int kSensorMaximumDistanceMm = waterflex::kA02YYUWMaximumDistanceMm;
constexpr int kRecoveryPin = D2;

constexpr char kPortalApAddress[] = "192.168.4.1";
constexpr char kPortalApSsidPrefix[] = "WaterFlex-";
constexpr uint8_t kPortalDnsPort = 53;
constexpr uint32_t kPortalIdleTimeoutMs = 10UL * 60UL * 1000UL;
constexpr uint32_t kPortalAbsoluteTimeoutMs = 20UL * 60UL * 1000UL;
constexpr uint32_t kWifiConnectTimeoutMs = 30UL * 1000UL;
constexpr uint32_t kRecoveryReopenMs = 15UL * 60UL * 1000UL;
constexpr uint32_t kRecoveryPortalHoldMs = 5UL * 1000UL;
constexpr uint32_t kFactoryResetHoldMs = 15UL * 1000UL;
constexpr uint32_t kOnboardResetGestureWindowMs = 10UL * 1000UL;
constexpr uint32_t kOnboardResetGestureMagic = 0x57465253;
constexpr uint32_t kDefaultTelemetryIntervalMs = 60UL * 1000UL;
constexpr uint32_t kMinimumHealthIntervalMs = 60UL * 1000UL;
constexpr uint32_t kMinimumTelemetryIntervalSeconds = 1;
constexpr uint32_t kMaximumTelemetryIntervalSeconds = 24UL * 60UL * 60UL;
constexpr uint32_t kClockSyncTimeoutMs = 20UL * 1000UL;
constexpr time_t kMinimumValidEpoch = 1704067200;  // 2024-01-01T00:00:00Z
constexpr size_t kQueueCapacity = 24;
constexpr size_t kUploadBatchSize = 8;
constexpr uint32_t kRetryBaseMs = 5UL * 1000UL;
constexpr uint32_t kRetryMaximumMs = 15UL * 60UL * 1000UL;

constexpr char kFirmwareVersion[] = "wf-uart-pilot-0.1";
constexpr char kDefaultTelemetryUrl[] = "https://telemetry-staging.saltmonitor.dev/api/v1/device/telemetry";

constexpr char kNvsNamespace[] = "wf_prov";
constexpr char kKeySsid[] = "active_ssid";
constexpr char kKeyPassword[] = "active_pwd";
constexpr char kKeyHidden[] = "active_hidden";
constexpr char kKeyPassphrase[] = "setup_pass";
constexpr char kKeyApiUrl[] = "api_url";
constexpr char kKeyDeviceToken[] = "dev_token";
constexpr char kKeyQueueHead[] = "q_head";
constexpr char kKeyQueueCount[] = "q_count";
constexpr char kKeyDroppedCount[] = "q_dropped";
constexpr char kKeyNextSequence[] = "next_seq";
constexpr char kKeyBootstrapToken[] = "boot_token";
constexpr char kKeySerialNumber[] = "serial_no";
constexpr char kKeyActivationAttempt[] = "act_attempt";
constexpr char kKeyOperationalCredential[] = "op_cred_id";
constexpr char kKeyOperationalSecret[] = "op_secret";
constexpr char kKeyOperationalSecretHash[] = "op_hash";

struct WifiProfile {
  String ssid;
  String password;
  bool hidden = false;
};

struct DeviceConfig {
  String apiUrl;
  String deviceToken;
};

struct SensorReadResult {
  SensorReadResult(int distance, const char* fault) : distanceMm(distance), faultCode(fault) {}

  int distanceMm;
  const char* faultCode;

  bool isTrustworthy() const {
    return distanceMm >= kSensorMinimumDistanceMm && distanceMm <= kSensorMaximumDistanceMm;
  }
};

struct QueuedReading {
  char bootId[37];
  uint64_t sequenceNumber;
  uint32_t uptimeMilliseconds;
  int32_t rawDistanceMm;
  int16_t wifiRssiDbm;
  uint8_t quality;
};

waterflex::A02YYUWFrameParser gSensorParser;

enum class ProvisioningState {
  Unprovisioned,
  PortalIdle,
  PortalConnecting,
  PortalError,
  Active
};

Preferences gPrefs;
bool gPrefsInitialized = false;
DNSServer gDnsServer;
WebServer gPortalServer(80);

bool gHasActiveProfile = false;
WifiProfile gActiveProfile;
DeviceConfig gDeviceConfig;
String gBootstrapToken;
String gSerialNumber;

bool gHasCandidateProfile = false;
WifiProfile gCandidateProfile;
DeviceConfig gCandidateDeviceConfig;
bool gCandidateApplyOnSuccess = false;

ProvisioningState gState = ProvisioningState::Unprovisioned;
String gLastError;
String gPortalToken;
String gPortalSsid;

bool gPortalRunning = false;
uint32_t gPortalStartedAtMs = 0;
uint32_t gPortalLastActivityAtMs = 0;

bool gWifiConnectInFlight = false;
uint32_t gWifiConnectStartedAtMs = 0;
uint32_t gLastWifiDisconnectAtMs = 0;

bool gRecoveryButtonDown = false;
bool gRecoveryPortalTriggered = false;
bool gFactoryResetTriggered = false;
uint32_t gRecoveryPressedAtMs = 0;
uint32_t gOnboardResetGestureArmedAtMs = 0;
uint32_t gLastTelemetryAtMs = 0;
uint32_t gLastHealthAtMs = 0;
bool gHasReportedSensorHealth = false;
bool gLastReportedSensorHealthy = false;
String gLastReportedSensorFault;
uint32_t gTelemetryIntervalMs = kDefaultTelemetryIntervalMs;
bool gTelemetryDue = true;
uint64_t gReadingSequenceNumber = 0;
String gBootId;
uint8_t gQueueHead = 0;
uint8_t gQueueCount = 0;
uint32_t gDroppedReadingCount = 0;
uint8_t gUploadFailureCount = 0;
uint32_t gNextUploadAtMs = 0;

RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarker;
RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarkerInverse;

bool ensureClockSynchronized();
bool verifyCandidateOperationalApi(const DeviceConfig& config);
bool readQueuedReading(uint8_t offset, QueuedReading* reading);
bool activateCandidateDevice(DeviceConfig* config);

bool onboardResetGestureIsArmed() {
  return gOnboardResetGestureMarker == kOnboardResetGestureMagic
      && gOnboardResetGestureMarkerInverse == ~kOnboardResetGestureMagic;
}

void disarmOnboardResetGesture() {
  gOnboardResetGestureMarker = 0;
  gOnboardResetGestureMarkerInverse = 0;
  gOnboardResetGestureArmedAtMs = 0;
}

void armOnboardResetGesture() {
  gOnboardResetGestureMarker = kOnboardResetGestureMagic;
  gOnboardResetGestureMarkerInverse = ~kOnboardResetGestureMagic;
  gOnboardResetGestureArmedAtMs = millis();
}

void restartDevice() {
  disarmOnboardResetGesture();
  ESP.restart();
}

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

String stateToString(ProvisioningState state) {
  switch (state) {
    case ProvisioningState::Unprovisioned:
      return "idle";
    case ProvisioningState::PortalIdle:
      return "idle";
    case ProvisioningState::PortalConnecting:
      return "connecting";
    case ProvisioningState::PortalError:
      return "error";
    case ProvisioningState::Active:
      return "connected";
  }
  return "error";
}

String jsonEscape(const String& value) {
  String out;
  out.reserve(value.length() + 8);
  for (size_t i = 0; i < value.length(); ++i) {
    const char c = value[i];
    if (c == '\\' || c == '"') {
      out += '\\';
      out += c;
    } else if (c == '\n') {
      out += "\\n";
    } else if (c == '\r') {
      out += "\\r";
    } else {
      out += c;
    }
  }
  return out;
}

String makePortalToken() {
  String token;
  token.reserve(32);
  for (int i = 0; i < 4; ++i) {
    char block[9];
    snprintf(block, sizeof(block), "%08lx", static_cast<unsigned long>(esp_random()));
    token += block;
  }
  return token;
}

String serialSuffix() {
  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  char suffix[7];
  snprintf(suffix, sizeof(suffix), "%02X%02X%02X", mac[3], mac[4], mac[5]);
  return String(suffix);
}

String hardwareId() {
  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  char value[13];
  snprintf(value, sizeof(value), "%02X%02X%02X%02X%02X%02X",
           mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  return String(value);
}

String base64Encode(const uint8_t* bytes, size_t length, bool urlSafe) {
  size_t outputLength = 0;
  unsigned char output[96]{};
  if (mbedtls_base64_encode(output, sizeof(output) - 1, &outputLength, bytes, length) != 0) {
    return "";
  }
  output[outputLength] = '\0';
  String encoded(reinterpret_cast<char*>(output));
  if (urlSafe) {
    encoded.replace("+", "-");
    encoded.replace("/", "_");
    while (encoded.endsWith("=")) encoded.remove(encoded.length() - 1);
  }
  return encoded;
}

String defaultPortalPassphrase() {
  // This deterministic fallback is compiled out of every pilot/release image.
  return String("WF-") + serialSuffix() + "-SETUP";
}

bool isApprovedOperationalApiUrl(const String& url) {
  if (url == kDefaultTelemetryUrl) {
    return true;
  }
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
  return url.startsWith("http://") || url.startsWith("https://");
#else
  return false;
#endif
}

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

void markPortalActivity() {
  gPortalLastActivityAtMs = millis();
}

void stopPortal() {
  if (!gPortalRunning) {
    return;
  }
  gDnsServer.stop();
  gPortalServer.stop();
  WiFi.softAPdisconnect(true);
  gPortalRunning = false;
}

void setPortalError(const String& errorCode) {
  gLastError = errorCode;
  gState = ProvisioningState::PortalError;
}

void beginWifiConnect(const WifiProfile& profile, bool applyOnSuccess) {
  gHasCandidateProfile = true;
  gCandidateProfile = profile;
  gCandidateApplyOnSuccess = applyOnSuccess;

  const wifi_mode_t mode = gPortalRunning ? WIFI_MODE_APSTA : WIFI_MODE_STA;
  WiFi.mode(mode);
  WiFi.disconnect(true, true);
  delay(50);

  WiFi.begin(gCandidateProfile.ssid.c_str(), gCandidateProfile.password.c_str());
  gWifiConnectInFlight = true;
  gWifiConnectStartedAtMs = millis();
  gState = ProvisioningState::PortalConnecting;
  gLastError = "";
}

void sendNoStoreHeaders() {
  gPortalServer.sendHeader("Cache-Control", "no-store");
  gPortalServer.sendHeader("X-Content-Type-Options", "nosniff");
  gPortalServer.sendHeader("Referrer-Policy", "no-referrer");
  gPortalServer.sendHeader(
      "Content-Security-Policy",
      "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline';");
}

void handlePortalRoot() {
  markPortalActivity();
  sendNoStoreHeaders();

  String html;
  html.reserve(1800);
  html += "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>";
  html += "<title>WaterFlex Setup</title>";
  html += "<style>body{font-family:Arial,sans-serif;background:#f4f7fb;color:#133041;margin:0;padding:20px}main{max-width:560px;margin:0 auto;background:#fff;border-radius:12px;padding:20px;box-shadow:0 8px 24px rgba(9,31,47,.12)}label{display:block;margin:.75rem 0 .25rem}input{width:100%;padding:.65rem;border:1px solid #c8d4dc;border-radius:8px}button{margin-top:1rem;background:#0e6e88;color:#fff;border:none;padding:.7rem 1rem;border-radius:8px}small{color:#5b6e79}code{background:#eef4f8;padding:.1rem .3rem;border-radius:4px}</style>";
  html += "</head><body><main><h1>WaterFlex Wi-Fi Setup</h1><p>Connect this sensor to the customer 2.4 GHz Wi-Fi.</p>";
  html += "<form method='POST' action='/api/v1/configure'>";
  html += "<input type='hidden' name='token' value='" + jsonEscape(gPortalToken) + "'>";
  html += "<label for='ssid'>Wi-Fi name (SSID)</label><input id='ssid' name='ssid' maxlength='32' required>";
  html += "<label for='password'>Wi-Fi password</label><input id='password' name='password' type='password' maxlength='63'>";
  html += "<label><input type='checkbox' name='hidden' value='1'> Hidden network</label>";
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
  html += "<label for='apiUrl'>Telemetry API URL</label><input id='apiUrl' name='apiUrl' maxlength='256' value='" + jsonEscape(gDeviceConfig.apiUrl) + "'>";
  html += "<label for='deviceToken'>Device token</label><input id='deviceToken' name='deviceToken' type='password' maxlength='256' autocomplete='off' placeholder='";
  html += gDeviceConfig.deviceToken.isEmpty() ? "Required" : "Leave blank to keep the stored token";
  html += "'>";
#endif
  html += "<button type='submit'>Configure sensor</button></form>";
  html += "<p><small>Use <code>/api/v1/status</code> to poll setup progress.</small></p></main></body></html>";

  gPortalServer.send(200, "text/html", html);
}

void handlePortalNetworks() {
  markPortalActivity();
  sendNoStoreHeaders();

  int count = WiFi.scanNetworks(false, true);
  if (count < 0) {
    gPortalServer.send(200, "application/json", "{\"networks\":[]}");
    return;
  }

  String body = "{\"networks\":[";
  bool first = true;
  for (int i = 0; i < count; ++i) {
    const String ssid = WiFi.SSID(i);
    if (ssid.isEmpty()) {
      continue;
    }
    const int32_t channel = WiFi.channel(i);
    if (channel < 1 || channel > 14) {
      continue;
    }

    if (!first) {
      body += ",";
    }
    first = false;

    body += "{\"ssid\":\"";
    body += jsonEscape(ssid);
    body += "\",\"rssi\":";
    body += String(WiFi.RSSI(i));
    body += ",\"secure\":";
    body += (WiFi.encryptionType(i) == WIFI_AUTH_OPEN) ? "false" : "true";
    body += "}";
  }
  body += "]}";
  WiFi.scanDelete();

  gPortalServer.send(200, "application/json", body);
}

void handlePortalConfigure() {
  markPortalActivity();
  sendNoStoreHeaders();

  const String token = gPortalServer.arg("token");
  if (token != gPortalToken) {
    gPortalServer.send(403, "application/json", "{\"errorCode\":\"invalid_token\"}");
    return;
  }

  const String ssid = gPortalServer.arg("ssid");
  const String password = gPortalServer.arg("password");
  const String apiUrl = gPortalServer.arg("apiUrl");
  const String deviceToken = gPortalServer.arg("deviceToken");
  const bool hidden = gPortalServer.hasArg("hidden") && gPortalServer.arg("hidden") == "1";

  if (ssid.isEmpty() || ssid.length() > 32) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"invalid_ssid\"}");
    return;
  }
  if (password.length() > 63) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"invalid_password\"}");
    return;
  }
  const String candidateApiUrl = apiUrl.isEmpty() ? kDefaultTelemetryUrl : apiUrl;
  if (candidateApiUrl.length() > 256 || !isApprovedOperationalApiUrl(candidateApiUrl)) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"invalid_api_url\"}");
    return;
  }
  if (deviceToken.length() > 256) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"invalid_device_token\"}");
    return;
  }

  WifiProfile candidate;
  candidate.ssid = ssid;
  candidate.password = password;
  candidate.hidden = hidden;

  gCandidateDeviceConfig.apiUrl = candidateApiUrl;
  gCandidateDeviceConfig.deviceToken = gDeviceConfig.deviceToken;
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
  if (!deviceToken.isEmpty()) gCandidateDeviceConfig.deviceToken = deviceToken;
#endif
  if (gCandidateDeviceConfig.deviceToken.isEmpty() && gBootstrapToken.isEmpty()) {
    gPortalServer.send(409, "application/json", "{\"errorCode\":\"factory_bootstrap_missing\"}");
    return;
  }

  beginWifiConnect(candidate, true);
  gPortalServer.send(202, "application/json", "{\"status\":\"connecting\"}");
}

void handlePortalStatus() {
  markPortalActivity();
  sendNoStoreHeaders();

  String body = "{\"status\":\"";
  body += stateToString(gState);
  body += "\"";
  if (!gLastError.isEmpty()) {
    body += ",\"errorCode\":\"";
    body += jsonEscape(gLastError);
    body += "\"";
  }
  if (WiFi.status() == WL_CONNECTED) {
    body += ",\"ip\":\"";
    body += WiFi.localIP().toString();
    body += "\"";
  }
  body += ",\"hasDeviceToken\":";
  body += gDeviceConfig.deviceToken.isEmpty() ? "false" : "true";
  body += ",\"configured\":";
  body += gHasActiveProfile && !gDeviceConfig.deviceToken.isEmpty() ? "true" : "false";
  body += ",\"hardwareId\":\"";
  body += hardwareId();
  body += "\",\"telemetryIntervalSeconds\":";
  body += String(gTelemetryIntervalMs / 1000UL);
  body += "}";

  gPortalServer.send(200, "application/json", body);
}

void handlePortalRestart() {
  markPortalActivity();
  sendNoStoreHeaders();

  if (gState != ProvisioningState::Active) {
    gPortalServer.send(409, "application/json", "{\"errorCode\":\"not_ready\"}");
    return;
  }

  gPortalServer.send(200, "application/json", "{\"status\":\"restarting\"}");
  delay(200);
  restartDevice();
}

void handleCaptiveRedirect() {
  markPortalActivity();
  gPortalServer.sendHeader("Location", "http://192.168.4.1/", true);
  gPortalServer.send(302, "text/plain", "");
}

void startPortal(const String& reasonCode) {
  if (gPortalRunning) {
    return;
  }

  gPortalToken = makePortalToken();
  gPortalSsid = String(kPortalApSsidPrefix) + serialSuffix();

  const String setupPassphrase = gPrefs.getString(kKeyPassphrase, "");
  String portalPassphrase = setupPassphrase;
  if (portalPassphrase.isEmpty()) {
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
    portalPassphrase = defaultPortalPassphrase();
    Serial.println("WARNING: development-derived setup passphrase enabled");
#else
    setPortalError("factory_setup_secret_missing");
    Serial.println("portal refused: factory-injected setup secret missing");
    return;
#endif
  }

  if (!WiFi.mode(WIFI_MODE_APSTA)) {
    setPortalError("portal_wifi_mode_failed");
    Serial.println("portal failed: could not enable AP+STA mode");
    return;
  }
  if (!WiFi.softAPConfig(IPAddress(192, 168, 4, 1), IPAddress(192, 168, 4, 1), IPAddress(255, 255, 255, 0))) {
    setPortalError("portal_ip_config_failed");
    Serial.println("portal failed: could not configure AP address");
    return;
  }
  if (!WiFi.softAP(gPortalSsid.c_str(), portalPassphrase.c_str(), 1, 0, 1)) {
    setPortalError("portal_start_failed");
    Serial.println("portal failed: could not start SoftAP");
    return;
  }

  gDnsServer.start(kPortalDnsPort, "*", IPAddress(192, 168, 4, 1));

  gPortalServer.on("/", HTTP_GET, handlePortalRoot);
  gPortalServer.on("/api/v1/networks", HTTP_GET, handlePortalNetworks);
  gPortalServer.on("/api/v1/configure", HTTP_POST, handlePortalConfigure);
  gPortalServer.on("/api/v1/status", HTTP_GET, handlePortalStatus);
  gPortalServer.on("/api/v1/restart", HTTP_POST, handlePortalRestart);

  gPortalServer.on("/generate_204", HTTP_ANY, handleCaptiveRedirect);
  gPortalServer.on("/hotspot-detect.html", HTTP_ANY, handleCaptiveRedirect);
  gPortalServer.on("/ncsi.txt", HTTP_ANY, handleCaptiveRedirect);
  gPortalServer.on("/connecttest.txt", HTTP_ANY, handleCaptiveRedirect);
  gPortalServer.onNotFound(handleCaptiveRedirect);

  gPortalServer.begin();
  gPortalRunning = true;
  gPortalStartedAtMs = millis();
  gPortalLastActivityAtMs = gPortalStartedAtMs;
  gState = ProvisioningState::PortalIdle;
  gLastError = reasonCode;

  Serial.printf("portal started ssid=%s ip=%s hidden=false\n",
                gPortalSsid.c_str(),
                WiFi.softAPIP().toString().c_str());
}

void processPortal() {
  if (!gPortalRunning) {
    return;
  }

  gDnsServer.processNextRequest();
  gPortalServer.handleClient();

  const uint32_t now = millis();
  if (now - gPortalLastActivityAtMs >= kPortalIdleTimeoutMs) {
    setPortalError("portal_idle_timeout");
    stopPortal();
    if (!gHasActiveProfile) {
      restartDevice();
    }
    return;
  }

  if (now - gPortalStartedAtMs >= kPortalAbsoluteTimeoutMs) {
    setPortalError("portal_absolute_timeout");
    stopPortal();
    if (!gHasActiveProfile) {
      restartDevice();
    }
  }
}

void processSerialCommands() {
  static String inputBuffer;

  while (Serial.available() > 0) {
    const char c = static_cast<char>(Serial.read());
    if (c == '\r' || c == '\n') {
      String command = inputBuffer;
      inputBuffer = "";
      command.trim();
      if (command.isEmpty()) {
        continue;
      }

      command.toUpperCase();
      if (command == "FACTORY_RESET" || command == "FACTORYRESET" || command == "RESET") {
        Serial.println("factory reset command received");
        performFactoryReset();
      } else if (command == "PORTAL") {
        Serial.println("portal command received");
        startPortal("serial_portal");
      } else {
        Serial.println("unknown command");
      }
      continue;
    }

    if (c >= 32 && c <= 126) {
      inputBuffer += c;
    }
  }
}

void processRecoveryButton() {
  const bool pressed = digitalRead(kRecoveryPin) == LOW;
  const uint32_t now = millis();

  if (pressed && !gRecoveryButtonDown) {
    gRecoveryButtonDown = true;
    gRecoveryPressedAtMs = now;
    gRecoveryPortalTriggered = false;
    gFactoryResetTriggered = false;
    return;
  }

  if (!pressed && gRecoveryButtonDown) {
    gRecoveryButtonDown = false;
    gRecoveryPressedAtMs = 0;
    return;
  }

  if (!pressed) {
    return;
  }

  const uint32_t heldMs = now - gRecoveryPressedAtMs;
  if (!gFactoryResetTriggered && heldMs >= kFactoryResetHoldMs) {
    gFactoryResetTriggered = true;
    performFactoryReset();
    return;
  }

  if (!gRecoveryPortalTriggered && heldMs >= kRecoveryPortalHoldMs) {
    gRecoveryPortalTriggered = true;
    startPortal("manual_recovery");
  }
}

void processOnboardResetGestureWindow() {
  if (gOnboardResetGestureArmedAtMs != 0
      && millis() - gOnboardResetGestureArmedAtMs >= kOnboardResetGestureWindowMs) {
    disarmOnboardResetGesture();
    Serial.println("onboard reset gesture window closed");
  }
}

void processWifiConnection() {
  if (!gWifiConnectInFlight) {
    return;
  }

  const wl_status_t status = WiFi.status();
  if (status == WL_CONNECTED) {
    gWifiConnectInFlight = false;
    gLastWifiDisconnectAtMs = 0;

    if (gCandidateApplyOnSuccess && gHasCandidateProfile) {
      if (gCandidateDeviceConfig.deviceToken.isEmpty()
          && !activateCandidateDevice(&gCandidateDeviceConfig)) {
        gState = ProvisioningState::PortalError;
        gLastError = "activation_failed";
        WiFi.disconnect(false, false);
        Serial.println("candidate rejected: bootstrap activation failed");
        return;
      }
      if (!verifyCandidateOperationalApi(gCandidateDeviceConfig)) {
        gState = ProvisioningState::PortalError;
        gLastError = "candidate_verification_failed";
        WiFi.disconnect(false, false);
        Serial.println("candidate rejected: DHCP/DNS/SNTP/TLS/API verification failed");
        return;
      }
      gActiveProfile = gCandidateProfile;
      gDeviceConfig = gCandidateDeviceConfig;
      gHasActiveProfile = true;
      saveActiveProfile(gActiveProfile);
      saveDeviceConfig(gDeviceConfig);
    }

    gState = ProvisioningState::Active;
    gLastError = "";
    gTelemetryDue = true;
    Serial.printf("wifi connected ip=%s\n", WiFi.localIP().toString().c_str());
    return;
  }

  if (millis() - gWifiConnectStartedAtMs < kWifiConnectTimeoutMs) {
    return;
  }

  gWifiConnectInFlight = false;
  gState = ProvisioningState::PortalError;
  gLastError = "wifi_connect_timeout";
  Serial.println("wifi connect timeout");

  if (!gCandidateApplyOnSuccess && gHasActiveProfile) {
    startPortal("wifi_connect_timeout");
  }
}

void processAutoRecoveryPortal() {
  if (!gHasActiveProfile || gPortalRunning || gWifiConnectInFlight) {
    return;
  }
  if (WiFi.status() == WL_CONNECTED) {
    gLastWifiDisconnectAtMs = 0;
    return;
  }

  const uint32_t now = millis();
  if (gLastWifiDisconnectAtMs == 0) {
    gLastWifiDisconnectAtMs = now;
    return;
  }
  if (now - gLastWifiDisconnectAtMs >= kRecoveryReopenMs) {
    startPortal("auto_recovery");
  }
}

void connectWithSavedProfile() {
  if (!gHasActiveProfile) {
    return;
  }
  beginWifiConnect(gActiveProfile, false);
}

String makeBootId() {
  const uint32_t a = static_cast<uint32_t>(esp_random());
  const uint16_t b = static_cast<uint16_t>(esp_random() & 0xFFFFU);
  const uint16_t c = static_cast<uint16_t>((esp_random() & 0x0FFFU) | 0x4000U);
  const uint16_t d = static_cast<uint16_t>((esp_random() & 0x3FFFU) | 0x8000U);
  const uint64_t e = (static_cast<uint64_t>(esp_random()) << 16)
      | static_cast<uint64_t>(esp_random() & 0xFFFFU);

  char guid[37];
  snprintf(guid, sizeof(guid), "%08lx-%04x-%04x-%04x-%012llx",
           static_cast<unsigned long>(a),
           static_cast<unsigned int>(b),
           static_cast<unsigned int>(c),
           static_cast<unsigned int>(d),
           static_cast<unsigned long long>(e & 0xFFFFFFFFFFFFULL));
  return String(guid);
}

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

String deviceHealthUrl(const String& telemetryUrl) {
  String url = telemetryUrl;
  constexpr char telemetrySuffix[] = "telemetry";
  if (!url.endsWith(telemetrySuffix)) {
    return "";
  }
  url.remove(url.length() - strlen(telemetrySuffix));
  url += "health";
  return url;
}

String activationUrl(const String& telemetryUrl) {
  String url = telemetryUrl;
  constexpr char telemetrySuffix[] = "telemetry";
  if (!url.endsWith(telemetrySuffix)) return "";
  url.remove(url.length() - strlen(telemetrySuffix));
  url += "activate";
  return url;
}

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
  request["hardwareId"] = hardwareId();
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

String deviceHealthUrl() {
  return deviceHealthUrl(gDeviceConfig.apiUrl);
}

bool sendDeviceHealth(bool sensorHealthy, const char* sensorFault = nullptr) {
  if (WiFi.status() != WL_CONNECTED || gDeviceConfig.deviceToken.isEmpty()) {
    return false;
  }

  const String healthUrl = deviceHealthUrl();
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

void initializeProvisioning() {
  ensurePrefsReady();

  bool onboardResetSetupRequested = false;
  if (onboardResetGestureIsArmed()) {
    disarmOnboardResetGesture();
    clearActiveProfile();
    clearDeviceConfig();
    onboardResetSetupRequested = true;
    Serial.println("onboard RESET gesture recognized; provisioning settings cleared");
  } else {
    armOnboardResetGesture();
    Serial.println("onboard RESET gesture armed for 10 seconds");
  }

  gHasActiveProfile = loadActiveProfile(&gActiveProfile);
  gDeviceConfig = loadDeviceConfig();
  gBootstrapToken = gPrefs.getString(kKeyBootstrapToken, "");
  gSerialNumber = gPrefs.getString(kKeySerialNumber, "");
  loadQueueState();
  gCandidateDeviceConfig = gDeviceConfig;

  Serial.printf("device hardwareId=%s wifiConfigured=%s tokenConfigured=%s bootstrapConfigured=%s\n",
                hardwareId().c_str(),
                gHasActiveProfile ? "true" : "false",
                gDeviceConfig.deviceToken.isEmpty() ? "false" : "true",
                (!gBootstrapToken.isEmpty() && !gSerialNumber.isEmpty()) ? "true" : "false");

  if (onboardResetSetupRequested) {
    startPortal("onboard_reset");
  } else if (!gHasActiveProfile || gDeviceConfig.deviceToken.isEmpty()) {
    startPortal(gHasActiveProfile ? "missing_device_token" : "first_boot");
  } else {
    connectWithSavedProfile();
  }
}
}  // namespace

void setup() {
  Serial.begin(115200);  // USB diagnostics
  pinMode(kRecoveryPin, INPUT_PULLUP);
  Serial1.setRxBufferSize(kSensorRxBufferSize);
  Serial1.begin(kSensorBaudRate, SERIAL_8N1, kSensorRxPin, kSensorTxPin);

  initializeProvisioning();
  gBootId = makeBootId();
  Serial.printf("bootId=%s\n", gBootId.c_str());

  // Tank calibration is owned by the commissioning session. Signed A/B OTA remains
  // gated by the production-security work instruction and is not enabled here.
}

void loop() {
  processSerialCommands();
  processOnboardResetGestureWindow();
  processRecoveryButton();
  processPortal();
  processWifiConnection();
  processAutoRecoveryPortal();
  processQueuedUploads();

  const SensorReadResult sensorRead = readSensor();
  if (sensorRead.isTrustworthy()) {
    Serial.printf("distance=%d mm\n", sensorRead.distanceMm);
  } else {
    Serial.printf("sensor read error fault=%s\n", sensorRead.faultCode);
  }
  processTelemetry(sensorRead);

  delay(1000);  // Bench cadence; production reports ~hourly plus events.
}
