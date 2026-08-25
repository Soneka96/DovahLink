#pragma once

#include "shared/enums.hpp"

#include <cstddef>
#include <string>
#include <unordered_set>

namespace dovahlink::application {

///  Rejects duplicate message IDs within one session.
///  One instance belongs to one serial connection and is not thread-safe.
class ReplayGuard {
  public:
    ///  Creates an empty message-ID tracker.
    ReplayGuard() = default;

    ///  Records and classifies one message ID.
    ///  @param messageId Message ID received from the client.
    ///  @return Accepted or replayed result.
    [[nodiscard]] MessageIdCheckResult
    RecordMessage(const std::string& messageId);

    ///  Reports the number of distinct recorded message IDs.
    [[nodiscard]] std::size_t Count() const;

  private:
    ///  Message IDs already observed in this session.
    std::unordered_set<std::string> seenIds_;
};

} //  namespace dovahlink::application
