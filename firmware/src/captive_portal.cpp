#include "captive_portal.h"

#include <WiFi.h>

#include "config.h"
#include "identity_utils.h"
#include "reset_control.h"
#include "state.h"
#include "wifi_connection.h"

namespace {

void sendNoStoreHeaders() {
  gPortalServer.sendHeader("Cache-Control", "no-store");
  gPortalServer.sendHeader("X-Content-Type-Options", "nosniff");
  gPortalServer.sendHeader("Referrer-Policy", "no-referrer");
  gPortalServer.sendHeader(
      "Content-Security-Policy",
      "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline';");
}

// Raw-string blocks below are intentionally kept out of the Arduino `String` builder
// above them: they contain no device-specific values, so they can't leak anything and
// are cheaper to read/maintain as literal CSS/JS rather than escaped C++ string pieces.
constexpr char kPortalStyle[] =
    "body{font-family:Arial,sans-serif;background:#f4f7fb;color:#133041;margin:0;padding:20px}"
    "main{max-width:560px;margin:0 auto;background:#fff;border-radius:12px;padding:20px;box-shadow:0 8px 24px rgba(9,31,47,.12)}"
    "label{display:block;margin:.75rem 0 .25rem}"
    "input{width:100%;padding:.65rem;border:1px solid #c8d4dc;border-radius:8px;box-sizing:border-box}"
    "button{margin-top:1rem;background:#0e6e88;color:#fff;border:none;padding:.7rem 1rem;border-radius:8px;font-size:1rem}"
    "button:disabled{background:#9fb3bc;cursor:default}"
    "small{color:#5b6e79}"
    "code{background:#eef4f8;padding:.1rem .3rem;border-radius:4px}"
    "#status{display:none;margin-top:1rem;padding:.85rem 1rem;border-radius:8px;border:1px solid transparent;align-items:center;gap:.6rem}"
    "#status.visible{display:flex}"
    "#status.connecting{background:#eaf3f7;border-color:#bcd7e2;color:#0e5570}"
    "#status.connected{background:#e9f7ee;border-color:#b7e3c6;color:#1b6b3c}"
    "#status.error{background:#fdecec;border-color:#f3bcbc;color:#9b2f2f}"
    "#status .spinner{width:16px;height:16px;flex:none;border-radius:50%;border:2px solid rgba(14,110,136,.25);border-top-color:#0e6e88;animation:spin .8s linear infinite}"
    "@media (prefers-reduced-motion: reduce){#status .spinner{animation:none}}"
    "@keyframes spin{to{transform:rotate(360deg)}}"
    "#status .msg{flex:1;font-size:.92rem;line-height:1.4}"
    "#status button{margin-top:0;padding:.4rem .75rem;font-size:.85rem;background:#9b2f2f}";

constexpr char kPortalScript[] = R"JS(
(function () {
  const form = document.getElementById('wifi-form');
  const status = document.getElementById('status');
  const spinner = status.querySelector('.spinner');
  const msg = status.querySelector('.msg');
  const retry = document.getElementById('retry');
  const ssidField = document.getElementById('ssid');
  const fields = form.querySelectorAll('input');
  const submitBtn = form.querySelector('button[type="submit"]');
  let pollTimer = null;

  const ERROR_MESSAGES = {
    invalid_ssid: 'Enter the Wi-Fi network name.',
    invalid_password: 'That password is too long.',
    invalid_token: 'This setup page expired. Reload the page and try again.',
    factory_bootstrap_missing: 'This sensor has no bootstrap credential and no device token. Contact support.',
    wifi_connect_timeout: 'Could not join that network. Double-check the Wi-Fi name and password, then try again.',
    activation_failed: 'Connected to Wi-Fi, but the sensor could not activate. Confirm a technician has reserved this sensor, then try again.',
    candidate_verification_failed: 'Connected to Wi-Fi, but could not reach the WaterFlex service. Check the network\'s internet connection and try again.'
  };

  function friendlyError(code) {
    return ERROR_MESSAGES[code] || ('Setup failed (' + code + '). Try again.');
  }

  function setFieldsEnabled(enabled) {
    fields.forEach(function (field) { field.disabled = !enabled; });
    submitBtn.disabled = !enabled;
  }

  function showStatus(kind, text, withRetry) {
    status.className = 'visible ' + kind;
    spinner.style.display = kind === 'connecting' ? 'block' : 'none';
    msg.textContent = text;
    retry.style.display = withRetry ? 'inline-block' : 'none';
  }

  function stopPolling() {
    if (pollTimer) {
      clearTimeout(pollTimer);
      pollTimer = null;
    }
  }

  function poll() {
    fetch('/api/v1/status', { cache: 'no-store' })
      .then(function (response) { return response.json(); })
      .then(function (data) {
        if (data.status === 'connected') {
          stopPolling();
          showStatus('connected', 'Connected and activated' + (data.ip ? ' — ' + data.ip : '') + '. Setup is complete.', false);
        } else if (data.status === 'error') {
          stopPolling();
          setFieldsEnabled(true);
          showStatus('error', friendlyError(data.errorCode || 'unknown'), true);
        } else {
          showStatus('connecting', 'Joining ' + (ssidField.value || 'the network') + '…', false);
          pollTimer = setTimeout(poll, 1500);
        }
      })
      .catch(function () {
        pollTimer = setTimeout(poll, 2000);
      });
  }

  retry.addEventListener('click', function () {
    status.classList.remove('visible');
    setFieldsEnabled(true);
  });

  form.addEventListener('submit', function (event) {
    event.preventDefault();
    stopPolling();
    const body = new URLSearchParams(new FormData(form));
    setFieldsEnabled(false);
    showStatus('connecting', 'Joining ' + (ssidField.value || 'the network') + '…', false);

    fetch(form.action, { method: 'POST', body: body })
      .then(function (response) {
        return response.json().then(function (data) { return { ok: response.ok, data: data }; });
      })
      .then(function (result) {
        if (!result.ok) {
          setFieldsEnabled(true);
          showStatus('error', friendlyError(result.data.errorCode || 'unknown'), true);
          return;
        }
        pollTimer = setTimeout(poll, 1000);
      })
      .catch(function () {
        setFieldsEnabled(true);
        showStatus('error', 'Could not reach the sensor. Make sure your phone is still joined to its Wi-Fi network, then try again.', true);
      });
  });
})();
)JS";

