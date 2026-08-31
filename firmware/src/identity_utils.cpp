#include "identity_utils.h"

#include <esp_mac.h>
#include <mbedtls/base64.h>

#include "config.h"

String stateToString(ProvisioningState state) {
  switch (state) {
    case ProvisioningState::Unprovisioned:
      return "idle";
    case ProvisioningState::PortalIdle:
      return "idle";
    case ProvisioningState::PortalConnecting:
      return "connecting";
    case ProvisioningState::PortalError:
      return "error";
    case ProvisioningState::Active:
      return "connected";
  }
  return "error";
}

String makeBootId() {
  const uint32_t a = static_cast<uint32_t>(esp_random());
  const uint16_t b = static_cast<uint16_t>(esp_random() & 0xFFFFU);
  const uint16_t c = static_cast<uint16_t>((esp_random() & 0x0FFFU) | 0x4000U);
  const uint16_t d = static_cast<uint16_t>((esp_random() & 0x3FFFU) | 0x8000U);
  const uint64_t e = (static_cast<uint64_t>(esp_random()) << 16)
      | static_cast<uint64_t>(esp_random() & 0xFFFFU);

  char guid[37];
  snprintf(guid, sizeof(guid), "%08lx-%04x-%04x-%04x-%012llx",
           static_cast<unsigned long>(a),
           static_cast<unsigned int>(b),
           static_cast<unsigned int>(c),
           static_cast<unsigned int>(d),
           static_cast<unsigned long long>(e & 0xFFFFFFFFFFFFULL));
  return String(guid);
}

String jsonEscape(const String& value) {
  String out;
  out.reserve(value.length() + 8);
  for (size_t i = 0; i < value.length(); ++i) {
    const char c = value[i];
    if (c == '\\' || c == '"') {
      out += '\\';
      out += c;
    } else if (c == '\n') {
      out += "\\n";
    } else if (c == '\r') {
      out += "\\r";
    } else {
      out += c;
    }
  }
  return out;
}

String makePortalToken() {
  String token;
  token.reserve(32);
  for (int i = 0; i < 4; ++i) {
    char block[9];
    snprintf(block, sizeof(block), "%08lx", static_cast<unsigned long>(esp_random()));
    token += block;
  }
  return token;
}

String serialSuffix() {
  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  char suffix[7];
  snprintf(suffix, sizeof(suffix), "%02X%02X%02X", mac[3], mac[4], mac[5]);
  return String(suffix);
}

String hardwareId() {
  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  char value[13];
  snprintf(value, sizeof(value), "%02X%02X%02X%02X%02X%02X",
           mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  return String(value);
}

String base64Encode(const uint8_t* bytes, size_t length, bool urlSafe) {
  size_t outputLength = 0;
  unsigned char output[96]{};
  if (mbedtls_base64_encode(output, sizeof(output) - 1, &outputLength, bytes, length) != 0) {
    return "";
  }
  output[outputLength] = '\0';
  String encoded(reinterpret_cast<char*>(output));
  if (urlSafe) {
    encoded.replace("+", "-");
    encoded.replace("/", "_");
    while (encoded.endsWith("=")) encoded.remove(encoded.length() - 1);
  }
  return encoded;
}

String defaultPortalPassphrase() {
  // This deterministic fallback is compiled out of every pilot/release image.
  return String("WF-") + serialSuffix() + "-SETUP";
}

bool isApprovedOperationalApiUrl(const String& url) {
  if (url == kDefaultTelemetryUrl) {
    return true;
  }
#if WATERFLEX_ALLOW_DEVELOPMENT_PROVISIONING
  return url.startsWith("http://") || url.startsWith("https://");
#else
  return false;
#endif
}

String deviceHealthUrl(const String& telemetryUrl) {
  String url = telemetryUrl;
  constexpr char telemetrySuffix[] = "telemetry";
  if (!url.endsWith(telemetrySuffix)) {
    return "";
  }
  url.remove(url.length() - strlen(telemetrySuffix));
  url += "health";
  return url;
}

String activationUrl(const String& telemetryUrl) {
  String url = telemetryUrl;
  constexpr char telemetrySuffix[] = "telemetry";
  if (!url.endsWith(telemetrySuffix)) return "";
  url.remove(url.length() - strlen(telemetrySuffix));
  url += "activate";
  return url;
}
