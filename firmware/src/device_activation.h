// Factory-bootstrap self-activation and post-connect operational API checks.
#pragma once

#include "types.h"

bool activateCandidateDevice(DeviceConfig* config);
bool verifyCandidateOperationalApi(const DeviceConfig& config);
