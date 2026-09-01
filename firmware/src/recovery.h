// Serial console commands and the physical recovery button gesture handling.
#pragma once

// Reads and dispatches line-buffered USB serial commands: factory
// provisioning (identity injection, one-time and only while unprovisioned),
// FACTORY_STATUS, FACTORY_RESET, and PORTAL (plus PORTAL_PREVIEW under
// WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING).
void processSerialCommands();
// Tracks the physical recovery button's hold duration and triggers a portal
// open (short hold) or full factory reset (long hold) once each threshold
// is crossed while the button stays down.
void processRecoveryButton();
// Closes the onboard double-RESET gesture's arming window once it expires,
// so only a second RESET within the window (not an arbitrary later one)
// is treated as a request to clear provisioning.
void processOnboardResetGestureWindow();
