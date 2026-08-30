#pragma once

#include "identity/adapter_instance_id.hpp"

namespace dovahlink::adapter::identity {

///  Generates a fresh `AdapterInstanceId` for the adapter's current process
///  lifetime. Not security-sensitive: this identifies a connection instance
///  for peer-ownership bookkeeping, distinct from the separate peer-proof
///  token that actually establishes trust.
class IAdapterInstanceIdGenerator {
public:
  virtual ~IAdapterInstanceIdGenerator() = default;

  ///  Generates a new, randomly generated identifier.
  virtual AdapterInstanceId Generate() = 0;
};

///  @copydoc IAdapterInstanceIdGenerator
class AdapterInstanceIdGenerator final : public IAdapterInstanceIdGenerator {
public:
  ///  @copydoc IAdapterInstanceIdGenerator::Generate
  AdapterInstanceId Generate() override;
};

} //  namespace dovahlink::adapter::identity
