// Factory-bootstrap self-activation and post-connect operational API checks.
#pragma once

#include "types.h"

// Exchanges the factory bootstrap token for an operational device
// credential via the activation API, writing it into `config->deviceToken`
// on success. The generated credential and its attempt id are staged in NVS
// before the network call so a retry after a mid-request reset reuses the
// same attempt instead of minting a new one. Returns false (leaving
// `config` untouched) if bootstrap identity is missing, Wi-Fi/clock aren't
// ready, or the API rejects the attempt.
bool activateCandidateDevice(DeviceConfig* config);
// Confirms a freshly-activated (or freshly-entered) device config actually
// works end-to-end — DHCP/DNS/SNTP/TLS and the operational API — by POSTing
// a throwaway health report before it's persisted as the active config.
bool verifyCandidateOperationalApi(const DeviceConfig& config);
