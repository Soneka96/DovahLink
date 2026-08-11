#include "application/outbound_queue.hpp"

#include "security/limits.hpp"

#include <catch2/catch_test_macros.hpp>

#include <cstddef>
#include <string>

using dovahlink::application::EnqueueResult;
using dovahlink::application::OutboundQueue;

TEST_CASE("EnqueueControl accepts a message under capacity", "[application][outbound_queue]") {
    OutboundQueue queue;
    CHECK(queue.EnqueueControl("hello_ack") == EnqueueResult::kEnqueued);
    CHECK(queue.ControlLaneSize() == 1);
}

TEST_CASE("EnqueueEvent accepts a message under capacity", "[application][outbound_queue]") {
    OutboundQueue queue;
    CHECK(queue.EnqueueEvent("state_event") == EnqueueResult::kEnqueued);
    CHECK(queue.EventLaneSize() == 1);
}

TEST_CASE("EnqueueControl rejects a message once the control lane is full",
          "[application][outbound_queue]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        REQUIRE(queue.EnqueueControl("message-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    CHECK(queue.EnqueueControl("one-too-many") == EnqueueResult::kControlLaneFull);
    CHECK(queue.ControlLaneSize() == dovahlink::security::kReservedControlRecoverySlots);
}

TEST_CASE("EnqueueEvent rejects a message once the event lane is full", "[application][outbound_queue]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("message-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    CHECK(queue.EnqueueEvent("one-too-many") == EnqueueResult::kEventLaneFull);
    CHECK(queue.EventLaneSize() == dovahlink::security::kReservedEventSlots);
}

TEST_CASE("EnqueueControl keeps rejecting while the control lane stays full",
          "[application][outbound_queue]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        REQUIRE(queue.EnqueueControl("message-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    CHECK(queue.EnqueueControl("first-overflow") == EnqueueResult::kControlLaneFull);
    CHECK(queue.EnqueueControl("second-overflow") == EnqueueResult::kControlLaneFull);
    CHECK(queue.ControlLaneSize() == dovahlink::security::kReservedControlRecoverySlots);
}

TEST_CASE("an empty message string is a valid, enqueueable message", "[application][outbound_queue]") {
    OutboundQueue queue;
    REQUIRE(queue.EnqueueControl("") == EnqueueResult::kEnqueued);
    CHECK(queue.DequeueControl() == "");
}

TEST_CASE("FIFO order is preserved across an interleaved dequeue and enqueue",
          "[application][outbound_queue]") {
    OutboundQueue queue;
    REQUIRE(queue.EnqueueControl("first") == EnqueueResult::kEnqueued);
    REQUIRE(queue.EnqueueControl("second") == EnqueueResult::kEnqueued);

    CHECK(queue.DequeueControl() == "first");
    REQUIRE(queue.EnqueueControl("third") == EnqueueResult::kEnqueued);

    CHECK(queue.DequeueControl() == "second");
    CHECK(queue.DequeueControl() == "third");
}

TEST_CASE("the control and event lanes never cross-contaminate", "[application][outbound_queue]") {
    OutboundQueue queue;
    REQUIRE(queue.EnqueueControl("control-message") == EnqueueResult::kEnqueued);
    REQUIRE(queue.EnqueueEvent("event-message") == EnqueueResult::kEnqueued);

    CHECK(queue.DequeueEvent() == "event-message");
    CHECK_FALSE(queue.DequeueEvent().has_value());
    // The control message is still there, untouched by draining the event lane.
    CHECK(queue.DequeueControl() == "control-message");
}

TEST_CASE("lane sizes are zero after being fully drained", "[application][outbound_queue]") {
    OutboundQueue queue;
    REQUIRE(queue.EnqueueControl("a") == EnqueueResult::kEnqueued);
    REQUIRE(queue.EnqueueEvent("b") == EnqueueResult::kEnqueued);

    REQUIRE(queue.DequeueControl().has_value());
    REQUIRE(queue.DequeueEvent().has_value());

    CHECK(queue.ControlLaneSize() == 0);
    CHECK(queue.EventLaneSize() == 0);
}

TEST_CASE("the control and event lanes have independent capacity", "[application][outbound_queue]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        REQUIRE(queue.EnqueueControl("control-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    REQUIRE(queue.EnqueueControl("overflow") == EnqueueResult::kControlLaneFull);

    // A full control lane does not affect the event lane's own capacity.
    CHECK(queue.EnqueueEvent("event-1") == EnqueueResult::kEnqueued);
}

TEST_CASE("DequeueControl returns messages in FIFO order", "[application][outbound_queue]") {
    OutboundQueue queue;
    REQUIRE(queue.EnqueueControl("first") == EnqueueResult::kEnqueued);
    REQUIRE(queue.EnqueueControl("second") == EnqueueResult::kEnqueued);

    CHECK(queue.DequeueControl() == "first");
    CHECK(queue.DequeueControl() == "second");
}

TEST_CASE("DequeueEvent returns messages in FIFO order", "[application][outbound_queue]") {
    OutboundQueue queue;
    REQUIRE(queue.EnqueueEvent("first") == EnqueueResult::kEnqueued);
    REQUIRE(queue.EnqueueEvent("second") == EnqueueResult::kEnqueued);

    CHECK(queue.DequeueEvent() == "first");
    CHECK(queue.DequeueEvent() == "second");
}

TEST_CASE("DequeueControl returns nullopt when the control lane is empty",
          "[application][outbound_queue]") {
    OutboundQueue queue;
    CHECK_FALSE(queue.DequeueControl().has_value());
}

TEST_CASE("DequeueEvent returns nullopt when the event lane is empty", "[application][outbound_queue]") {
    OutboundQueue queue;
    CHECK_FALSE(queue.DequeueEvent().has_value());
}

TEST_CASE("dequeuing from a full control lane frees a slot for a new message",
          "[application][outbound_queue]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedControlRecoverySlots; ++i) {
        REQUIRE(queue.EnqueueControl("message-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    REQUIRE(queue.EnqueueControl("overflow") == EnqueueResult::kControlLaneFull);

    REQUIRE(queue.DequeueControl().has_value());
    CHECK(queue.EnqueueControl("now-fits") == EnqueueResult::kEnqueued);
}

TEST_CASE("dequeuing from a full event lane frees a slot for a new message",
          "[application][outbound_queue]") {
    OutboundQueue queue;
    for (std::size_t i = 0; i < dovahlink::security::kReservedEventSlots; ++i) {
        REQUIRE(queue.EnqueueEvent("message-" + std::to_string(i)) == EnqueueResult::kEnqueued);
    }
    REQUIRE(queue.EnqueueEvent("overflow") == EnqueueResult::kEventLaneFull);

    REQUIRE(queue.DequeueEvent().has_value());
    CHECK(queue.EnqueueEvent("now-fits") == EnqueueResult::kEnqueued);
}
