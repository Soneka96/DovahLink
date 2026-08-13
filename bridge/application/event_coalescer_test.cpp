#include "application/event_coalescer.hpp"

#include "application/outbound_queue.hpp"
#include "application/revision_tracker.hpp"
#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <cstdint>
#include <string>
#include <utility>

using dovahlink::application::EnqueueResult;
using dovahlink::application::EventCoalescer;
using dovahlink::application::OutboundQueue;
using dovahlink::application::RevisionTracker;

TEST_CASE("Flush delivers a published event into the queue's event lane", "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "event-1");
    coalescer.Flush();

    CHECK(queue.EventLaneSize() == 1);
    CHECK(queue.DequeueEvent() == "event-1");
}

TEST_CASE("publishing a second assigned event for one area requires snapshot recovery",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    CHECK(coalescer.PublishEvent("character", "first-assigned-event"));
    CHECK_FALSE(coalescer.PublishEvent("character", "replacement-with-dependent-revision"));
    CHECK(coalescer.PendingCount() == 0);
    CHECK(coalescer.NeedsRecovery("character"));

    coalescer.Flush();

    CHECK(queue.EventLaneSize() == 0);
}

TEST_CASE("attempted replacement never delivers an event with a missing base revision",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);
    RevisionTracker revisions;
    REQUIRE(revisions.StartSnapshot("character") == 1);

    auto first = revisions.NextEvent("character");
    REQUIRE(first == std::make_pair(std::int64_t{1}, std::int64_t{2}));
    REQUIRE(coalescer.PublishEvent("character", "base-1-revision-2"));

    auto second = revisions.NextEvent("character");
    REQUIRE(second == std::make_pair(std::int64_t{2}, std::int64_t{3}));
    CHECK_FALSE(coalescer.PublishEvent("character", "base-2-revision-3"));

    coalescer.Flush();
    CHECK_FALSE(queue.DequeueEvent().has_value());
    CHECK(coalescer.NeedsRecovery("character"));
    CHECK(revisions.StartSnapshot("character") == 4);
}

TEST_CASE("publishing for different areas keeps them independent", "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "character-event");
    coalescer.PublishEvent("location", "location-event");
    CHECK(coalescer.PendingCount() == 2);

    coalescer.Flush();

    CHECK(queue.EventLaneSize() == 2);
}

TEST_CASE("PendingCount reflects staged events not yet flushed", "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    CHECK(coalescer.PendingCount() == 0);
    coalescer.PublishEvent("character", "event-1");
    CHECK(coalescer.PendingCount() == 1);
    coalescer.Flush();
    CHECK(coalescer.PendingCount() == 0);
}

TEST_CASE("Flush marks a state area for recovery when the event lane is already full",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "event-1");
    coalescer.Flush();

    CHECK(coalescer.NeedsRecovery("character"));
    // The dropped event is not left pending for a later retry.
    CHECK(coalescer.PendingCount() == 0);
    CHECK(queue.EventLaneSize() == dovahlink::security::kReservedEventSlots);
}

TEST_CASE("NeedsRecovery is false for an area that has never needed recovery",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);
    CHECK_FALSE(coalescer.NeedsRecovery("character"));
}

TEST_CASE("PublishEvent is a no-op for a state area that needs recovery",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);
    coalescer.PublishEvent("character", "dropped-event");
    coalescer.Flush();
    REQUIRE(coalescer.NeedsRecovery("character"));

    // Free up space, but the area still needs recovery, so this publish must not stage.
    REQUIRE(queue.DequeueEvent().has_value());
    CHECK_FALSE(coalescer.PublishEvent("character", "should-not-be-staged"));
    CHECK(coalescer.PendingCount() == 0);
}

TEST_CASE("PublishEvent returns true once staged and false when dropped during recovery",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);

    CHECK(coalescer.PublishEvent("character", "event-1"));  // stages fine; not yet flushed.
    coalescer.Flush();
    REQUIRE(coalescer.NeedsRecovery("character"));
    CHECK_FALSE(coalescer.PublishEvent("character", "dropped"));
}

