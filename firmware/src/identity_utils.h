// Device identity, encoding, and URL-derivation helpers. These are pure
// functions with no dependency on the mutable global device state.
#pragma once

#include <Arduino.h>

#include "types.h"

String stateToString(ProvisioningState state);
String makeBootId();
String jsonEscape(const String& value);
String makePortalToken();
String base64Encode(const uint8_t* bytes, size_t length, bool urlSafe);
String defaultPortalPassphrase();
bool isApprovedOperationalApiUrl(const String& url);
String deviceHealthUrl(const String& telemetryUrl);
String activationUrl(const String& telemetryUrl);
