#include "ipc/settable_adapter_ipc_peer_proof_provider.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <thread>
#include <vector>

using dovahlink::adapter::ipc::SettableAdapterIpcPeerProofProvider;

TEST_CASE("SettableAdapterIpcPeerProofProvider returns an empty token before "
          "SetToken is called",
          "[ipc][settable_adapter_ipc_peer_proof_provider]") {
  SettableAdapterIpcPeerProofProvider provider;

  CHECK(provider.Token().empty());
}

TEST_CASE("SettableAdapterIpcPeerProofProvider returns the configured token "
          "after SetToken",
          "[ipc][settable_adapter_ipc_peer_proof_provider]") {
  SettableAdapterIpcPeerProofProvider provider;

  provider.SetToken({std::byte{1}, std::byte{2}, std::byte{3}});

  CHECK(provider.Token() ==
        std::vector<std::byte>{std::byte{1}, std::byte{2}, std::byte{3}});
}

TEST_CASE("SettableAdapterIpcPeerProofProvider reflects the most recent "
          "SetToken call",
          "[ipc][settable_adapter_ipc_peer_proof_provider]") {
  SettableAdapterIpcPeerProofProvider provider;

  provider.SetToken({std::byte{1}});
  provider.SetToken({std::byte{2}, std::byte{3}});

  CHECK(provider.Token() == std::vector<std::byte>{std::byte{2}, std::byte{3}});
}

TEST_CASE("SettableAdapterIpcPeerProofProvider::SetToken can reset the "
          "token back to empty",
          "[ipc][settable_adapter_ipc_peer_proof_provider]") {
  SettableAdapterIpcPeerProofProvider provider;
  provider.SetToken({std::byte{1}, std::byte{2}});

  provider.SetToken({});

  CHECK(provider.Token().empty());
}

TEST_CASE("SettableAdapterIpcPeerProofProvider::Token returns an "
          "independent copy, not an alias of the stored token",
          "[ipc][settable_adapter_ipc_peer_proof_provider]") {
  SettableAdapterIpcPeerProofProvider provider;
  provider.SetToken({std::byte{1}, std::byte{2}, std::byte{3}});

  std::vector<std::byte> returned = provider.Token();
  returned[0] = std::byte{99};

  CHECK(provider.Token() ==
        std::vector<std::byte>{std::byte{1}, std::byte{2}, std::byte{3}});
}

TEST_CASE("SettableAdapterIpcPeerProofProvider::Token is safe to call "
          "concurrently with SetToken",
          "[ipc][settable_adapter_ipc_peer_proof_provider]") {
  SettableAdapterIpcPeerProofProvider provider;

  std::thread writer([&] {
    for (int i = 0; i < 1000; ++i) {
      provider.SetToken({static_cast<std::byte>(i)});
    }
  });
  std::thread reader([&] {
    for (int i = 0; i < 1000; ++i) {
      (void)provider.Token();
    }
  });
  writer.join();
  reader.join();
}
