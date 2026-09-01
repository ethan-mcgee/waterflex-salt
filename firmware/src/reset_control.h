// Onboard double-RESET gesture tracking and device restart.
#pragma once

// Returns true if the RTC-noinit marker pair survived the last reset intact,
// meaning the previous boot armed the gesture and this boot is the second
// RESET within its window (RTC_NOINIT memory survives a plain reset but not
// a power cycle, so this only fires for back-to-back RESETs).
bool onboardResetGestureIsArmed();
// Clears the RTC-noinit marker pair and the armed-at timestamp.
void disarmOnboardResetGesture();
// Sets the RTC-noinit marker pair and records the arm time, so a second
// RESET within kOnboardResetGestureWindowMs will be recognized on the next boot.
void armOnboardResetGesture();
// Disarms the reset gesture (so this planned restart isn't mistaken for the
// user's second RESET) and restarts the device.
void restartDevice();
