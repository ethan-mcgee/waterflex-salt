#pragma once

#include <cstddef>
#include <cstdint>

namespace waterflex {

// Sensor-datasheet distance range; a checksum-valid frame outside this
// range is still reported (as OutOfRange) rather than treated as noise.
constexpr int kA02YYUWMinimumDistanceMm = 30;
constexpr int kA02YYUWMaximumDistanceMm = 4500;

// Outcome of feeding one byte to A02YYUWFrameParser::consume().
enum class A02YYUWFrameStatus {
  Incomplete,      // Frame not yet complete; keep feeding bytes.
  Valid,            // Complete frame, checksum ok, distance in range.
  InvalidChecksum,  // Complete frame, checksum mismatch.
  OutOfRange        // Complete frame, checksum ok, distance out of range.
};

// Byte-at-a-time state machine for the A02YYUW four-byte UART frame
// (0xFF header, distance high byte, distance low byte, checksum).
class A02YYUWFrameParser {
 public:
  // Feeds one received byte into the frame in progress. On a completed
  // frame, writes the parsed distance to `distanceMm` (if non-null) when
  // the checksum is valid and returns the frame's status.
  A02YYUWFrameStatus consume(std::uint8_t value, int* distanceMm) {
    if (frameLength_ == 0) {
      if (value == kFrameHeader) {
        frame_[0] = value;
        frameLength_ = 1;
      }
      return A02YYUWFrameStatus::Incomplete;
    }

    frame_[frameLength_++] = value;
    if (frameLength_ < kFrameSize) {
      return A02YYUWFrameStatus::Incomplete;
    }

    const std::uint8_t expectedChecksum = static_cast<std::uint8_t>(
        frame_[0] + frame_[1] + frame_[2]);
    const bool checksumValid = expectedChecksum == frame_[3];
    const int candidateDistanceMm =
        (static_cast<int>(frame_[1]) << 8) | frame_[2];

    // A failed frame may have ended with the next frame's 0xFF header. Preserve
    // that byte so the parser can recover without discarding another full frame.
    const bool trailingHeader = !checksumValid && value == kFrameHeader;
    frameLength_ = trailingHeader ? 1 : 0;
    if (trailingHeader) {
      frame_[0] = kFrameHeader;
    }

    if (!checksumValid) {
      return A02YYUWFrameStatus::InvalidChecksum;
    }
    if (candidateDistanceMm < kA02YYUWMinimumDistanceMm
        || candidateDistanceMm > kA02YYUWMaximumDistanceMm) {
      return A02YYUWFrameStatus::OutOfRange;
    }

    if (distanceMm != nullptr) {
      *distanceMm = candidateDistanceMm;
    }
    return A02YYUWFrameStatus::Valid;
  }

  // Discards any partially-received frame, so the next consume() starts
  // fresh looking for a header byte.
  void reset() {
    frameLength_ = 0;
  }

 private:
  static constexpr std::size_t kFrameSize = 4;
  static constexpr std::uint8_t kFrameHeader = 0xFF;

  std::uint8_t frame_[kFrameSize]{};
  std::size_t frameLength_ = 0;
};

}  // namespace waterflex

