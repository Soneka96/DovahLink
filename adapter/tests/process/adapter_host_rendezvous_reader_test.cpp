#include "process/adapter_host_rendezvous_reader.hpp"

#include "process/adapter_host_constants.hpp"
#include "process/adapter_owner_lifetime_id.hpp"
#include "test_support/source_text_test_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <cstddef>
#include <fstream>
#include <random>

using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::FileAdapterHostRendezvousReader;
using dovahlink::adapter::process::ResolveDefaultRendezvousFilePath;
using dovahlink::adapter::test_support::ReadSource;

namespace {

///  A fresh, unique temporary file path per test, so parallel and repeated
///  test runs never collide.
std::filesystem::path UniqueTempFilePath() {
  std::mt19937_64 engine{std::random_device{}()};
  return std::filesystem::temp_directory_path() /
         ("dovahlink-rendezvous-test-" + std::to_string(engine()) + ".dat");
}

///  Writes `content` verbatim (no line-ending translation) to `path`.
void WriteRawFile(const std::filesystem::path &path,
                  const std::string &content) {
  std::ofstream file(path, std::ios::binary);
  file << content;
}

///  A representative, fixed owner-lifetime-id for tests that don't care
///  about its value.
std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
SampleLifetimeId() {
  std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes> id{};
  for (std::size_t index = 0; index < id.size(); ++index) {
    id[index] = static_cast<std::byte>(index + 1);
  }
  return id;
}

} //  namespace

TEST_CASE("FileAdapterHostRendezvousReader reads a well-formed file with "
          "Windows-style CRLF line endings",
          "[process][adapter_host_rendezvous_reader]") {
  std::filesystem::path path = UniqueTempFilePath();
  WriteRawFile(path, "PORT 12345\r\nPROOF a0b1c2\r\n");
  FileAdapterHostRendezvousReader reader(path);

  auto result = reader.TryRead();

  std::filesystem::remove(path);
  REQUIRE(result.has_value());
  CHECK(result->port == 12345);
  CHECK(result->proofToken == std::vector<std::byte>{std::byte{0xA0},
                                                     std::byte{0xB1},
                                                     std::byte{0xC2}});
}

TEST_CASE("FileAdapterHostRendezvousReader reads a well-formed file with "
          "plain LF line endings",
          "[process][adapter_host_rendezvous_reader]") {
  std::filesystem::path path = UniqueTempFilePath();
  WriteRawFile(path, "PORT 1\nPROOF ff\n");
  FileAdapterHostRendezvousReader reader(path);

  auto result = reader.TryRead();

  std::filesystem::remove(path);
  REQUIRE(result.has_value());
  CHECK(result->port == 1);
  CHECK(result->proofToken == std::vector<std::byte>{std::byte{0xFF}});
}

TEST_CASE("FileAdapterHostRendezvousReader accepts the port boundary "
          "values 0 and 65535",
          "[process][adapter_host_rendezvous_reader]") {
  for (std::uint16_t port : {std::uint16_t{0}, std::uint16_t{65535}}) {
    std::filesystem::path path = UniqueTempFilePath();
    WriteRawFile(path, "PORT " + std::to_string(port) + "\nPROOF a0\n");
    FileAdapterHostRendezvousReader reader(path);

    auto result = reader.TryRead();

    std::filesystem::remove(path);
    REQUIRE(result.has_value());
    CHECK(result->port == port);
  }
}

TEST_CASE("FileAdapterHostRendezvousReader reads an empty proof token",
          "[process][adapter_host_rendezvous_reader]") {
  std::filesystem::path path = UniqueTempFilePath();
  WriteRawFile(path, "PORT 1\nPROOF \n");
  FileAdapterHostRendezvousReader reader(path);

  auto result = reader.TryRead();

  std::filesystem::remove(path);
  REQUIRE(result.has_value());
  CHECK(result->proofToken.empty());
}

TEST_CASE("FileAdapterHostRendezvousReader returns nullopt for a missing "
          "file",
          "[process][adapter_host_rendezvous_reader]") {
  FileAdapterHostRendezvousReader reader(UniqueTempFilePath());

  CHECK_FALSE(reader.TryRead().has_value());
}

