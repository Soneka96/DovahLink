#include "process/adapter_host_endpoint_report.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <string>
#include <vector>

using dovahlink::adapter::process::TryParseHostEndpointReport;

TEST_CASE("TryParseHostEndpointReport parses a well-formed report with "
          "Windows-style CRLF line endings",
          "[process][adapter_host_endpoint_report]") {
  auto result = TryParseHostEndpointReport("PORT 12345\r", "PROOF a0b1c2\r");

  REQUIRE(result.has_value());
  CHECK(result->port == 12345);
  CHECK(result->proofToken == std::vector<std::byte>{std::byte{0xA0},
                                                     std::byte{0xB1},
                                                     std::byte{0xC2}});
}

TEST_CASE("TryParseHostEndpointReport parses a well-formed report with "
          "plain LF (no trailing carriage return)",
          "[process][adapter_host_endpoint_report]") {
  auto result = TryParseHostEndpointReport("PORT 1", "PROOF ff");

  REQUIRE(result.has_value());
  CHECK(result->port == 1);
  CHECK(result->proofToken == std::vector<std::byte>{std::byte{0xFF}});
}

TEST_CASE("TryParseHostEndpointReport accepts the port boundary values 0 "
          "and 65535",
          "[process][adapter_host_endpoint_report]") {
  for (std::uint16_t port : {std::uint16_t{0}, std::uint16_t{65535}}) {
    auto result =
        TryParseHostEndpointReport("PORT " + std::to_string(port), "PROOF a0");

    REQUIRE(result.has_value());
    CHECK(result->port == port);
  }
}

TEST_CASE("TryParseHostEndpointReport accepts an empty proof token",
          "[process][adapter_host_endpoint_report]") {
  auto result = TryParseHostEndpointReport("PORT 1", "PROOF ");

  REQUIRE(result.has_value());
  CHECK(result->proofToken.empty());
}

TEST_CASE("TryParseHostEndpointReport returns nullopt for malformed input",
          "[process][adapter_host_endpoint_report]") {
  struct Case {
    std::string portLine;
    std::string proofLine;
  };
  for (const Case &testCase :
       {Case{"PORT abc", "PROOF a0"},         // non-numeric port
        Case{"PORT 70000", "PROOF a0"},       // out-of-range port
        Case{"PORT 65536", "PROOF a0"},       // exactly one past the max port
        Case{"PORT 99999999999", "PROOF a0"}, // overflows int outright
        Case{"PORT -1", "PROOF a0"},          // negative port
        Case{"PORT 1", "PROOF a0g"},          // non-hex character
        Case{"PORT 1", "PROOF A0"},           // uppercase hex
        Case{"PORT 1", "PROOF a"},            // odd-length hex
        Case{"NOPORT 1", "PROOF a0"},         // wrong port-line prefix
        Case{"PORT 1", "NOPROOF a0"},         // wrong proof-line prefix
        Case{"", ""},                         // empty lines
        Case{"PORT 1 extra", "PROOF a0"}}) {  // trailing garbage
    INFO("portLine: " << testCase.portLine
                      << ", proofLine: " << testCase.proofLine);
    CHECK_FALSE(
        TryParseHostEndpointReport(testCase.portLine, testCase.proofLine)
            .has_value());
  }
}
