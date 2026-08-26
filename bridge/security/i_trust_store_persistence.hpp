#pragma once

#include "security/trust_store_snapshot.hpp"

#include <optional>

namespace dovahlink::security {

///  Persistence boundary a `TrustStore` reads from and writes to.
///  Implementations own the actual storage mechanism (for example per-user
///  secure storage); `TrustStore` never assumes a file format or storage
///  location. Declared in its own file, split from `WindowsTrustStorePersistence`,
///  because a second implementation is a documented, anticipated port target --
///  see `ai/context/protocol/security.md`'s "Persistent local trust" section: "a
///  future non-Windows platform implements the same `ITrustStorePersistence` port
///  against its own per-user secure-storage primitive." This is not the
///  CommonLib-target-wall split exception `cpp-style.md` describes; a port with
///  more than one anticipated implementation was never eligible for the
///  interface-and-implementation paired-file default in the first place.
class ITrustStorePersistence {
  public:
    ///  Allows derived storage backends to be destroyed through this interface.
    virtual ~ITrustStorePersistence() = default;

    ///  Loads the durable trust snapshot, or `std::nullopt` when storage is
    ///  corrupt or inaccessible. A missing-but-valid empty store is a successful
    ///  load of an empty snapshot, not a `std::nullopt` result.
    [[nodiscard]] virtual std::optional<TrustStoreSnapshot> Load() = 0;

    ///  Durably saves the given snapshot.
    ///  @return Whether the save succeeded.
    [[nodiscard]] virtual bool Save(const TrustStoreSnapshot& snapshot) = 0;
};

} //  namespace dovahlink::security
