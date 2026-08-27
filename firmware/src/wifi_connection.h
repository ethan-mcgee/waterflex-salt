// Wi-Fi station connection lifecycle: kicking off connects, polling their
// outcome, and auto-recovery when a saved connection drops.
#pragma once

#include "types.h"

void beginWifiConnect(const WifiProfile& profile, bool applyOnSuccess);
void connectWithSavedProfile();
void processWifiConnection();
void processAutoRecoveryPortal();
