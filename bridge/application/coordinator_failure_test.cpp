#include "application/coordinator.hpp"

#include <catch2/catch_test_macros.hpp>

#include <stdexcept>
#include <string>
#include <vector>

using dovahlink::application::CallbackRegistry;
using dovahlink::application::Coordinator;
using dovahlink::application::TransportLifecycle;
using dovahlink::application::WorkerPool;

namespace {

class NoopCallbackRegistry : public CallbackRegistry {
public:
    void RegisterAll() override {}
    void UnregisterAll() override {}
};

class NoopWorkerPool : public WorkerPool {
public:
    void Start() override {}
    void Stop() override {}
    void Join() override {}
};

class NoopTransportLifecycle : public TransportLifecycle {
public:
    void Start() override {}
    void CancelCompletions() override {}
    void Close() override {}
};

struct Fixture {
    NoopCallbackRegistry callbacks;
    NoopWorkerPool workers;
    NoopTransportLifecycle transport;
    Coordinator coordinator{callbacks, workers, transport};
};

}  // namespace

TEST_CASE("a fresh coordinator is available", "[application][coordinator][failure]") {
    Fixture f;
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained returns true and stays available when work does not throw",
          "[application][coordinator][failure]") {
    Fixture f;
    bool ran = false;
    bool result = f.coordinator.RunContained([&ran] { ran = true; });

    CHECK(result);
    CHECK(ran);
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches a std::exception thrown from a callback boundary and marks unavailable",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw std::runtime_error("callback failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches an exception thrown from a worker-thread boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw std::runtime_error("worker loop failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches an exception thrown from a transport-completion boundary",
          "[application][coordinator][failure]") {
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw std::runtime_error("transport completion failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained catches a non-std::exception throwable", "[application][coordinator][failure]") {
    // The "never allow an exception to escape" rule applies to anything
    // throwable, not only std::exception subclasses.
    Fixture f;
    bool result = f.coordinator.RunContained([] { throw 42; });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("ResetAvailability restores availability after a failure",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    f.coordinator.ResetAvailability();
    CHECK(f.coordinator.IsAvailable());
}

TEST_CASE("a second failure after ResetAvailability marks the coordinator unavailable again",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("first failure"); });
    f.coordinator.ResetAvailability();
    REQUIRE(f.coordinator.IsAvailable());

    f.coordinator.RunContained([] { throw std::runtime_error("second failure"); });
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("multiple RegisterFailure calls without a reset between them are safe",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RegisterFailure();
    f.coordinator.RegisterFailure();
    f.coordinator.RegisterFailure();

    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("a later successful RunContained call does not restore availability on its own",
          "[application][coordinator][failure]") {
    // Availability is only restored by an explicit ResetAvailability call once
    // a fresh snapshot is published on a reconnected session -- a merely
    // successful, unrelated piece of contained work must not silently clear
    // an existing failure.
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    bool result = f.coordinator.RunContained([] { /* succeeds */ });

    CHECK(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("a failure registered before shutdown does not block or alter the shutdown barrier",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("boom"); });
    REQUIRE_FALSE(f.coordinator.IsAvailable());

    f.coordinator.Shutdown();  // must complete normally, not hang.

    CHECK(f.coordinator.IsStopping());
    // Shutdown does not itself restore availability; that stays an explicit,
    // reconnect-driven decision (ResetAvailability).
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("RunContained after shutdown still contains the exception without hanging or crashing",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Shutdown();
    REQUIRE(f.coordinator.IsStopping());

    bool result = f.coordinator.RunContained([] { throw std::runtime_error("late failure"); });

    CHECK_FALSE(result);
    CHECK_FALSE(f.coordinator.IsAvailable());
}

TEST_CASE("ResetAvailability after shutdown does not affect the already-completed shutdown state",
          "[application][coordinator][failure]") {
    Fixture f;
    f.coordinator.Shutdown();
    f.coordinator.ResetAvailability();

    CHECK(f.coordinator.IsAvailable());
    CHECK(f.coordinator.IsStopping());  // shutdown remains in effect regardless.
}

TEST_CASE("RunContained does not let an exception propagate past it", "[application][coordinator][failure]") {
    // If this were not caught, the test itself would fail with an unhandled
    // exception rather than reaching the checks below.
    Fixture f;
    f.coordinator.RunContained([] { throw std::runtime_error("must not escape"); });
    SUCCEED("RunContained returned normally instead of propagating the exception");
}
