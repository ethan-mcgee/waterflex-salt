#include "reset_control.h"

#include <Arduino.h>

#include "config.h"
#include "state.h"

bool onboardResetGestureIsArmed() {
  return gOnboardResetGestureMarker == kOnboardResetGestureMagic
      && gOnboardResetGestureMarkerInverse == ~kOnboardResetGestureMagic;
}

void disarmOnboardResetGesture() {
  gOnboardResetGestureMarker = 0;
  gOnboardResetGestureMarkerInverse = 0;
  gOnboardResetGestureArmedAtMs = 0;
}

void armOnboardResetGesture() {
  gOnboardResetGestureMarker = kOnboardResetGestureMagic;
  gOnboardResetGestureMarkerInverse = ~kOnboardResetGestureMagic;
  gOnboardResetGestureArmedAtMs = millis();
}

void restartDevice() {
  disarmOnboardResetGesture();
  ESP.restart();
}
