#pragma once

#include <cstddef>
#include <cstdint>

namespace waterflex {

constexpr int kA02YYUWMinimumDistanceMm = 30;
constexpr int kA02YYUWMaximumDistanceMm = 4500;

enum class A02YYUWFrameStatus {
  Incomplete,
  Valid,
  InvalidChecksum,
  OutOfRange
};

class A02YYUWFrameParser {
 public:
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

