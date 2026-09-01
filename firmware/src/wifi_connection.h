// Wi-Fi station connection lifecycle: kicking off connects, polling their
// outcome, and auto-recovery when a saved connection drops.
#pragma once

#include "types.h"

// Starts an asynchronous WiFi.begin() against `profile`. When
// `applyOnSuccess` is true (a portal-submitted candidate), a successful
// connect will trigger activation/verification and, if those pass, replace
// the active profile/config; when false (reconnecting a saved profile), a
// successful connect is applied immediately with no re-activation.
void beginWifiConnect(const WifiProfile& profile, bool applyOnSuccess);
// Begins a connection attempt using the already-active Wi-Fi profile. No-op
// if no profile has been provisioned yet.
void connectWithSavedProfile();
// Polls the in-flight connection started by beginWifiConnect(). On success,
// runs candidate activation/verification (if applicable) and transitions to
// Active; on timeout, transitions to PortalError and reopens the portal if
// the failed candidate wasn't overwriting a working saved profile.
void processWifiConnection();
// Watches for the active profile's connection dropping and silently
// reopens the portal after it stays down past kRecoveryReopenMs, so the
// device is reachable for re-setup without requiring a physical reset.
void processAutoRecoveryPortal();
