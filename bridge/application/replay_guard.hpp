#pragma once

#include <cstddef>
#include <string>
#include <unordered_set>

namespace dovahlink::application {

enum class MessageIdCheckResult {
    kAccepted,           // new messageId; process this message normally.
    kReplayed,           // duplicate messageId; reject this message only.
    kSessionCapReached,  // the session has reached its total message bound;
                         // reject this message and close the session now.
};

// Tracks every messageId seen in one session, rejecting duplicates as replays
// and enforcing the session's total message bound (see
// ai/context/protocol/security.md, bridge/security/limits.hpp). The bound
// keeps the seen-ID set naturally bounded, so it is never pruned or evicted
// -- eviction would let a previously-seen ID be replayed once forgotten.
//
// One instance per session. Not thread-safe: a session's messages are
// expected to be handled serially by its own connection, not shared across
// threads.
class ReplayGuard {
public:
    /**
 * @brief Creates an empty message ID tracker.
 */
ReplayGuard() = default;

    // Checks and, if accepted, records `messageId`. Once kSessionCapReached
    // is returned, every subsequent call also returns kSessionCapReached
    // without recording, since the caller is expected to have closed the
    // session by then.
    [[nodiscard]] MessageIdCheckResult RecordMessage(const std::string& messageId);

    // The number of distinct messageIds recorded so far.
    [[nodiscard]] std::size_t Count() const;

private:
    std::unordered_set<std::string> seenIds_;
};

}  // namespace dovahlink::application
