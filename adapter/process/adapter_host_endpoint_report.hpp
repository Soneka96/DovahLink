#pragma once

#include "process/adapter_host_endpoint.hpp"

#include <optional>
#include <string_view>

namespace dovahlink::adapter::process {

///  Parses one three-line `PORT <n>` / `PROOF <hex>` / `HOSTPROOF <hex>`
///  endpoint report -- the format a packaged host process writes both to its
///  rendezvous file (`FileAdapterHostRendezvousReader`) and to its own
///  redirected stdout when launched (`Win32AdapterHostProcessLauncher`) --
///  into a candidate endpoint. Tolerates a single trailing `\r` on any line,
///  so a caller that split its source on `\n` alone (as `std::getline` and a
///  raw pipe-line reader both do) does not need to trim it first. Discovery
///  only, never authentication: a well-formed report is still just a
///  candidate that must pass the full mutual Hello/HelloAck handshake
///  before it is trusted.
///  @return The parsed endpoint, or `std::nullopt` if any line is not
///  well-formed.
std::optional<AdapterHostEndpoint>
TryParseHostEndpointReport(std::string_view portLine,
                           std::string_view proofLine,
                           std::string_view hostProofLine);

} //  namespace dovahlink::adapter::process
