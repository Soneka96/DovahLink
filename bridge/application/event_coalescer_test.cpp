#include "application/event_coalescer.hpp"

#include "application/outbound_queue.hpp"
#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <string>

using dovahlink::application::EnqueueResult;
using dovahlink::application::EventCoalescer;
using dovahlink::application::OutboundQueue;

TEST_CASE("Flush delivers a published event into the queue's event lane", "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "event-1");
    coalescer.Flush();

    CHECK(queue.EventLaneSize() == 1);
    CHECK(queue.DequeueEvent() == "event-1");
}

TEST_CASE("publishing twice for the same area before Flush coalesces to the latest",
          "[application][event_coalescer]") {
    OutboundQueue queue;
    EventCoalescer coalescer(queue);

    coalescer.PublishEvent("character", "stale-event");
    coalescer.PublishEvent("character", "latest-event");
    CHECK(coalescer.PendingCount() == 1);

    coalescer.Flush();

    CHECK(queue.EventLaneSize() == 1);
    CHECK(queue.DequeueEvent() == "latest-event");
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

    REQUIRE(queue.DequeueEvent().has_value());  // free a slot
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

TEST_CASE("coalescing still works normally for an area after it recovers",
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

    coalescer.PublishEvent("character", "stale-event");
    coalescer.PublishEvent("character", "latest-event");
    CHECK(coalescer.PendingCount() == 1);

    coalescer.Flush();
    CHECK(queue.DequeueEvent() == "latest-event");
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