String buildPortalHtml(bool preview) {
  String html;
  html.reserve(4400);
  html += "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>";
  html += "<title>WaterFlex Setup</title>";
  html += "<style>";
  html += kPortalStyle;
  html += "</style>";
  html += "</head><body><main><h1>WaterFlex Wi-Fi Setup</h1><p>Connect this sensor to the customer 2.4 GHz Wi-Fi.</p>";
  if (preview) {
    html += "<p><small><strong>Development preview:</strong> configuration is read-only and telemetry remains connected.</small></p>";
  }
  html += "<form id='wifi-form' method='POST' action='/api/v1/configure'>";
  html += "<input type='hidden' name='token' value='" + jsonEscape(gPortalToken) + "'>";
  html += "<label for='ssid'>Wi-Fi name (SSID)</label><input id='ssid' name='ssid' maxlength='32' required>";
  html += "<label for='password'>Wi-Fi password</label><input id='password' name='password' type='password' maxlength='63'>";
  html += preview
      ? "<button type='submit' disabled>Connect to Wi-Fi</button>"
      : "<button type='submit'>Connect to Wi-Fi</button>";
  html += "<div id='status' role='status' aria-live='polite'>";
  html += "<span class='spinner'></span><span class='msg'></span>";
  html += "<button type='button' id='retry' style='display:none'>Try again</button>";
  html += "</div>";
  html += "</form>";
  html += "<p><small>Status updates automatically above once you submit.</small></p></main>";
  html += "<script>";
  html += kPortalScript;
  html += "</script>";
  html += "</body></html>";

  return html;
}

void handlePortalRoot() {
  markPortalActivity();
  sendNoStoreHeaders();

  gPortalServer.send(200, "text/html", buildPortalHtml(false));
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

  if (ssid.isEmpty() || ssid.length() > 32) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"invalid_ssid\"}");
    return;
  }
  if (password.length() > 63) {
    gPortalServer.send(400, "application/json", "{\"errorCode\":\"invalid_password\"}");
    return;
  }
  WifiProfile candidate;
  candidate.ssid = ssid;
  candidate.password = password;

  gCandidateDeviceConfig.apiUrl = kDefaultTelemetryUrl;
  gCandidateDeviceConfig.deviceToken = gDeviceConfig.deviceToken;
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
  body += ",\"serialNumber\":\"";
  body += jsonEscape(gSerialNumber);
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

void registerPortalRoutes() {
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
}

#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
constexpr uint16_t kPortalPreviewPort = 8080;
WebServer gPortalPreviewServer(kPortalPreviewPort);
bool gPortalPreviewRunning = false;
bool gPortalPreviewRoutesRegistered = false;
uint32_t gPortalPreviewStartedAtMs = 0;
uint32_t gPortalPreviewLastActivityAtMs = 0;

