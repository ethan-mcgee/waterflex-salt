#include "wifi_connection.h"

#include <Arduino.h>
#include <WiFi.h>

#include "captive_portal.h"
#include "config.h"
#include "device_activation.h"
#include "state.h"
#include "storage.h"

void beginWifiConnect(const WifiProfile& profile, bool applyOnSuccess) {
  gHasCandidateProfile = true;
  gCandidateProfile = profile;
  gCandidateApplyOnSuccess = applyOnSuccess;

  // Stay in AP+STA mode while the portal's SoftAP is up, so a candidate
  // connect started from a portal submission doesn't drop the portal's own
  // access point out from under the phone that's still talking to it.
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

void connectWithSavedProfile() {
  if (!gHasActiveProfile) {
    return;
  }
  beginWifiConnect(gActiveProfile, false);
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

  // Only reopen the portal for a saved-profile reconnect attempt (not a
  // portal-submitted candidate, which already has the portal open and its
  // own error path back to the setup page).
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
