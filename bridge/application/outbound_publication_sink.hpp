#pragma once

#include "protocol/envelope.hpp"
#include "shared/enums.hpp"
#include "transport/websocket_session.hpp"

#include <cstddef>
#include <cstdint>
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
    ///  coalesced with another submission for the same state area. Discarded
    ///  silently, rather than delivered, when its own revision is at or
    ///  below a revision already established for this state area by
    ///  `PublishRecoverySnapshot`.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param envelope Built `state_event` envelope.
    virtual void PublishEvent(std::string stateArea,
                              protocol::Envelope envelope) = 0;

    ///  Submits an initial or recovery Snapshot envelope through the
    ///  reserved control/recovery lane rather than the bounded data lanes,
    ///  and establishes this state area's recovery barrier at `revision`:
    ///  any `PublishEvent` for this area, already queued or submitted later,
    ///  whose own revision is at or below `revision` is discarded as
    ///  superseded rather than delivered after this snapshot. Unlike
    ///  `PublishSnapshot`, submissions are delivered in order rather than
    ///  replacing a pending one -- initial/recovery snapshots are rare,
    ///  one-off resynchronization events, not the frequent latest-value
    ///  updates `PublishSnapshot` models.
    ///  @param stateArea Canonical state-area identifier.
    ///  @param envelope Built `state_snapshot` envelope.
    ///  @param revision The authoritative baseline this snapshot establishes.
    virtual void PublishRecoverySnapshot(std::string stateArea,
                                         protocol::Envelope envelope,
                                         std::int64_t revision) = 0;

    ///  Submits an acknowledgement, error, or other recovery/control message
    ///  through the same reserved control/recovery lane as
    ///  `PublishRecoverySnapshot`, sharing its capacity and priority over the
    ///  bounded data lanes. Carries no state area of its own and never
    ///  establishes or consults a recovery barrier.
    ///  @param envelope Built control-category envelope.
    virtual void PublishControl(protocol::Envelope envelope) = 0;
};

///  Bounded outbound organization consuming `IStatePublisher`'s typed
///  publications toward one authenticated session's live socket. Enforces
///  the approved Normal/Heavy data-lane capacity and the queue-wide
///  encoded-byte budget (`ai/context/protocol/security.md`'s "Input
///  limits"), replaces a keyed Snapshot's pending entry in place rather than
///  growing the queue, and disconnects the session outright when a reliable
///  Event or a reserved-lane message cannot be admitted rather than dropping
///  it. A reserved control/recovery lane, unaffected by data-lane pressure or
///  the data byte budget, carries initial and recovery Snapshots and control
///  messages (acknowledgements, errors) ahead of ordinary data-lane traffic;
///  each recovery Snapshot establishes its state area's recovery barrier,
///  which supersedes an Event at or below that revision rather than
///  delivering it after the snapshot. One instance is scoped to
///  one authenticated session's socket and its own `sessionId`; a reconnect
///  constructs a fresh instance rather than reusing this one, so a previous
///  session's undelivered traffic and barriers are never carried into a new
///  one.
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

    ///  @copydoc IOutboundPublicationSink::PublishRecoverySnapshot
    void PublishRecoverySnapshot(std::string stateArea,
                                 protocol::Envelope envelope,
                                 std::int64_t revision) override;

    ///  @copydoc IOutboundPublicationSink::PublishControl
    void PublishControl(protocol::Envelope envelope) override;

  private:
    ///  One data-lane entry awaiting delivery: a keyed, replaceable Snapshot
    ///  slot, or an ordered, non-coalesced Event entry.
    struct PendingEntry {
        ///  Whether this is a keyed Snapshot slot (`true`) or an ordered
        ///  Event entry (`false`).
        bool isSnapshot;
        ///  State area this entry belongs to.
        std::string stateArea;
        ///  This Event's own revision, used to compare against a later
        ///  recovery barrier for this state area. Unset for a Snapshot
        ///  entry, and for an Event entry whose payload could not be decoded
        ///  back into its revision.
        std::optional<std::int64_t> revision;
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
    void ApplyAdmitNewLocked(PendingEntry entry);

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

    ///  Admits an already-encoded reserved-lane message when the 16-slot
    ///  capacity allows it, shared by `PublishRecoverySnapshot` and
    ///  `PublishControl`. Otherwise stops the queue and disconnects, per
    ///  `ai/context/protocol/security.md`'s "If reserved control/recovery
    ///  capacity is full, the client is marked unavailable and the
    ///  connection is closed." Caller must hold `mutex_`.
    void AdmitReservedOrDisconnectLocked(std::string encoded);

    ///  Removes queued, not-yet-in-flight Event entries for `stateArea`
    ///  whose own revision is at or below `revision`: superseded by the
    ///  recovery snapshot establishing that revision as the new baseline.
    ///  Never removes an entry already handed to `Send` -- its bytes are
    ///  already on their way to the peer and cannot be recalled. Caller must
    ///  hold `mutex_`.
    void SupersedeQueuedEventsLocked(const std::string& stateArea,
                                     std::int64_t revision);

    ///  Starts draining the next pending entry when nothing is currently in
    ///  flight, preferring the reserved control/recovery lane over the data
    ///  lanes. Caller must hold `mutex_`.
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

    ///  Reserved-lane entries (initial/recovery snapshots) in delivery
    ///  order, already fully encoded -- this lane carries no per-entry
    ///  bookkeeping beyond its own slot count.
    std::list<std::string> reservedPending_;

    ///  Data-lane entries in overall delivery order.
    std::list<PendingEntry> dataPending_;

    ///  Maps a Snapshot state area to its one pending slot in `dataPending_`.
    std::unordered_map<std::string, std::list<PendingEntry>::iterator>
        snapshotSlots_;

    ///  At most one retained Snapshot value per state area, awaiting
    ///  capacity.
    std::unordered_map<std::string, DirtyEntry> dirtyMarkers_;

    ///  Each state area's recovery barrier: the revision established by its
    ///  most recent `PublishRecoverySnapshot`, below or at which a
    ///  `PublishEvent` for that area is superseded.
    std::unordered_map<std::string, std::int64_t> barriers_;

    ///  Normal-lane slots currently occupied.
    std::size_t normalSlotsUsed_{0};

    ///  Heavy-lane slots currently occupied.
    std::size_t heavySlotsUsed_{0};

    ///  Total encoded bytes currently occupying the data lanes.
    std::size_t totalBytesUsed_{0};

    ///  Reserved-lane slots currently occupied.
    std::size_t reservedSlotsUsed_{0};

    ///  Whether a `Send` is currently outstanding.
    bool sendInFlight_{false};

    ///  Whether the outstanding `Send`, if any, is draining `reservedPending_`
    ///  rather than `dataPending_`.
    bool sendingReserved_{false};

    ///  Set once a reliable Event or reserved-lane message could not be
    ///  admitted, or a `Send` reported failure; further publications become
    ///  no-ops.
    bool stopped_{false};
};

} //  namespace dovahlink::application
