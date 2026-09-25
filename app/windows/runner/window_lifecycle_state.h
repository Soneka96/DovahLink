#ifndef RUNNER_WINDOW_LIFECYCLE_STATE_H_
#define RUNNER_WINDOW_LIFECYCLE_STATE_H_

#include <cstdint>
#include <optional>

///  Defines close-request and committed-session-end state for one native window.
class IWindowLifecycleState {
  public:
    ///  Releases the native window lifecycle state contract.
    virtual ~IWindowLifecycleState() = default;

    ///  Starts one close handshake identified by [generation].
    ///  @param generation A nonzero process-unique token for this close request.
    ///  @return `true` when the request becomes pending; otherwise `false`.
    virtual bool BeginClose(std::uint64_t generation) noexcept = 0;

    ///  Completes [generation] only while it is the active close request.
    ///  @param generation The request token carried by a Dart reply or native timeout.
    ///  @return `true` when this call completes the active request; otherwise `false`.
    virtual bool CompleteClose(std::uint64_t generation) noexcept = 0;

    ///  Whether a close handshake is waiting for Dart or the native timeout.
    ///  @return `true` while a close request is pending.
    virtual bool HasPendingClose() const noexcept = 0;

    ///  The active close token, or no value when no close request is pending.
    ///  @return The active generation, or `std::nullopt` when idle.
    virtual std::optional<std::uint64_t> PendingCloseGeneration() const noexcept = 0;

    ///  Admits cleanup once after Windows commits to ending the current session.
    ///  @param sessionEnded The nonzero `WM_ENDSESSION` wParam value.
    ///  @return `true` for the first committed session-end request only.
    virtual bool BeginSessionEndCleanup(bool sessionEnded) noexcept = 0;

    ///  Clears per-window close and session-end state when the native window is created or destroyed.
    virtual void ResetForWindow() noexcept = 0;
};

///  Stores coherent pending-close and committed-session-end state for the native window.
class WindowLifecycleState final : public IWindowLifecycleState {
  public:
    ///  @copydoc IWindowLifecycleState::BeginClose
    bool BeginClose(std::uint64_t generation) noexcept override;

    ///  @copydoc IWindowLifecycleState::CompleteClose
    bool CompleteClose(std::uint64_t generation) noexcept override;

    ///  @copydoc IWindowLifecycleState::HasPendingClose
    bool HasPendingClose() const noexcept override;

    ///  @copydoc IWindowLifecycleState::PendingCloseGeneration
    std::optional<std::uint64_t> PendingCloseGeneration() const noexcept override;

    ///  @copydoc IWindowLifecycleState::BeginSessionEndCleanup
    bool BeginSessionEndCleanup(bool sessionEnded) noexcept override;

    ///  @copydoc IWindowLifecycleState::ResetForWindow
    void ResetForWindow() noexcept override;

  private:
    ///  The active close request token, or empty when the window may close directly.
    std::optional<std::uint64_t> pending_close_generation_;

    ///  Whether committed session ending already requested shared cleanup.
    bool session_end_cleanup_requested_ = false;
};

#endif //  RUNNER_WINDOW_LIFECYCLE_STATE_H_
