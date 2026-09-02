#pragma once

#include <string_view>

namespace dovahlink::application {

///  Signals that a previously admitted session's slot has just been released
///  and is available for a new admission. `SessionManager` releases a slot
///  synchronously, but that release happens on whichever thread owns the
///  connection that held it -- a moment a caller on another thread or process
///  (a client reconnecting to the same Bridge) cannot otherwise observe. This
///  seam owns only how that moment is reported: a native diagnostic in the
///  real implementation, an observable stdout line in the Skyrim-independent
///  test harness, a recording double in tests.
class ISessionReleaseNotificationSink {
  public:
    ///  Releases the interface without performing work.
    virtual ~ISessionReleaseNotificationSink() = default;

    ///  Reports that the session slot previously held by `clientId` has just
    ///  been released and is now available for a new admission.
    ///  @param clientId The client whose session slot was released.
    virtual void NotifySessionReleased(std::string_view clientId) = 0;
};

} //  namespace dovahlink::application
