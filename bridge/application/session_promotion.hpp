#pragma once

#include "application/active_session.hpp"

#include <string>

namespace dovahlink::application {

///  Narrow capability for promoting one validated session to the full tier.
class ISessionPromotion {
  public:
    ///  Releases the interface without performing work.
    virtual ~ISessionPromotion() = default;

    ///  Promotes the matching active session, or does nothing for stale identity.
    virtual void UpgradeToFullTrust(ConnectionId connection,
                                    const std::string& sessionId) = 0;
};

} //  namespace dovahlink::application
