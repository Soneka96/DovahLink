#include "process/adapter_host_endpoint_report.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <string>
#include <vector>

using dovahlink::adapter::process::TryParseHostEndpointReport;

TEST_CASE("TryParseHostEndpointReport parses a well-formed report with "
          "Windows-style CRLF line endings",
          "[process][adapter_host_endpoint_report]") {
  auto result = TryParseHostEndpointReport("PORT 12345\r", "PROOF a0b1c2\r",
                                           "HOSTPROOF d3e4\r");

  REQUIRE(result.has_value());
  CHECK(result->port == 12345);
  CHECK(result->proofToken == std::vector<std::byte>{std::byte{0xA0},
                                                     std::byte{0xB1},
                                                     std::byte{0xC2}});
  CHECK(result->hostProofKey ==
        std::vector<std::byte>{std::byte{0xD3}, std::byte{0xE4}});
}

TEST_CASE("TryParseHostEndpointReport parses a well-formed report with "
          "plain LF (no trailing carriage return)",
          "[process][adapter_host_endpoint_report]") {
  auto result =
      TryParseHostEndpointReport("PORT 1", "PROOF ff", "HOSTPROOF ee");

  REQUIRE(result.has_value());
  CHECK(result->port == 1);
  CHECK(result->proofToken == std::vector<std::byte>{std::byte{0xFF}});
  CHECK(result->hostProofKey == std::vector<std::byte>{std::byte{0xEE}});
}

TEST_CASE("TryParseHostEndpointReport accepts the port boundary values 0 "
          "and 65535",
          "[process][adapter_host_endpoint_report]") {
  for (std::uint16_t port : {std::uint16_t{0}, std::uint16_t{65535}}) {
    auto result = TryParseHostEndpointReport("PORT " + std::to_string(port),
                                             "PROOF a0", "HOSTPROOF b1");

    REQUIRE(result.has_value());
    CHECK(result->port == port);
  }
}

TEST_CASE("TryParseHostEndpointReport accepts an empty proof token or "
          "HostProof key",
          "[process][adapter_host_endpoint_report]") {
  auto result = TryParseHostEndpointReport("PORT 1", "PROOF ", "HOSTPROOF ");

  REQUIRE(result.has_value());
  CHECK(result->proofToken.empty());
  CHECK(result->hostProofKey.empty());
}

TEST_CASE("TryParseHostEndpointReport returns nullopt for malformed input",
          "[process][adapter_host_endpoint_report]") {
  struct Case {
    std::string portLine;
    std::string proofLine;
    std::string hostProofLine;
  };
  for (const Case &testCase :
       {Case{"PORT abc", "PROOF a0", "HOSTPROOF b1"},   // non-numeric port
        Case{"PORT 70000", "PROOF a0", "HOSTPROOF b1"}, // out-of-range port
        Case{"PORT 65536", "PROOF a0",
             "HOSTPROOF b1"}, // exactly one past the max port
        Case{"PORT 99999999999", "PROOF a0",
             "HOSTPROOF b1"},                         // overflows int outright
        Case{"PORT -1", "PROOF a0", "HOSTPROOF b1"},  // negative port
        Case{"PORT 1", "PROOF a0g", "HOSTPROOF b1"},  // non-hex character
        Case{"PORT 1", "PROOF A0", "HOSTPROOF b1"},   // uppercase hex
        Case{"PORT 1", "PROOF a", "HOSTPROOF b1"},    // odd-length hex
        Case{"NOPORT 1", "PROOF a0", "HOSTPROOF b1"}, // wrong port-line prefix
        Case{"PORT 1", "NOPROOF a0", "HOSTPROOF b1"}, // wrong proof-line prefix
        Case{"PORT 1", "PROOF a0",
             "NOHOSTPROOF b1"}, // wrong HostProof-line prefix
        Case{"PORT 1", "PROOF a0",
             "HOSTPROOF b1g"}, // non-hex character in HostProof
        Case{"PORT 1", "PROOF a0", "HOSTPROOF b"}, // odd-length HostProof hex
        Case{"", "", ""},                          // empty lines
        Case{"PORT 1 extra", "PROOF a0", "HOSTPROOF b1"}}) { // trailing garbage
    INFO("portLine: " << testCase.portLine
                      << ", proofLine: " << testCase.proofLine
                      << ", hostProofLine: " << testCase.hostProofLine);
    CHECK_FALSE(TryParseHostEndpointReport(testCase.portLine,
                                           testCase.proofLine,
                                           testCase.hostProofLine)
                    .has_value());
  }
}
