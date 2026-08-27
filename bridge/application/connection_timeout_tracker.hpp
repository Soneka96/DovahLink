#pragma once

#include <chrono>

namespace dovahlink::application {

///  Tracks the 5-second handshake and 60-second authenticated-idle deadlines for
///  one connection. Callers provide time explicitly; an expired connection
///  cannot be revived by late activity.
class IConnectionTimeoutTracker {
  public:
    ///  Releases the interface without performing work.
    virtual ~IConnectionTimeoutTracker() = default;

    ///  Switches from the handshake deadline to the authenticated idle deadline.
    ///  @param now Time at which authentication succeeded.
    virtual void MarkAuthenticated(std::chrono::steady_clock::time_point now) = 0;

    ///  Refreshes the idle deadline after valid authenticated activity.
    ///  @param now Time at which the activity occurred.
    virtual void RecordActivity(std::chrono::steady_clock::time_point now) = 0;

    ///  Reports whether the active deadline has elapsed.
    ///  @param now Time used for the deadline comparison.
    ///  @return `true` when the connection has timed out.
    [[nodiscard]] virtual bool
    IsTimedOut(std::chrono::steady_clock::time_point now) const = 0;

    ///  Reports the current handshake-or-idle deadline, for a caller that must
    ///  race its own operation against it (for example a transport-level read
    ///  watchdog) rather than only polling `IsTimedOut` after the fact.
    ///  @return The absolute time at which this connection is considered timed
    ///  out.
    [[nodiscard]] virtual std::chrono::steady_clock::time_point Deadline() const = 0;
};

///  @copydoc IConnectionTimeoutTracker
class ConnectionTimeoutTracker final : public IConnectionTimeoutTracker {
  public:
    ///  Starts the handshake deadline at `now`.
    ///  @param now Time at which the connection was accepted.
    explicit ConnectionTimeoutTracker(std::chrono::steady_clock::time_point now);

    ///  @copydoc IConnectionTimeoutTracker::MarkAuthenticated
    void MarkAuthenticated(std::chrono::steady_clock::time_point now) override;

    ///  @copydoc IConnectionTimeoutTracker::RecordActivity
    void RecordActivity(std::chrono::steady_clock::time_point now) override;

    ///  @copydoc IConnectionTimeoutTracker::IsTimedOut
    [[nodiscard]] bool
    IsTimedOut(std::chrono::steady_clock::time_point now) const override;

    ///  @copydoc IConnectionTimeoutTracker::Deadline
    [[nodiscard]] std::chrono::steady_clock::time_point Deadline() const override;

  private:
    ///  Whether authentication has completed.
    bool authenticated_ = false;

    ///  Current handshake or idle deadline.
    std::chrono::steady_clock::time_point deadline_;
};

} //  namespace dovahlink::application
