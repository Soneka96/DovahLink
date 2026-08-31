#include "ipc/adapter_ipc_target.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <cstdint>
#include <vector>

using dovahlink::adapter::ipc::AdapterIpcTarget;

TEST_CASE("AdapterIpcTarget preserves port, proof token, HostProof key, and "
          "generation as one value") {
  AdapterIpcTarget original{
      .port = 12345,
      .proofToken = {std::byte{1}, std::byte{2}, std::byte{3}},
      .hostProofKey = {std::byte{4}, std::byte{5}},
      .targetGeneration = 9,
  };

  AdapterIpcTarget copy = original;

  CHECK(copy == original);
  CHECK(copy.port == 12345);
  CHECK(copy.proofToken ==
        std::vector<std::byte>{std::byte{1}, std::byte{2}, std::byte{3}});
  CHECK(copy.hostProofKey ==
        std::vector<std::byte>{std::byte{4}, std::byte{5}});
  CHECK(copy.targetGeneration == 9);
}

TEST_CASE("AdapterIpcTarget equality distinguishes every target field") {
  AdapterIpcTarget original{
      .port = 12345,
      .proofToken = {std::byte{1}},
      .hostProofKey = {std::byte{2}},
      .targetGeneration = 9,
  };

  CHECK_FALSE((AdapterIpcTarget{.port = 12346,
                                .proofToken = {std::byte{1}},
                                .hostProofKey = {std::byte{2}},
                                .targetGeneration = 9}) == original);
  CHECK_FALSE((AdapterIpcTarget{.port = 12345,
                                .proofToken = {std::byte{9}},
                                .hostProofKey = {std::byte{2}},
                                .targetGeneration = 9}) == original);
  CHECK_FALSE((AdapterIpcTarget{.port = 12345,
                                .proofToken = {std::byte{1}},
                                .hostProofKey = {std::byte{9}},
                                .targetGeneration = 9}) == original);
  CHECK_FALSE((AdapterIpcTarget{.port = 12345,
                                .proofToken = {std::byte{1}},
                                .hostProofKey = {std::byte{2}},
                                .targetGeneration = 10}) == original);
}
