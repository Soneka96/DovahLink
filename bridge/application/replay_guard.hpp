#pragma once

#include <cstddef>
#include <string>
#include <unordered_set>

namespace dovahlink::application {

/// Classifies a message-ID recording attempt.
enum class MessageIdCheckResult {
    /// The message ID was new and recorded.
    kAccepted,

    /// The message ID was already recorded for this session.
    kReplayed,

    /// The session message limit was reached and the session must close.
    kSessionCapReached,
};

/// Rejects duplicate message IDs and enforces the per-session message bound.
/// One instance belongs to one serial connection and is not thread-safe.
class ReplayGuard {
public:
    /// Creates an empty message-ID tracker.
    ReplayGuard() = default;

    /// Records and classifies one message ID.
    /// @param messageId Message ID received from the client.
    /// @return Accepted, replayed, or session-cap result.
    [[nodiscard]] MessageIdCheckResult RecordMessage(const std::string& messageId);

    /// Reports the number of distinct recorded message IDs.
    [[nodiscard]] std::size_t Count() const;

private:
    /// Message IDs already observed in this session.
    std::unordered_set<std::string> seenIds_;
};

}  // namespace dovahlink::application