TEST_CASE("FileAdapterHostRendezvousReader returns nullopt for malformed "
          "content",
          "[process][adapter_host_rendezvous_reader]") {
  for (const std::string &content :
       {std::string("PORT 1\n"),                    // missing PROOF line
        std::string("PORT abc\nPROOF a0\n"),        // non-numeric port
        std::string("PORT 70000\nPROOF a0\n"),      // out-of-range port
        std::string("PORT -1\nPROOF a0\n"),         // negative port
        std::string("PORT 1\nPROOF a0g\n"),         // non-hex character
        std::string("PORT 1\nPROOF A0\n"),          // uppercase hex
        std::string("PORT 1\nPROOF a\n"),           // odd-length hex
        std::string("NOPORT 1\nPROOF a0\n"),        // wrong prefix
        std::string(""),                            // empty file
        std::string("PORT 1 extra\nPROOF a0\n")}) { // trailing garbage
    std::filesystem::path path = UniqueTempFilePath();
    WriteRawFile(path, content);
    FileAdapterHostRendezvousReader reader(path);

    INFO("content: " << content);
    CHECK_FALSE(reader.TryRead().has_value());

    std::filesystem::remove(path);
  }
}

TEST_CASE("FileAdapterHostRendezvousReader rejects lines beyond the bounded "
          "report size") {
  const std::size_t maximum =
      dovahlink::adapter::process::kMaxAdapterHostRendezvousLineBytes;
  for (const std::string &content :
       {"PORT " + std::string(maximum, '1') + "\nPROOF a0\n",
        "PORT 1\nPROOF " + std::string(maximum, 'a') + "\n",
        "PORT " + std::string(maximum + 1, '1') + "\nPROOF a0\n"}) {
    std::filesystem::path path = UniqueTempFilePath();
    WriteRawFile(path, content);
    FileAdapterHostRendezvousReader reader(path);

    CHECK_FALSE(reader.TryRead().has_value());
    std::filesystem::remove(path);
  }
}

TEST_CASE("FileAdapterHostRendezvousReader uses a bounded line reader",
          "[process][adapter_host_rendezvous_reader][structural]") {
  std::filesystem::path sourcePath =
      std::filesystem::path(DOVAHLINK_ADAPTER_PROCESS_DIR) /
      "adapter_host_rendezvous_reader.cpp";
  std::string source = ReadSource(sourcePath);

  CHECK(source.find("ReadBoundedLine") != std::string::npos);
  CHECK(source.find("kMaxAdapterHostRendezvousLineBytes") != std::string::npos);
  CHECK(source.find("std::getline") == std::string::npos);
}

TEST_CASE("ResolveDefaultRendezvousFilePath embeds the formatted "
          "owner-lifetime-id in the resolved file name",
          "[process][adapter_host_rendezvous_reader]") {
  auto id = SampleLifetimeId();

  auto path = ResolveDefaultRendezvousFilePath(id);

  REQUIRE(path.has_value());
  CHECK(path->filename().string().find(
            dovahlink::adapter::process::FormatOwnerLifetimeId(id)) !=
        std::string::npos);
}

TEST_CASE("ResolveDefaultRendezvousFilePath produces different paths for "
          "different owner-lifetime-ids",
          "[process][adapter_host_rendezvous_reader]") {
  std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
      first = SampleLifetimeId();
  std::array<std::byte, dovahlink::adapter::ipc::kIpcOwnerLifetimeIdBytes>
      second = SampleLifetimeId();
  second[0] = std::byte{0xFF};

  auto firstPath = ResolveDefaultRendezvousFilePath(first);
  auto secondPath = ResolveDefaultRendezvousFilePath(second);

  REQUIRE(firstPath.has_value());
  REQUIRE(secondPath.has_value());
  CHECK(*firstPath != *secondPath);
}

TEST_CASE("ResolveDefaultRendezvousFilePath is stable for the same "
          "owner-lifetime-id",
          "[process][adapter_host_rendezvous_reader]") {
  auto id = SampleLifetimeId();

  CHECK(ResolveDefaultRendezvousFilePath(id) ==
        ResolveDefaultRendezvousFilePath(id));
}
