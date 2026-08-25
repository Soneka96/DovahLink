#include "protocol/capabilities_payload.hpp"

#include "protocol/json_field_decoders.hpp"

#include <boost/json/array.hpp>

#include <utility>

namespace dovahlink::protocol {

std::expected<CapabilitiesPayload, MessageError>
DecodeCapabilitiesPayload(const boost::json::object &payload) {
  const boost::json::value *capabilitiesValue =
      RequireField(payload, "capabilities");
  if (!capabilitiesValue) {
    return Fail("missing required field: capabilities");
  }
  if (!capabilitiesValue->is_array()) {
    return Fail("capabilities must be an array");
  }

  std::vector<Capability> capabilities;
  capabilities.reserve(capabilitiesValue->get_array().size());
  for (const boost::json::value &item : capabilitiesValue->get_array()) {
    if (!item.is_object()) {
      return Fail("each capability must be an object");
    }
    const boost::json::object &capObj = item.get_object();

    auto id =
        DecodeNonEmptyString(RequireField(capObj, "id"), "capabilities[].id");
    if (!id) {
      return std::unexpected(id.error());
    }
    auto version = DecodeNonNegativeInt(RequireField(capObj, "version"),
                                        "capabilities[].version");
    if (!version) {
      return std::unexpected(version.error());
    }
    capabilities.push_back(
        Capability{.id = std::move(*id), .version = *version});
  }

  return CapabilitiesPayload{.capabilities = std::move(capabilities)};
}

boost::json::object
EncodeCapabilitiesPayload(const CapabilitiesPayload &payload) {
  boost::json::array capabilities;
  capabilities.reserve(payload.capabilities.size());
  for (const Capability &capability : payload.capabilities) {
    boost::json::object capObj;
    capObj["id"] = capability.id;
    capObj["version"] = capability.version;
    capabilities.push_back(std::move(capObj));
  }
  boost::json::object obj;
  obj["capabilities"] = std::move(capabilities);
  return obj;
}

} // namespace dovahlink::protocol
