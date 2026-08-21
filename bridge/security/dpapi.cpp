#include "security/dpapi.hpp"

#include <windows.h>

#include <wincrypt.h>

namespace dovahlink::security {

namespace {

/// Builds a non-owning `DATA_BLOB` view over `bytes`. The caller must keep `bytes` alive for the
/// blob's lifetime.
DATA_BLOB MakeBlobView(const std::vector<std::uint8_t>& bytes) {
    DATA_BLOB blob;
    blob.cbData = static_cast<DWORD>(bytes.size());
    blob.pbData = const_cast<BYTE*>(bytes.data());
    return blob;
}

/// Copies a DPAPI-allocated output blob into an owned vector and frees the blob's memory.
std::vector<std::uint8_t> TakeOwnership(DATA_BLOB& blob) {
    std::vector<std::uint8_t> result(blob.pbData, blob.pbData + blob.cbData);
    LocalFree(blob.pbData);
    return result;
}

}  // namespace

std::optional<std::vector<std::uint8_t>> EncryptForCurrentUser(
    const std::vector<std::uint8_t>& plaintext) {
    DATA_BLOB input = MakeBlobView(plaintext);
    DATA_BLOB output{};

    // No CRYPTPROTECT_LOCAL_MACHINE: default per-user scope. No entropy or description: the
    // approved design does not add a second secret to manage.
    // Untested: this failure branch has no unit test -- per-user CryptProtectData with
    // UI_FORBIDDEN only fails on profile corruption or resource exhaustion, neither reproducible
    // in-process; DecryptForCurrentUser's equivalent failure path is exercised directly instead.
    if (!CryptProtectData(&input, /*szDataDescr=*/nullptr, /*pOptionalEntropy=*/nullptr,
                           /*pvReserved=*/nullptr, /*pPromptStruct=*/nullptr,
                           CRYPTPROTECT_UI_FORBIDDEN, &output)) {
        return std::nullopt;
    }
    return TakeOwnership(output);
}

std::optional<std::vector<std::uint8_t>> DecryptForCurrentUser(
    const std::vector<std::uint8_t>& ciphertext) {
    DATA_BLOB input = MakeBlobView(ciphertext);
    DATA_BLOB output{};

    if (!CryptUnprotectData(&input, /*ppszDataDescr=*/nullptr, /*pOptionalEntropy=*/nullptr,
                             /*pvReserved=*/nullptr, /*pPromptStruct=*/nullptr,
                             CRYPTPROTECT_UI_FORBIDDEN, &output)) {
        return std::nullopt;
    }
    return TakeOwnership(output);
}

}  // namespace dovahlink::security
