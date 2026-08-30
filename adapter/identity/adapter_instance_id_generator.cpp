#include "identity/adapter_instance_id_generator.hpp"

#include <algorithm>
#include <random>

namespace dovahlink::adapter::identity {

namespace {

///  A thread-local random engine, seeded once per thread from a
///  non-deterministic source. Keeps generation free of shared mutable state
///  across `AdapterInstanceIdGenerator` instances or threads, without paying
///  the cost of reseeding on every call.
std::mt19937_64 &RandomEngine() {
  thread_local std::mt19937_64 engine{std::random_device{}()};
  return engine;
}

} //  namespace

AdapterInstanceId AdapterInstanceIdGenerator::Generate() {
  std::uniform_int_distribution<int> byteDistribution(0, 255);
  AdapterInstanceId id;
  std::ranges::generate(id.value, [&] {
    return static_cast<std::byte>(byteDistribution(RandomEngine()));
  });
  return id;
}

} //  namespace dovahlink::adapter::identity
