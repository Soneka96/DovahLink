#include "shared/scoped_release.hpp"

#include <catch2/catch_test_macros.hpp>

#include <optional>
#include <type_traits>
#include <utility>

using dovahlink::shared::ScopedRelease;

static_assert(!std::is_copy_constructible_v<ScopedRelease>);
static_assert(!std::is_copy_assignable_v<ScopedRelease>);
static_assert(std::is_nothrow_move_constructible_v<ScopedRelease>);
static_assert(std::is_nothrow_move_assignable_v<ScopedRelease>);

TEST_CASE("destroying a ScopedRelease runs its action",
          "[shared][scoped_release]") {
    int calls = 0;
    {
        ScopedRelease release([&calls] { ++calls; });
    }
    CHECK(calls == 1);
}

TEST_CASE("Dismiss suppresses the action on destruction",
          "[shared][scoped_release]") {
    int calls = 0;
    {
        ScopedRelease release([&calls] { ++calls; });
        release.Dismiss();
    }
    CHECK(calls == 0);
}

TEST_CASE("Trigger runs the action early and destruction does not run it again",
          "[shared][scoped_release]") {
    int calls = 0;
    {
        ScopedRelease release([&calls] { ++calls; });
        release.Trigger();
        CHECK(calls == 1);
    }
    CHECK(calls == 1);
}

TEST_CASE("Trigger is harmless to call more than once",
          "[shared][scoped_release]") {
    int calls = 0;
    ScopedRelease release([&calls] { ++calls; });
    release.Trigger();
    release.Trigger();
    CHECK(calls == 1);
}

TEST_CASE("Trigger after Dismiss does not run the action",
          "[shared][scoped_release]") {
    int calls = 0;
    ScopedRelease release([&calls] { ++calls; });
    release.Dismiss();
    release.Trigger();
    CHECK(calls == 0);
}

TEST_CASE("moving a ScopedRelease transfers the action; the moved-from "
          "instance runs nothing",
          "[shared][scoped_release]") {
    int calls = 0;
    {
        ScopedRelease release([&calls] { ++calls; });
        std::optional<ScopedRelease> moved{std::move(release)};
        release.Dismiss();
        CHECK(calls == 0);
    }
    CHECK(calls == 1);
}

TEST_CASE("move-assigning a ScopedRelease runs the target's pending action "
          "first, then transfers the source's",
          "[shared][scoped_release]") {
    int firstCalls = 0;
    int secondCalls = 0;
    {
        ScopedRelease first([&firstCalls] { ++firstCalls; });
        ScopedRelease second([&secondCalls] { ++secondCalls; });
        first = std::move(second);
        CHECK(firstCalls == 1);
        CHECK(secondCalls == 0);
    }
    CHECK(secondCalls == 1);
}

TEST_CASE("self-move-assignment is safe and does not rerun the action",
          "[shared][scoped_release]") {
    int calls = 0;
    ScopedRelease release([&calls] { ++calls; });
    ScopedRelease& alias = release;
    release = std::move(alias);
    CHECK(calls == 0);
    release.Trigger();
    CHECK(calls == 1);
}

TEST_CASE("moving from an already-triggered ScopedRelease is a no-op",
          "[shared][scoped_release]") {
    int calls = 0;
    ScopedRelease release([&calls] { ++calls; });
    release.Trigger();
    CHECK(calls == 1);

    std::optional<ScopedRelease> moved{std::move(release)};
    moved.reset();
    CHECK(calls == 1);
}

TEST_CASE("move-assigning from an already-dismissed ScopedRelease runs "
          "nothing for the source",
          "[shared][scoped_release]") {
    int firstCalls = 0;
    int secondCalls = 0;
    {
        ScopedRelease first([&firstCalls] { ++firstCalls; });
        ScopedRelease second([&secondCalls] { ++secondCalls; });
        second.Dismiss();
        first = std::move(second);
        CHECK(firstCalls == 1);
    }
    CHECK(secondCalls == 0);
}
