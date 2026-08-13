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
};

/// Rejects duplicate message IDs within one session.
/// One instance belongs to one serial connection and is not thread-safe.
class ReplayGuard {
public:
    /// Creates an empty message-ID tracker.
    ReplayGuard() = default;

    /// Records and classifies one message ID.
    /// @param messageId Message ID received from the client.
    /// @return Accepted or replayed result.
    [[nodiscard]] MessageIdCheckResult RecordMessage(const std::string& messageId);

    /// Reports the number of distinct recorded message IDs.
    [[nodiscard]] std::size_t Count() const;

private:
    /// Message IDs already observed in this session.
    std::unordered_set<std::string> seenIds_;
};

}  // namespace dovahlink::application
