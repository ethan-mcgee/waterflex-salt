// SoftAP captive portal: setup page, JSON routes, DNS capture, and lifecycle.
#pragma once

#include <Arduino.h>

// Resets the portal idle timer. Call on every request the portal handles so
// an actively-used portal isn't torn down mid-setup.
void markPortalActivity();
// Tears down the SoftAP, DNS capture, and HTTP server if the portal is running.
void stopPortal();
// Records `errorCode` as the last error and moves the state machine to
// PortalError, so the setup page's status poll can surface it to the user.
void setPortalError(const String& errorCode);
// Brings up the SoftAP captive portal (AP+STA mode, DNS capture, HTTP
// routes) for Wi-Fi setup. `reasonCode` records why the portal opened (e.g.
// "first_boot", "wifi_connect_timeout") for the status endpoint. No-op if
// the portal is already running; refuses to start if factory identity
// (serial number/setup passphrase) is missing on non-development builds.
void startPortal(const String& reasonCode);
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
// Development-only: serves a read-only copy of the setup page over the
// existing Wi-Fi connection so the portal UI can be exercised without
// dropping the device off the network.
void startPortalPreview();
#endif
// Services the portal's DNS capture and HTTP server, and the dev preview
// server when built with WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING. Closes
// the portal on idle/absolute timeout, restarting the device if no active
// profile was ever established (so it re-enters the portal on boot).
void processPortal();
