#pragma once

#include "shared/enums.hpp"

#include <cstddef>
#include <string>
#include <unordered_set>

namespace dovahlink::application {

///  Rejects duplicate message IDs within one session.
///  One instance belongs to one serial connection and is not thread-safe.
class IReplayGuard {
  public:
    ///  Releases the interface without performing work.
    virtual ~IReplayGuard() = default;

    ///  Records and classifies one message ID.
    ///  @param messageId Message ID received from the client.
    ///  @return Accepted or replayed result.
    [[nodiscard]] virtual MessageIdCheckResult
    RecordMessage(const std::string& messageId) = 0;

    ///  Reports the number of distinct recorded message IDs.
    [[nodiscard]] virtual std::size_t Count() const = 0;
};

///  @copydoc IReplayGuard
class ReplayGuard final : public IReplayGuard {
  public:
    ///  Creates an empty message-ID tracker.
    ReplayGuard() = default;

    ///  @copydoc IReplayGuard::RecordMessage
    [[nodiscard]] MessageIdCheckResult
    RecordMessage(const std::string& messageId) override;

    ///  @copydoc IReplayGuard::Count
    [[nodiscard]] std::size_t Count() const override;

  private:
    ///  Message IDs already observed in this session.
    std::unordered_set<std::string> seenIds_;
};

} //  namespace dovahlink::application
