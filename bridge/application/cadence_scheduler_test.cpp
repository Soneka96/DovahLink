#include "application/cadence_scheduler.hpp"

#include <catch2/catch_test_macros.hpp>

#include <chrono>
#include <string>
#include <vector>

using dovahlink::application::CadenceScheduler;
using dovahlink::application::RateClass;
using namespace std::chrono;

namespace {

///  Builds the steady-clock instant `seconds` after the scheduler's
///  alignment reference (`steady_clock::time_point{}`).
steady_clock::time_point AtSeconds(long long seconds) {
    return steady_clock::time_point{} + std::chrono::seconds(seconds);
}

} //  namespace

TEST_CASE("DueKeys reports Fast, Medium, and Slow together at second 0",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("fast", RateClass::kFast, {});
    scheduler.RegisterSampled("medium", RateClass::kMedium, {});
    scheduler.RegisterSampled("slow", RateClass::kSlow, {});

    auto due = scheduler.DueKeys(AtSeconds(0));

    CHECK(due.size() == 3);
}

TEST_CASE("DueKeys matches the roadmap's aligned Fast/Medium/Slow cadence "
          "across seconds 0 through 6",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("fast", RateClass::kFast, {});
    scheduler.RegisterSampled("medium", RateClass::kMedium, {});
    scheduler.RegisterSampled("slow", RateClass::kSlow, {});

    CHECK(scheduler.DueKeys(AtSeconds(0)).size() == 3); //  fast, medium, slow
    CHECK(scheduler.DueKeys(AtSeconds(1)) ==
          std::vector<std::string>{"fast"});
    CHECK(scheduler.DueKeys(AtSeconds(2)).size() == 2); //  fast, medium
    CHECK(scheduler.DueKeys(AtSeconds(3)).size() == 2); //  fast, slow
    CHECK(scheduler.DueKeys(AtSeconds(4)).size() == 2); //  fast, medium
    CHECK(scheduler.DueKeys(AtSeconds(5)) ==
          std::vector<std::string>{"fast"});
    CHECK(scheduler.DueKeys(AtSeconds(6)).size() == 3); //  fast, medium, slow
}

TEST_CASE("DueKeys returns no keys for a scheduler with nothing registered",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;

    CHECK(scheduler.DueKeys(AtSeconds(0)).empty());
}

TEST_CASE("DueKeys excludes a staggered key before its first due instant",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("staggered", RateClass::kFast, seconds(2));

    CHECK(scheduler.DueKeys(AtSeconds(0)).empty());
    CHECK(scheduler.DueKeys(AtSeconds(1)).empty());
    CHECK(scheduler.DueKeys(AtSeconds(2)) ==
          std::vector<std::string>{"staggered"});
}

TEST_CASE("DueKeys excludes a key that is not yet due",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("slow", RateClass::kSlow, {});

    CHECK(scheduler.DueKeys(AtSeconds(0)) ==
          std::vector<std::string>{"slow"});
    CHECK(scheduler.DueKeys(AtSeconds(1)).empty());
    CHECK(scheduler.DueKeys(AtSeconds(2)).empty());
}

TEST_CASE("DueKeys skips missed instants instead of issuing a catch-up "
          "burst",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("fast", RateClass::kFast, {});
    //  Consume the first due instant so the gap below is a genuine miss,
    //  not the key's first-ever report.
    REQUIRE(scheduler.DueKeys(AtSeconds(0)) ==
            std::vector<std::string>{"fast"});

    //  Seconds 1 through 4 are never queried.
    auto due = scheduler.DueKeys(AtSeconds(5));

    CHECK(due == std::vector<std::string>{"fast"});
    //  The next due instant lands immediately after the queried time, not
    //  somewhere inside the skipped backlog.
    CHECK(scheduler.DueKeys(AtSeconds(5)).empty());
    CHECK(scheduler.DueKeys(AtSeconds(6)) ==
          std::vector<std::string>{"fast"});
}

TEST_CASE("RegisterSampled's stagger offset shifts a key's due instants",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("unstaggered", RateClass::kFast, {});
    scheduler.RegisterSampled("staggered", RateClass::kFast, milliseconds(500));

    CHECK(scheduler.DueKeys(AtSeconds(0)) ==
          std::vector<std::string>{"unstaggered"});
    CHECK(scheduler.DueKeys(steady_clock::time_point{} + milliseconds(500)) ==
          std::vector<std::string>{"staggered"});
    CHECK(scheduler.DueKeys(AtSeconds(1)) ==
          std::vector<std::string>{"unstaggered"});
}

TEST_CASE("RegisterSampled replaces a previously registered cadence for the "
          "same key",
          "[application][cadence_scheduler]") {
    CadenceScheduler scheduler;
    scheduler.RegisterSampled("value", RateClass::kSlow, {});

    scheduler.RegisterSampled("value", RateClass::kFast, {});

    //  Second 1 is due for kFast but not for the original kSlow
    //  registration, proving the replacement took effect.
    CHECK(scheduler.DueKeys(AtSeconds(0)) == std::vector<std::string>{"value"});
    CHECK(scheduler.DueKeys(AtSeconds(1)) == std::vector<std::string>{"value"});
}
