#pragma once

#include <functional>

namespace dovahlink::shared {

///  Move-only RAII value that runs a caller-supplied action exactly once --
///  on destruction by default, or earlier via @ref Trigger -- unless
///  @ref Dismiss is called first. Lets a behavior-bearing class expose an
///  acquire/release-style capability through a project-owned interface: the
///  action is bound at construction, typically capturing the exact identity
///  being released (for example a connection or session ID), so a test
///  double can build one directly instead of needing `friend` access to a
///  concrete owner's private nested type.
class ScopedRelease {
  public:
    ///  Binds the action this instance runs exactly once.
    explicit ScopedRelease(std::function<void()> action) noexcept;

    ///  Prevents two instances from sharing the same pending action.
    ScopedRelease(const ScopedRelease&) = delete;

    ///  Prevents copying a pending action.
    ScopedRelease& operator=(const ScopedRelease&) = delete;

    ///  Transfers the pending action; the moved-from instance runs nothing.
    ScopedRelease(ScopedRelease&& other) noexcept;

    ///  Runs this instance's own pending action, if any, then transfers the
    ///  other instance's pending action.
    ScopedRelease& operator=(ScopedRelease&& other) noexcept;

    ///  Runs the pending action, if it has not already run or been dismissed.
    ~ScopedRelease();

    ///  Runs the pending action now, if any; harmless to call more than once
    ///  or after @ref Dismiss.
    void Trigger() noexcept;

    ///  Discards the pending action without running it.
    void Dismiss() noexcept;

  private:
    ///  The bound action, or empty once it has run or been dismissed.
    std::function<void()> action_;
};

} //  namespace dovahlink::shared
