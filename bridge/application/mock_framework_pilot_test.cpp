#include "application/active_session_disconnector.hpp"

#include <catch2/catch_test_macros.hpp>
#include <gmock/gmock.h>

#include <string_view>

using dovahlink::application::ActiveSessionDisconnector;
using testing::StrictMock;

namespace {

///  GoogleMock double for the existing Bridge session-disconnection port.
class MockActiveSessionDisconnector : public ActiveSessionDisconnector {
  public:
    MOCK_METHOD(void, DisconnectIfClientActive,
                (std::string_view clientId, std::string_view reason),
                (override));
    MOCK_METHOD(void, DisconnectActive, (std::string_view reason), (override));
};

} //  namespace

TEST_CASE("GoogleMock verifies string-view arguments on an existing Bridge port",
          "[application][mock_framework]") {
    StrictMock<MockActiveSessionDisconnector> disconnector;
    EXPECT_CALL(disconnector,
                DisconnectIfClientActive("client-1", "revoked"));

    disconnector.DisconnectIfClientActive("client-1", "revoked");
}
