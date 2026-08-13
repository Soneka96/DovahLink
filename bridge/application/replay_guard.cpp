#include "application/replay_guard.hpp"

#include "security/limits.hpp"

namespace dovahlink::application {

/**
 * @brief Records a message ID and classifies it as accepted, replayed, or beyond the session message limit.
 *
 * @param messageId Message ID to record.
 * @return The result of recording the message ID.
 */
MessageIdCheckResult ReplayGuard::RecordMessage(const std::string& messageId) {
    if (seenIds_.size() >= security::kMaxMessagesPerSession) {
        return MessageIdCheckResult::kSessionCapReached;
    }
    if (seenIds_.contains(messageId)) {
        return MessageIdCheckResult::kReplayed;
    }
    seenIds_.insert(messageId);
    return MessageIdCheckResult::kAccepted;
}

/**
 * @brief Returns the number of recorded message IDs.
 *
 * @return std::size_t Number of recorded message IDs.
 */
std::size_t ReplayGuard::Count() const {
    return seenIds_.size();
}

}  // namespace dovahlink::application