TEST_CASE("MarkRecovered clears the recovery mark and allows publishing again",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);
    coalescer.PublishEvent("character", "dropped-event");
    coalescer.Flush();
    REQUIRE(coalescer.NeedsRecovery("character"));

    coalescer.MarkRecovered("character");
    CHECK_FALSE(coalescer.NeedsRecovery("character"));

    // Drain every filler first: DequeueEvent is FIFO, so freeing only one
    // slot and publishing "fresh-event" would enqueue it behind the
    // remaining 111 fillers, and the very next dequeue would still return
    // one of those, not "fresh-event" -- caught by actually running this
    // test, which a single freed slot did not.
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.DequeueEvent().has_value());
    }
    coalescer.PublishEvent("character", "fresh-event");
    coalescer.Flush();
    CHECK(queue.DequeueEvent() == "fresh-event");
}

TEST_CASE("Flush handles a mix of successful and failed areas in the same call",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    // Leave room for exactly one more event before the lane is full.
    for (std::size_t i = 0; i + 1 < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "character-event");
    coalescer.PublishEvent("location", "location-event");
    REQUIRE(coalescer.PendingCount() == 2);

    coalescer.Flush();

    // Exactly one of the two areas fit in the single remaining slot; the other needed recovery.
    // unordered_map iteration order is unspecified, so check the invariant rather than which one.
    bool characterNeedsRecovery = coalescer.NeedsRecovery("character");
    bool locationNeedsRecovery = coalescer.NeedsRecovery("location");
    CHECK(characterNeedsRecovery != locationNeedsRecovery);
    CHECK(coalescer.PendingCount() == 0);
    CHECK(queue.EventLaneSize() == dovahlink::security::kReservedEventSlots);
}

TEST_CASE("an attempted replacement requires recovery again after an area recovers",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);
    coalescer.PublishEvent("character", "dropped-event");
    coalescer.Flush();
    REQUIRE(coalescer.NeedsRecovery("character"));
    coalescer.MarkRecovered("character");

    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.DequeueEvent().has_value());  // fully drain, so the next flush fits.
    }

    CHECK(coalescer.PublishEvent("character", "first-assigned-event"));
    CHECK_FALSE(coalescer.PublishEvent("character", "replacement-assigned-event"));
    CHECK(coalescer.PendingCount() == 0);
    CHECK(coalescer.NeedsRecovery("character"));

    coalescer.Flush();
    CHECK_FALSE(queue.DequeueEvent().has_value());
}

TEST_CASE("recovery on one area does not affect an unrelated area", "[application][event_coalescer]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("filler-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    EventCoalescer coalescer(queue);
    coalescer.PublishEvent("character", "dropped-event");
    coalescer.Flush();
    REQUIRE(coalescer.NeedsRecovery("character"));

    CHECK_FALSE(coalescer.NeedsRecovery("location"));
    // A different, unrelated area may still be staged even though "character" cannot be.
    CHECK(coalescer.PublishEvent("location", "location-event"));
    CHECK(coalescer.PendingCount() == 1);
}

TEST_CASE("Flush preserves one area's recovery state while delivering another area",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    REQUIRE(coalescer.PublishEvent("character", "character-revision-2"));
    REQUIRE_FALSE(coalescer.PublishEvent("character", "character-revision-3"));
    REQUIRE(coalescer.NeedsRecovery("character"));
    REQUIRE(coalescer.PublishEvent("location", "location-revision-2"));

    coalescer.Flush();

    CHECK(coalescer.NeedsRecovery("character"));
    CHECK(coalescer.PendingCount() == 0);
    CHECK(queue.DequeueEvent() == "location-revision-2");
}

TEST_CASE("MarkRecovered does not discard an event that is still pending",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    REQUIRE(coalescer.PublishEvent("character", "pending-event"));
    coalescer.MarkRecovered("character");
    CHECK(coalescer.PendingCount() == 1);

    coalescer.Flush();
    CHECK(queue.DequeueEvent() == "pending-event");
}

TEST_CASE("Flush only affects areas with a staged event", "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    coalescer.Flush();  // nothing staged; must not fail or affect the queue.
    CHECK(queue.EventLaneSize() == 0);
}

TEST_CASE("a full drain-and-refill cycle behaves correctly across multiple Flush calls",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "event-1");
    coalescer.Flush();
    CHECK(queue.DequeueEvent() == "event-1");

    coalescer.PublishEvent("character", "event-2");
    coalescer.Flush();
    CHECK(queue.DequeueEvent() == "event-2");
}
