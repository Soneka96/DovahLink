#pragma once

#include "protocol/envelope.hpp"
#include "shared/enums.hpp"
#include "transport/websocket_session.hpp"

#include <cstddef>
#include <list>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>

namespace dovahlink::application {

///  Receives typed publications from `IStatePublisher` toward the bounded
///  outbound organization. It is the handoff seam between publication-mode
///  dispatch and the bounded transport organization; `IStatePublisher` can
///  therefore remain complete and testable while that organization is wired
///  behind this contract.
class IOutboundPublicationSink {
  public:
    ///  Allows destruction through the interface.
    virtual ~IOutboundPublicationSink() = default;

    ///  Submits a replaceable Snapshot-mode envelope, replacing any pending
    ///  envelope previously submitted for the same state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param envelope Built `state_snapshot` envelope.
    virtual void PublishSnapshot(std::string stateArea,
                                 protocol::Envelope envelope) = 0;

    ///  Submits a reliable Event-mode envelope, appended in order and never
    ///  coalesced with another submission for the same state area.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param envelope Built `state_event` envelope.
    virtual void PublishEvent(std::string stateArea,
                              protocol::Envelope envelope) = 0;
};

///  Bounded outbound organization consuming `IStatePublisher`'s typed
///  publications toward one authenticated session's live socket. Enforces
///  the approved Normal/Heavy data-lane capacity and the queue-wide
///  encoded-byte budget (`ai/context/protocol/security.md`'s "Input
///  limits"), replaces a keyed Snapshot's pending entry in place rather than
///  growing the queue, and disconnects the session outright when a reliable
///  Event cannot be admitted rather than dropping it. One instance is scoped
///  to one authenticated session's socket and its own `sessionId`; a
///  reconnect constructs a fresh instance rather than reusing this one, so a
///  previous session's undelivered traffic is never carried into a new one.
class BoundedOutboundQueue final : public IOutboundPublicationSink {
  public:
    ///  Binds the queue to the live session socket it drains into and the
    ///  session identity stamped onto every delivered envelope.
    ///  @param socket Live session socket; must outlive this queue.
    ///  @param sessionId Identity of the authenticated session this queue
    ///  serves.
    BoundedOutboundQueue(transport::ISocket& socket, std::string sessionId);

    ///  @copydoc IOutboundPublicationSink::PublishSnapshot
    void PublishSnapshot(std::string stateArea,
                         protocol::Envelope envelope) override;

    ///  @copydoc IOutboundPublicationSink::PublishEvent
    void PublishEvent(std::string stateArea,
                      protocol::Envelope envelope) override;

  private:
    ///  One data-lane entry awaiting delivery: a keyed Snapshot slot when
    ///  `stateArea` has a value, or an ordered Event entry otherwise.
    struct PendingEntry {
        ///  Snapshot state area this entry is keyed by, or no value for an
        ///  Event entry.
        std::optional<std::string> stateArea;
        ///  Already-encoded, session-stamped wire text.
        std::string encoded;
        ///  Data lane this entry currently occupies.
        QueueClass queueClass;
    };

    ///  A Snapshot value that could not be admitted or replaced when
    ///  submitted, retained so it can be retried once capacity frees.
    struct DirtyEntry {
        ///  Already-encoded, session-stamped wire text awaiting capacity.
        std::string encoded;
        ///  Data lane this value would occupy once admitted.
        QueueClass queueClass;
    };

    ///  Classifies an already-encoded publication by size.
    [[nodiscard]] static QueueClass Classify(const std::string& encoded);

    ///  Reports whether a brand-new data-lane entry of this class and size
    ///  fits within both its lane's slot capacity and the queue's byte
    ///  budget. Caller must hold `mutex_`.
    [[nodiscard]] bool CanAdmitNewLocked(QueueClass queueClass,
                                         std::size_t bytes) const;

    ///  Reports whether replacing an already-admitted slot's value fits,
    ///  accounting for the byte delta and, when the class changes, the
    ///  destination lane's own capacity. Caller must hold `mutex_`.
    [[nodiscard]] bool CanReplaceLocked(QueueClass oldClass,
                                        std::size_t oldBytes,
                                        QueueClass newClass,
                                        std::size_t newBytes) const;

    ///  Admits a brand-new data-lane entry, already known to fit. Caller
    ///  must hold `mutex_`.
    void ApplyAdmitNewLocked(std::string stateArea, QueueClass queueClass,
                             std::string encoded);

    ///  Replaces an already-admitted Snapshot slot's value in place, already
    ///  known to fit. Caller must hold `mutex_`.
    void ApplyReplaceLocked(std::list<PendingEntry>::iterator slot,
                            QueueClass newClass, std::string encoded);

    ///  Increments the slot counter for one data lane. Caller must hold
    ///  `mutex_`.
    void IncrementLaneLocked(QueueClass queueClass);

    ///  Decrements the slot counter for one data lane. Caller must hold
    ///  `mutex_`.
    void DecrementLaneLocked(QueueClass queueClass);

    ///  Admits at most one retained dirty Snapshot value that now fits,
    ///  bounding retry to one attempt per freed slot rather than an
    ///  unbounded catch-up burst. Caller must hold `mutex_`.
    void TryPromoteOneDirtyLocked();

    ///  Starts draining the next pending entry when nothing is currently in
    ///  flight. Caller must hold `mutex_`.
    void MaybeStartSendLocked();

    ///  Handles one `Send` completion: releases the delivered entry's
    ///  capacity, stops the queue and disconnects on failure, and otherwise
    ///  promotes a retained dirty value and continues draining.
    void OnSendComplete(bool ok);

    ///  Live session socket this queue drains into.
    transport::ISocket& socket_;

    ///  Identity stamped onto every delivered envelope.
    std::string sessionId_;

    ///  Serializes all queue state.
    std::mutex mutex_;

    ///  Data-lane entries in overall delivery order.
    std::list<PendingEntry> pending_;

    ///  Maps a Snapshot state area to its one pending slot in `pending_`.
    std::unordered_map<std::string, std::list<PendingEntry>::iterator>
        snapshotSlots_;

    ///  At most one retained Snapshot value per state area, awaiting
    ///  capacity.
    std::unordered_map<std::string, DirtyEntry> dirtyMarkers_;

    ///  Normal-lane slots currently occupied.
    std::size_t normalSlotsUsed_{0};

    ///  Heavy-lane slots currently occupied.
    std::size_t heavySlotsUsed_{0};

    ///  Total encoded bytes currently occupying the data lanes.
    std::size_t totalBytesUsed_{0};

    ///  Whether a `Send` is currently outstanding.
    bool sendInFlight_{false};

    ///  Set once a reliable Event could not be admitted or a `Send` reported
    ///  failure; further publications become no-ops.
    bool stopped_{false};
};

} //  namespace dovahlink::application
