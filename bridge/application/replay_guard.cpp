#include "application/replay_guard.hpp"

namespace dovahlink::application {

MessageIdCheckResult ReplayGuard::RecordMessage(const std::string &messageId) {
  if (seenIds_.contains(messageId)) {
    return MessageIdCheckResult::kReplayed;
  }
  seenIds_.insert(messageId);
  return MessageIdCheckResult::kAccepted;
}

std::size_t ReplayGuard::Count() const { return seenIds_.size(); }

} // namespace dovahlink::application
