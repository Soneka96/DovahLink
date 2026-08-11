#include "application/replay_guard.hpp"

#include "security/limits.hpp"

namespace dovahlink::application {

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

std::size_t ReplayGuard::Count() const {
    return seenIds_.size();
}

}  // namespace dovahlink::application
