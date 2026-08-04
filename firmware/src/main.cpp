/*
 * WaterFlex Plan C - Arduino Nano ESP32 salt-level sensor firmware (skeleton).
 *
 * Hardware:
 *   - Arduino Nano ESP32 (ESP32-S3)
 *   - DFRobot A02YYUW (SEN0311) ultrasonic sensor, UART @ 9600 8N1
 *
 * Wiring (see AI-Plans/plan-c-arduino-nano-esp32.md):
 *   A02YYUW VCC -> 3V3
 *   A02YYUW GND -> GND
 *   A02YYUW TX  -> D4  (Serial1 RX)
 *   A02YYUW RX  -> not connected (floating = processed/stable output mode)
 *
 * This skeleton implements the verified UART read path and leaves clearly marked
 * TODOs for Wi-Fi provisioning, HTTPS telemetry, calibration, and OTA.
 */

#include <Arduino.h>
#include <ArduinoJson.h>
#include <DNSServer.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <WebServer.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <esp_attr.h>
#include <esp_mac.h>
#include <time.h>

#include "cloudflare_root_ca.h"

namespace {
constexpr int kSensorRxPin = D4;      // A02YYUW TX -> Nano RX
constexpr int kSensorTxPin = D5;      // Assigned to Serial1 but not physically connected
constexpr uint32_t kSensorBaud = 9600;
constexpr uint8_t kFrameHeader = 0xFF;
constexpr uint32_t kReadTimeoutMs = 200;
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
constexpr uint32_t kMinimumTelemetryIntervalSeconds = 1;
constexpr uint32_t kMaximumTelemetryIntervalSeconds = 24UL * 60UL * 60UL;
constexpr uint32_t kClockSyncTimeoutMs = 20UL * 1000UL;
constexpr time_t kMinimumValidEpoch = 1704067200;  // 2024-01-01T00:00:00Z

constexpr char kFirmwareVersion[] = "wf-dev-telemetry-0.1";
constexpr char kDefaultTelemetryUrl[] = "https://telemetry-staging.saltmonitor.dev/api/v1/device/telemetry";

constexpr char kNvsNamespace[] = "wf_prov";
constexpr char kKeySsid[] = "active_ssid";
constexpr char kKeyPassword[] = "active_pwd";
constexpr char kKeyHidden[] = "active_hidden";
constexpr char kKeyPassphrase[] = "setup_pass";
constexpr char kKeyApiUrl[] = "api_url";
constexpr char kKeyDeviceToken[] = "dev_token";

struct WifiProfile {
  String ssid;
  String password;
  bool hidden = false;
};

struct DeviceConfig {
  String apiUrl;
  String deviceToken;
};

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
uint32_t gTelemetryIntervalMs = kDefaultTelemetryIntervalMs;
bool gTelemetryDue = true;
uint64_t gReadingSequenceNumber = 0;
String gBootId;

RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarker;
RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarkerInverse;

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

// Returns distance in millimetres, or -1 on an invalid or timed-out frame.
int readDistanceMm() {
  const uint32_t start = millis();
  while (millis() - start < kReadTimeoutMs) {
    if (Serial1.read() != kFrameHeader) {
      continue;
    }
    uint8_t buf[3];
    if (Serial1.readBytes(buf, 3) != 3) {
      return -1;
    }
    const uint8_t checksum = static_cast<uint8_t>(kFrameHeader + buf[0] + buf[1]);
    if (checksum != buf[2]) {
      return -1;  // Bad checksum
    }
    return (static_cast<int>(buf[0]) << 8) | buf[1];
  }
  return -1;  // Timeout
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

String defaultPortalPassphrase() {
  // Development fallback only. Factory flow should inject a per-device setup passphrase.
  return String("WF-") + serialSuffix() + "-SETUP";
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
  html += "<label for='apiUrl'>Telemetry API URL</label><input id='apiUrl' name='apiUrl' maxlength='256' value='" + jsonEscape(gDeviceConfig.apiUrl) + "'>";
  html += "<label for='deviceToken'>Device token</label><input id='deviceToken' name='deviceToken' type='password' maxlength='256' autocomplete='off' placeholder='";
  html += gDeviceConfig.deviceToken.isEmpty() ? "Required" : "Leave blank to keep the stored token";
  html += "'>";
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
  if (apiUrl.length() > 256
      || (!apiUrl.isEmpty()
          && !apiUrl.startsWith("http://")
          && !apiUrl.startsWith("https://"))) {
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

  gCandidateDeviceConfig.apiUrl = apiUrl.isEmpty() ? kDefaultTelemetryUrl : apiUrl;
  gCandidateDeviceConfig.deviceToken = deviceToken.isEmpty()
      ? gDeviceConfig.deviceToken
      : deviceToken;
  if (gCandidateDeviceConfig.deviceToken.isEmpty()) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"device_token_required\"}");
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
  body += serialSuffix();
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
  const String portalPassphrase = setupPassphrase.isEmpty()
      ? defaultPortalPassphrase()
      : setupPassphrase;

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

int syntheticDistanceMm() {
  return 850 + static_cast<int>((millis() / 1000UL) % 120UL);
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

String telemetryBatchPayload(int distanceMm, bool synthetic, uint64_t sequenceNumber) {
  const int clampedDistanceMm = distanceMm < 30 ? 30 : (distanceMm > 4500 ? 4500 : distanceMm);
  const int wifiRssiDbm = WiFi.RSSI() > 0 ? 0 : (WiFi.RSSI() < -127 ? -127 : WiFi.RSSI());
  String body;
  body.reserve(520);
  body += "{\"schemaVersion\":1,\"firmwareVersion\":\"";
  body += kFirmwareVersion;
  body += "\",\"readings\":[{\"bootId\":\"";
  body += gBootId;
  body += "\",\"sequenceNumber\":";
  body += String(static_cast<unsigned long long>(sequenceNumber));
  body += ",\"uptimeMilliseconds\":";
  body += String(millis());
  body += ",\"rawDistanceMm\":";
  body += String(clampedDistanceMm);
  body += ",\"quality\":";
  body += synthetic ? "70" : "90";
  body += ",\"sampleCount\":1,\"wifiRssiDbm\":";
  body += String(wifiRssiDbm);
  body += ",\"errorFlags\":";
  body += synthetic ? "[\"synthetic_distance\"]" : "[]";
  body += "}] }";
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

void sendTelemetry(int distanceMm, bool synthetic) {
  if (WiFi.status() != WL_CONNECTED) {
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

  const bool usesHttps = gDeviceConfig.apiUrl.startsWith("https://");
  if (usesHttps && !ensureClockSynchronized()) {
    Serial.println("telemetry skipped: clock synchronization required for TLS");
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

  const uint64_t sequenceNumber = gReadingSequenceNumber;

  http.addHeader("Content-Type", "application/json");
  http.addHeader("Authorization", String("Bearer ") + gDeviceConfig.deviceToken);
  const String body = telemetryBatchPayload(distanceMm, synthetic, sequenceNumber);
  const int statusCode = http.POST(body);
  if (statusCode == 200) {
    JsonDocument acknowledgement;
    const DeserializationError jsonError = deserializeJson(acknowledgement, http.getString());
    const uint32_t nextIntervalSeconds = acknowledgement["nextReportIntervalSeconds"] | 0;
    if (!jsonError
        && nextIntervalSeconds >= kMinimumTelemetryIntervalSeconds
        && nextIntervalSeconds <= kMaximumTelemetryIntervalSeconds) {
      gTelemetryIntervalMs = nextIntervalSeconds * 1000UL;
    }
    gReadingSequenceNumber++;
    Serial.printf("telemetry post status=%d seq=%llu synthetic=%s distance=%d next=%lus\n",
                  statusCode,
                  static_cast<unsigned long long>(sequenceNumber),
                  synthetic ? "true" : "false",
                  distanceMm,
                  static_cast<unsigned long>(gTelemetryIntervalMs / 1000UL));
  } else if (statusCode > 0) {
    Serial.printf("telemetry rejected status=%d seq=%llu\n",
                  statusCode,
                  static_cast<unsigned long long>(sequenceNumber));
  } else {
    Serial.printf("telemetry post failed error=%s\n", http.errorToString(statusCode).c_str());
  }
  http.end();
}

void processTelemetry(int distanceMm) {
  const uint32_t now = millis();
  if (!gTelemetryDue && now - gLastTelemetryAtMs < gTelemetryIntervalMs) {
    return;
  }
  gLastTelemetryAtMs = now;
  gTelemetryDue = false;

  const bool synthetic = distanceMm < 0;
  sendTelemetry(synthetic ? syntheticDistanceMm() : distanceMm, synthetic);
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
  gCandidateDeviceConfig = gDeviceConfig;

  Serial.printf("device hardwareId=%s wifiConfigured=%s tokenConfigured=%s\n",
                serialSuffix().c_str(),
                gHasActiveProfile ? "true" : "false",
                gDeviceConfig.deviceToken.isEmpty() ? "false" : "true");

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

  Serial1.begin(kSensorBaud, SERIAL_8N1, kSensorRxPin, kSensorTxPin);
  Serial1.setTimeout(kReadTimeoutMs);

  initializeProvisioning();
  gBootId = makeBootId();
  Serial.printf("bootId=%s\n", gBootId.c_str());

  // TODO(plan-c C2): validate candidate Wi-Fi using DHCP, DNS, SNTP, and API health before commit.
  // TODO(plan-c C2): implement bootstrap activation and transition to per-device operational credentials.
  // TODO(plan-c C2): establish HTTPS with a unique per-device bearer token.
  // TODO(plan-c C2): implement tank-depth calibration and secure OTA with rollback.
}

void loop() {
  processSerialCommands();
  processOnboardResetGestureWindow();
  processRecoveryButton();
  processPortal();
  processWifiConnection();
  processAutoRecoveryPortal();

  const int distanceMm = readDistanceMm();
  if (distanceMm >= 0) {
    Serial.printf("distance=%d mm\n", distanceMm);
  } else {
    Serial.println("sensor read error");
  }
  processTelemetry(distanceMm);

  delay(1000);  // Bench cadence; production reports ~hourly plus events.
}
