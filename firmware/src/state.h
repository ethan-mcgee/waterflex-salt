// Global mutable device state shared across the WaterFlex firmware modules.
//
// This firmware runs as a single-threaded Arduino sketch, so plain externs
// (rather than passing state through every call) match how the rest of the
// codebase already treats NVS-backed configuration and connection status.
#pragma once

#include <DNSServer.h>
#include <Preferences.h>
#include <WebServer.h>
#include <WiFi.h>

#include "types.h"

// --- NVS handle and portal servers ---
extern Preferences gPrefs;
extern bool gPrefsInitialized;
extern DNSServer gDnsServer;
extern WebServer gPortalServer;

// --- Active Wi-Fi profile / device config / factory identity ---
extern bool gHasActiveProfile;
extern WifiProfile gActiveProfile;
extern DeviceConfig gDeviceConfig;
extern String gBootstrapToken;
extern String gSerialNumber;

// --- Candidate profile/config staged by the portal, pending connect+verify ---
extern bool gHasCandidateProfile;
extern WifiProfile gCandidateProfile;
extern DeviceConfig gCandidateDeviceConfig;
extern bool gCandidateApplyOnSuccess;

// --- Provisioning state machine ---
extern ProvisioningState gState;
extern String gLastError;
extern String gPortalToken;
extern String gPortalSsid;

// --- Captive portal lifecycle ---
extern bool gPortalRunning;
extern uint32_t gPortalStartedAtMs;
extern uint32_t gPortalLastActivityAtMs;

// --- Wi-Fi connection ---
extern bool gWifiConnectInFlight;
extern uint32_t gWifiConnectStartedAtMs;
extern uint32_t gLastWifiDisconnectAtMs;

// --- Recovery button / onboard reset gesture ---
extern bool gRecoveryButtonDown;
extern bool gRecoveryPortalTriggered;
extern bool gFactoryResetTriggered;
extern uint32_t gRecoveryPressedAtMs;
extern uint32_t gOnboardResetGestureArmedAtMs;

// --- Telemetry / health reporting / upload queue ---
extern uint32_t gLastTelemetryAtMs;
extern uint32_t gLastHealthAtMs;
extern bool gHasReportedSensorHealth;
extern bool gLastReportedSensorHealthy;
extern String gLastReportedSensorFault;
extern uint32_t gTelemetryIntervalMs;
extern bool gTelemetryDue;
extern uint64_t gReadingSequenceNumber;
extern String gBootId;
extern uint8_t gQueueHead;
extern uint8_t gQueueCount;
extern uint32_t gDroppedReadingCount;
extern uint8_t gUploadFailureCount;
extern uint32_t gNextUploadAtMs;

// RTC_NOINIT memory survives a plain reset (but not a power cycle), so this
// pair doubles as the onboard double-RESET gesture's persisted arm marker.
extern RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarker;
extern RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarkerInverse;

// Opens the NVS "wf_prov" namespace on first call; a no-op on every
// subsequent call. Every function that touches gPrefs calls this first so
// callers don't need to sequence initialization themselves.
void ensurePrefsReady();
