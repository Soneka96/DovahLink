#include "security/dpapi.hpp"

#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <cstdint>
#include <vector>

using dovahlink::security::DecryptForCurrentUser;
using dovahlink::security::EncryptForCurrentUser;

namespace {

/// Builds a deterministic byte sequence from a seed value.
std::vector<std::uint8_t> MakeBytes(std::uint8_t seed, std::size_t size) {
    std::vector<std::uint8_t> bytes(size);
    for (std::size_t i = 0; i < size; ++i) {
        bytes[i] = static_cast<std::uint8_t>(seed + i);
    }
    return bytes;
}

}  // namespace

TEST_CASE("encrypted plaintext decrypts back to the original bytes", "[security][dpapi]") {
    auto plaintext = MakeBytes(1, 64);
    auto ciphertext = EncryptForCurrentUser(plaintext);
    REQUIRE(ciphertext.has_value());

    auto decrypted = DecryptForCurrentUser(*ciphertext);

    REQUIRE(decrypted.has_value());
    CHECK(*decrypted == plaintext);
}

TEST_CASE("ciphertext differs from the plaintext it encrypts", "[security][dpapi]") {
    auto plaintext = MakeBytes(1, 64);
    auto ciphertext = EncryptForCurrentUser(plaintext);
    REQUIRE(ciphertext.has_value());

    CHECK(*ciphertext != plaintext);
}

TEST_CASE("ciphertext does not contain the plaintext as a contiguous byte run",
          "[security][dpapi]") {
    // A cheap sanity check that DPAPI is genuinely transforming the bytes rather than wrapping
    // them unchanged; not a cryptographic opacity proof.
    auto plaintext = MakeBytes(1, 64);
    auto ciphertext = EncryptForCurrentUser(plaintext);
    REQUIRE(ciphertext.has_value());

    auto found = std::search(ciphertext->begin(), ciphertext->end(), plaintext.begin(), plaintext.end());

    CHECK(found == ciphertext->end());
}

TEST_CASE("two encryptions of identical plaintext produce different ciphertext",
          "[security][dpapi]") {
    auto plaintext = MakeBytes(1, 32);
    auto first = EncryptForCurrentUser(plaintext);
    auto second = EncryptForCurrentUser(plaintext);

    REQUIRE(first.has_value());
    REQUIRE(second.has_value());
    CHECK(*first != *second);
}

TEST_CASE("decrypting a bit-flipped ciphertext fails closed", "[security][dpapi]") {
    auto plaintext = MakeBytes(1, 64);
    auto ciphertext = EncryptForCurrentUser(plaintext);
    REQUIRE(ciphertext.has_value());
    (*ciphertext)[ciphertext->size() / 2] ^= 0xFF;

    auto decrypted = DecryptForCurrentUser(*ciphertext);

    CHECK_FALSE(decrypted.has_value());
}

TEST_CASE("decrypting a truncated ciphertext fails closed", "[security][dpapi]") {
    auto plaintext = MakeBytes(1, 64);
    auto ciphertext = EncryptForCurrentUser(plaintext);
    REQUIRE(ciphertext.has_value());
    ciphertext->resize(ciphertext->size() / 2);

    auto decrypted = DecryptForCurrentUser(*ciphertext);

    CHECK_FALSE(decrypted.has_value());
}

TEST_CASE("decrypting arbitrary garbage bytes fails closed without crashing",
          "[security][dpapi]") {
    auto garbage = MakeBytes(0xAB, 16);

    auto decrypted = DecryptForCurrentUser(garbage);

    CHECK_FALSE(decrypted.has_value());
}

TEST_CASE("decrypting an empty buffer fails closed without crashing", "[security][dpapi]") {
    auto decrypted = DecryptForCurrentUser(std::vector<std::uint8_t>{});

    CHECK_FALSE(decrypted.has_value());
}
