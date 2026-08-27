#include "state.h"

#include "config.h"

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
