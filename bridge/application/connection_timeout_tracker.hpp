#pragma once

#include <chrono>

namespace dovahlink::application {

/// Tracks the 5-second handshake and 60-second authenticated-idle deadlines for
/// one connection. Callers provide time explicitly; an expired connection
/// cannot be revived by late activity.
class ConnectionTimeoutTracker {
public:
  /// Starts the handshake deadline at `now`.
  /// @param now Time at which the connection was accepted.
  explicit ConnectionTimeoutTracker(std::chrono::steady_clock::time_point now);

  /// Switches from the handshake deadline to the authenticated idle deadline.
  /// @param now Time at which authentication succeeded.
  void MarkAuthenticated(std::chrono::steady_clock::time_point now);

  /// Refreshes the idle deadline after valid authenticated activity.
  /// @param now Time at which the activity occurred.
  void RecordActivity(std::chrono::steady_clock::time_point now);

  /// Reports whether the active deadline has elapsed.
  /// @param now Time used for the deadline comparison.
  /// @return `true` when the connection has timed out.
  [[nodiscard]] bool
  IsTimedOut(std::chrono::steady_clock::time_point now) const;

  /// Reports the current handshake-or-idle deadline, for a caller that must
  /// race its own operation against it (for example a transport-level read
  /// watchdog) rather than only polling `IsTimedOut` after the fact.
  /// @return The absolute time at which this connection is considered timed
  /// out.
  [[nodiscard]] std::chrono::steady_clock::time_point Deadline() const;

private:
  /// Whether authentication has completed.
  bool authenticated_ = false;

  /// Current handshake or idle deadline.
  std::chrono::steady_clock::time_point deadline_;
};

} // namespace dovahlink::application
