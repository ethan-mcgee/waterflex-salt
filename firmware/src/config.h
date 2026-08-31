// Compile-time constants shared across the WaterFlex firmware modules.
#pragma once

#include <Arduino.h>

#include "a02yyuw_uart_parser.h"

#ifndef WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
#define WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING 0
#endif

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
constexpr char kLegacyKeyHidden[] = "active_hidden";  // Cleanup only; hidden-network support was removed.
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
