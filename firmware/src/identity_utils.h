// Device identity, encoding, and URL-derivation helpers. These are pure
// functions with no dependency on the mutable global device state.
#pragma once

#include <Arduino.h>

#include "types.h"

// Maps the provisioning state machine to the small status vocabulary
// ("idle"/"connecting"/"error"/"connected") the portal's status API exposes.
String stateToString(ProvisioningState state);
// Generates a random UUIDv4-shaped id, used both as the per-boot id
// attached to queued readings and as an activation attempt id.
String makeBootId();
// Escapes backslash, double-quote, and newline/carriage-return characters
// so `value` can be embedded in a hand-built JSON string literal.
String jsonEscape(const String& value);
// Generates a random per-portal-session token that the setup page must echo
// back on /api/v1/configure, so a stale or foreign page can't submit
// credentials into an unrelated portal session.
String makePortalToken();
// Base64-encodes `bytes`. When `urlSafe` is true, substitutes -/_ for +//
// and strips padding, matching the operational secret's on-the-wire form.
String base64Encode(const uint8_t* bytes, size_t length, bool urlSafe);
// Deterministic SoftAP passphrase used only when no factory-injected setup
// passphrase exists, compiled out of every pilot/release image.
String defaultPortalPassphrase();
// Confirms `url` is an endpoint this device is allowed to talk telemetry/
// activation/health to: the built-in default always qualifies, and any
// http(s) URL only qualifies when built with
// WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING.
bool isApprovedOperationalApiUrl(const String& url);
// Derives the device-health endpoint from a telemetry URL by replacing its
// required "telemetry" suffix with "health". Returns "" if the URL doesn't
// end in "telemetry".
String deviceHealthUrl(const String& telemetryUrl);
// Derives the activation endpoint from a telemetry URL by replacing its
// required "telemetry" suffix with "activate". Returns "" if the URL
// doesn't end in "telemetry".
String activationUrl(const String& telemetryUrl);
