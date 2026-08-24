#include <cassert>
#include <cstdint>
#include <initializer_list>

#include "../src/a02yyuw_uart_parser.h"

using waterflex::A02YYUWFrameParser;
using waterflex::A02YYUWFrameStatus;

namespace {

A02YYUWFrameStatus consumeFrame(
    A02YYUWFrameParser& parser,
    std::initializer_list<std::uint8_t> bytes,
    int* distanceMm) {
  A02YYUWFrameStatus status = A02YYUWFrameStatus::Incomplete;
  for (const std::uint8_t value : bytes) {
    status = parser.consume(value, distanceMm);
  }
  return status;
}

void validFrameReturnsMillimeters() {
  A02YYUWFrameParser parser;
  int distanceMm = -1;
  const auto status = consumeFrame(parser, {0xFF, 0x01, 0x2C, 0x2C}, &distanceMm);
  assert(status == A02YYUWFrameStatus::Valid);
  assert(distanceMm == 300);
}

void noiseAndPartialFramesResynchronize() {
  A02YYUWFrameParser parser;
  int distanceMm = -1;
  assert(consumeFrame(parser, {0x00, 0x7E, 0xFF, 0x01}, &distanceMm)
      == A02YYUWFrameStatus::Incomplete);
  assert(consumeFrame(parser, {0x2C, 0x2C}, &distanceMm)
      == A02YYUWFrameStatus::Valid);
  assert(distanceMm == 300);
}

void checksumFailureIsRejected() {
  A02YYUWFrameParser parser;
  int distanceMm = -1;
  assert(consumeFrame(parser, {0xFF, 0x01, 0x2C, 0x00}, &distanceMm)
      == A02YYUWFrameStatus::InvalidChecksum);
  assert(distanceMm == -1);
}

void trailingHeaderStartsTheNextFrame() {
  A02YYUWFrameParser parser;
  int distanceMm = -1;
  assert(consumeFrame(parser, {0xFF, 0x01, 0x2C, 0xFF}, &distanceMm)
      == A02YYUWFrameStatus::InvalidChecksum);
  assert(consumeFrame(parser, {0x01, 0x2C, 0x2C}, &distanceMm)
      == A02YYUWFrameStatus::Valid);
  assert(distanceMm == 300);
}

void rangeLimitsAreEnforced() {
  A02YYUWFrameParser parser;
  int distanceMm = -1;
  assert(consumeFrame(parser, {0xFF, 0x00, 0x14, 0x13}, &distanceMm)
      == A02YYUWFrameStatus::OutOfRange);
  assert(consumeFrame(parser, {0xFF, 0x11, 0x94, 0xA4}, &distanceMm)
      == A02YYUWFrameStatus::Valid);
  assert(distanceMm == 4500);
}

void resetDiscardsAnIncompleteFrame() {
  A02YYUWFrameParser parser;
  int distanceMm = -1;
  assert(consumeFrame(parser, {0xFF, 0x01}, &distanceMm)
      == A02YYUWFrameStatus::Incomplete);
  parser.reset();
  assert(consumeFrame(parser, {0x2C, 0x2C}, &distanceMm)
      == A02YYUWFrameStatus::Incomplete);
  assert(consumeFrame(parser, {0xFF, 0x01, 0x2C, 0x2C}, &distanceMm)
      == A02YYUWFrameStatus::Valid);
}

}  // namespace

int main() {
  validFrameReturnsMillimeters();
  noiseAndPartialFramesResynchronize();
  checksumFailureIsRejected();
  trailingHeaderStartsTheNextFrame();
  rangeLimitsAreEnforced();
  resetDiscardsAnIncompleteFrame();
  return 0;
}

