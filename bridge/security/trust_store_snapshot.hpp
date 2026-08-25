#pragma once

#include "security/known_device_record.hpp"

#include <vector>

namespace dovahlink::security {

///  The durable state a `TrustStore` loads from and saves to persistent storage.
struct TrustStoreSnapshot {
    ///  Every known device, regardless of state.
    std::vector<KnownDeviceRecord> devices;
};

} //  namespace dovahlink::security