void sendPortalPreviewHeaders() {
  gPortalPreviewServer.sendHeader("Cache-Control", "no-store");
  gPortalPreviewServer.sendHeader("X-Content-Type-Options", "nosniff");
  gPortalPreviewServer.sendHeader("Referrer-Policy", "no-referrer");
  gPortalPreviewServer.sendHeader(
      "Content-Security-Policy",
      "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline';");
}

void markPortalPreviewActivity() {
  gPortalPreviewLastActivityAtMs = millis();
}

void handlePortalPreviewRoot() {
  markPortalPreviewActivity();
  sendPortalPreviewHeaders();
  gPortalPreviewServer.send(200, "text/html", buildPortalHtml(true));
}

void handlePortalPreviewReadOnly() {
  markPortalPreviewActivity();
  sendPortalPreviewHeaders();
  gPortalPreviewServer.send(403, "application/json", "{\"errorCode\":\"preview_read_only\"}");
}

void handlePortalPreviewNotFound() {
  markPortalPreviewActivity();
  sendPortalPreviewHeaders();
  gPortalPreviewServer.send(404, "text/plain", "Not found");
}

void registerPortalPreviewRoutes() {
  if (gPortalPreviewRoutesRegistered) {
    return;
  }
  gPortalPreviewServer.on("/", HTTP_GET, handlePortalPreviewRoot);
  gPortalPreviewServer.on("/api/v1/configure", HTTP_POST, handlePortalPreviewReadOnly);
  gPortalPreviewServer.on("/api/v1/restart", HTTP_POST, handlePortalPreviewReadOnly);
  gPortalPreviewServer.onNotFound(handlePortalPreviewNotFound);
  gPortalPreviewRoutesRegistered = true;
}

void stopPortalPreview(const char* reason) {
  if (!gPortalPreviewRunning) {
    return;
  }
  gPortalPreviewServer.stop();
  gPortalPreviewRunning = false;
  Serial.printf("portal preview stopped: %s\n", reason);
}

void processPortalPreview() {
  if (!gPortalPreviewRunning) {
    return;
  }
  if (WiFi.status() != WL_CONNECTED) {
    stopPortalPreview("Wi-Fi disconnected");
    return;
  }

  gPortalPreviewServer.handleClient();
  const uint32_t now = millis();
  if (now - gPortalPreviewLastActivityAtMs >= kPortalIdleTimeoutMs) {
    stopPortalPreview("idle timeout");
    return;
  }
  if (now - gPortalPreviewStartedAtMs >= kPortalAbsoluteTimeoutMs) {
    stopPortalPreview("absolute timeout");
  }
}
#endif

}  // namespace

void markPortalActivity() {
  gPortalLastActivityAtMs = millis();
}

void stopPortal() {
  if (!gPortalRunning) {
    return;
  }

  gPortalServer.stop();
  gDnsServer.stop();
  WiFi.softAPdisconnect(true);
  gPortalRunning = false;
}

void setPortalError(const String& errorCode) {
  gLastError = errorCode;
  gState = ProvisioningState::PortalError;
}

void startPortal(const String& reasonCode) {
  if (gPortalRunning) {
    Serial.println("portal already running");
    return;
  }

  gPortalToken = makePortalToken();
  gPortalSsid = gSerialNumber;

  const String setupPassphrase = gPrefs.getString(kKeyPassphrase, "");
  String portalPassphrase = setupPassphrase;
  if (gPortalSsid.isEmpty()) {
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
    gPortalSsid = "WF-NANO-DEVELOPMENT";
#else
    setPortalError("factory_identity_missing");
    Serial.println("portal refused: factory-injected serial number missing");
    return;
#endif
  }
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

  registerPortalRoutes();

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

#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
void startPortalPreview() {
  if (WiFi.status() != WL_CONNECTED) {
    Serial.println("portal preview unavailable: Wi-Fi is not connected");
    return;
  }

  registerPortalPreviewRoutes();
  if (gPortalPreviewRunning) {
    gPortalPreviewServer.stop();
  }
  gPortalPreviewServer.begin();
  gPortalPreviewRunning = true;
  gPortalPreviewStartedAtMs = millis();
  gPortalPreviewLastActivityAtMs = gPortalPreviewStartedAtMs;

  Serial.printf("portal preview started url=http://%s:%u/ telemetry=preserved readOnly=true\n",
                WiFi.localIP().toString().c_str(),
                kPortalPreviewPort);
}
#endif

void processPortal() {
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
  processPortalPreview();
#endif

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
