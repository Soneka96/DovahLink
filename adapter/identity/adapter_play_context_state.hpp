#pragma once

#include <array>
#include <cstddef>
#include <mutex>

namespace dovahlink::adapter::identity {

///  The adapter's own current play-context identity: the value most recently
///  announced through `ipc::IAdapterIpcSession::SendPlayContextChanged`,
///  shared between the session (which writes it and replays it to a newly
///  authenticated connection) and a native capture router that stamps its own
///  spontaneous native-event captures with it. Kept as its own focused type,
///  rather than a field on the session, so both collaborators depend on it
///  directly instead of one depending on the other -- `ipc/` and `runtime/`
///  otherwise have no reason to depend on each other.
class IAdapterPlayContextState {
  public:
    virtual ~IAdapterPlayContextState() = default;

    ///  @return The play context most recently set, or all-zero bytes if
    ///  none has been set yet.
    [[nodiscard]] virtual std::array<std::byte, 16> CurrentPlayContext() const = 0;

    ///  Records a newly announced play context.
    ///  @param playContextId The play context now current.
    virtual void SetCurrentPlayContext(std::array<std::byte, 16> playContextId) = 0;
};

///  @copydoc IAdapterPlayContextState
class AdapterPlayContextState final : public IAdapterPlayContextState {
  public:
    ///  @copydoc IAdapterPlayContextState::CurrentPlayContext
    [[nodiscard]] std::array<std::byte, 16> CurrentPlayContext() const override;

    ///  @copydoc IAdapterPlayContextState::SetCurrentPlayContext
    void SetCurrentPlayContext(std::array<std::byte, 16> playContextId) override;

  private:
    ///  Guards `playContextId_` against concurrent access: written from
    ///  whichever thread calls `SendPlayContextChanged` (the SKSE messaging
    ///  listener thread for a real save transition, the connection's own
    ///  thread for a replay), read from the Skyrim game thread at capture
    ///  time.
    mutable std::mutex mutex_;
    ///  The play context most recently set. All-zero until the first real
    ///  play-context transition.
    std::array<std::byte, 16> playContextId_{};
};

} //  namespace dovahlink::adapter::identity
