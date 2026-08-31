// SoftAP captive portal: setup page, JSON routes, DNS capture, and lifecycle.
#pragma once

#include <Arduino.h>

void markPortalActivity();
void stopPortal();
void setPortalError(const String& errorCode);
void startPortal(const String& reasonCode);
void processPortal();
