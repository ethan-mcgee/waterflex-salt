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

extern Preferences gPrefs;
extern bool gPrefsInitialized;
extern DNSServer gDnsServer;
extern WebServer gPortalServer;

extern bool gHasActiveProfile;
extern WifiProfile gActiveProfile;
extern DeviceConfig gDeviceConfig;
extern String gBootstrapToken;
extern String gSerialNumber;

extern bool gHasCandidateProfile;
extern WifiProfile gCandidateProfile;
extern DeviceConfig gCandidateDeviceConfig;
extern bool gCandidateApplyOnSuccess;

extern ProvisioningState gState;
extern String gLastError;
extern String gPortalToken;
extern String gPortalSsid;

extern bool gPortalRunning;
extern uint32_t gPortalStartedAtMs;
extern uint32_t gPortalLastActivityAtMs;

extern bool gWifiConnectInFlight;
extern uint32_t gWifiConnectStartedAtMs;
extern uint32_t gLastWifiDisconnectAtMs;

extern bool gRecoveryButtonDown;
extern bool gRecoveryPortalTriggered;
extern bool gFactoryResetTriggered;
extern uint32_t gRecoveryPressedAtMs;
extern uint32_t gOnboardResetGestureArmedAtMs;
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

extern RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarker;
extern RTC_NOINIT_ATTR uint32_t gOnboardResetGestureMarkerInverse;

void ensurePrefsReady();
