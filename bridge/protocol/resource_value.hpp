#pragma once

namespace dovahlink::protocol {

/// Current and maximum values for one character resource pool.
struct ResourceValue {
    /// Current resource amount.
    double current = 0.0;
    /// Maximum resource amount.
    double maximum = 0.0;
};

}  // namespace dovahlink::protocol
