#include "identity/adapter_instance_id_generator.hpp"
#include "ipc/ipc_constants.hpp"
#include "ipc/ipc_frame_codec.hpp"
#include "ipc/winsock_adapter_ipc_socket.hpp"
#include "process/adapter_host_endpoint.hpp"
#include "process/adapter_host_handshake_verifier.hpp"
#include "process/adapter_host_process_launcher.hpp"
#include "process/adapter_host_rendezvous_reader.hpp"
#include "process/adapter_owner_lifetime_id.hpp"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <chrono>
#include <cstddef>
#include <filesystem>
#include <optional>
#include <system_error>
#include <utility>

using dovahlink::adapter::identity::AdapterInstanceIdGenerator;
using dovahlink::adapter::ipc::IpcFrameCodec;
using dovahlink::adapter::ipc::WinsockAdapterIpcSocket;
using dovahlink::adapter::process::AdapterHostEndpoint;
using dovahlink::adapter::process::AdapterHostHandshakeVerifier;
using dovahlink::adapter::process::DeriveOwnerLifetimeId;
using dovahlink::adapter::process::FileAdapterHostRendezvousReader;
using dovahlink::adapter::process::ResolveDefaultRendezvousFilePath;
using dovahlink::adapter::process::Win32AdapterHostProcessLauncher;

namespace {

///  Removes a test's per-lifetime rendezvous file even when an assertion
///  aborts the test body after the real host has been launched.
class ScopedRendezvousCleanup {
public:
  explicit ScopedRendezvousCleanup(std::filesystem::path path)
      : path_(std::move(path)) {}

  ~ScopedRendezvousCleanup() {
    std::error_code error;
    std::filesystem::remove(path_, error);
  }

  ScopedRendezvousCleanup(const ScopedRendezvousCleanup &) = delete;
  ScopedRendezvousCleanup &operator=(const ScopedRendezvousCleanup &) = delete;

private:
  ///  The per-test rendezvous file to remove.
  std::filesystem::path path_;
};

} //  namespace

TEST_CASE("the native launcher and verifier complete the real C# host "
          "dynamic-port handshake",
          "[process][integration]") {
  const std::filesystem::path hostExecutable{DOVAHLINK_HOST_EXECUTABLE};
  REQUIRE(std::filesystem::exists(hostExecutable));

  const auto ownerLifetimeId = DeriveOwnerLifetimeId();
  const auto rendezvousPath = ResolveDefaultRendezvousFilePath(ownerLifetimeId);
  REQUIRE(rendezvousPath.has_value());
  ScopedRendezvousCleanup rendezvousCleanup(*rendezvousPath);

  std::error_code removeError;
  std::filesystem::remove(*rendezvousPath, removeError);

  Win32AdapterHostProcessLauncher launcher(hostExecutable, ownerLifetimeId,
                                           std::chrono::seconds(10));
  std::optional<AdapterHostEndpoint> launchedEndpoint = launcher.Launch();

  REQUIRE(launchedEndpoint.has_value());
  CHECK(launchedEndpoint->port != 0);
  REQUIRE(launchedEndpoint->proofToken.size() ==
          dovahlink::adapter::ipc::kMaxIpcPeerProofTokenBytes);

  FileAdapterHostRendezvousReader reader(*rendezvousPath);
  std::optional<AdapterHostEndpoint> fileEndpoint = reader.TryRead();
  REQUIRE(fileEndpoint.has_value());
  CHECK(*fileEndpoint == *launchedEndpoint);

  AdapterInstanceIdGenerator idGenerator;
  const auto adapterInstanceId = idGenerator.Generate();
  WinsockAdapterIpcSocket verifierSocket(launchedEndpoint->port);
  IpcFrameCodec codec;
  AdapterHostHandshakeVerifier verifier(adapterInstanceId, ownerLifetimeId,
                                        verifierSocket, codec,
                                        std::chrono::seconds(2));

  CHECK(verifier.Verify(*launchedEndpoint));

  AdapterHostEndpoint forgedEndpoint = *launchedEndpoint;
  forgedEndpoint.proofToken.front() ^= std::byte{0xFF};
  CHECK_FALSE(verifier.Verify(forgedEndpoint));

  CHECK_FALSE(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
  CHECK(launcher.AwaitExitOrTerminate(std::chrono::milliseconds(0)));
}
