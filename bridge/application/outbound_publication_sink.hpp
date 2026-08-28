#pragma once

#include "application/publication_diagnostics.hpp"
#include "protocol/envelope.hpp"
#include "shared/enums.hpp"
#include "transport/websocket_session.hpp"

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <list>
#include <mutex>
#include <optional>
#include <string>
#include <unordered_map>

namespace dovahlink::application {

class SessionPublicationFactory;

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
///  one. A queue created by `SessionPublicationFactory` detaches its own router
///  binding before destruction, and an older queue can never detach a newer
///  replacement binding.
class BoundedOutboundQueue final : public IOutboundPublicationSink {
  public:
    ///  Binds the queue to the live session socket it drains into, the
    ///  diagnostics sink observing its behavior, and the session identity
    ///  stamped onto every delivered envelope.
    ///  @param socket Live session socket; must outlive this queue.
    ///  @param diagnostics Diagnostics sink; must outlive this queue.
    ///  @param sessionId Identity of the authenticated session this queue
    ///  serves.
    BoundedOutboundQueue(transport::ISocket& socket,
                         IPublicationDiagnostics& diagnostics,
                         std::string sessionId);

    ///  Prevents late socket completions from accessing this queue after its
    ///  owning session has destroyed it, and releases its router binding first
    ///  when the queue was created by `SessionPublicationFactory`.
    ~BoundedOutboundQueue() noexcept;

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
    friend class SessionPublicationFactory;

    ///  Installs the teardown callback used to release this queue's router
    ///  binding before the queue becomes invalid.
    ///  @param callback No-throw callback that detaches this queue's binding.
    void SetDetachCallback(std::function<void()> callback);

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
        ///  Instant this entry was last admitted or replaced, used to report
        ///  `IPublicationDiagnostics::RecordDequeueLatency` on delivery.
        std::chrono::steady_clock::time_point enqueuedAt;
    };

    ///  A Snapshot value that could not be admitted or replaced when
    ///  submitted, retained so it can be retried once capacity frees.
    struct DirtyEntry {
        ///  Already-encoded, session-stamped wire text awaiting capacity.
        ///  Empty when the publication itself exceeds the queue-wide byte
        ///  budget; the authoritative store remains the source for a later
        ///  bounded retry.
        std::optional<std::string> encoded;
        ///  Encoded size used for diagnostics and classification decisions.
        std::size_t encodedBytes;
        ///  Data lane this value would occupy once admitted.
        QueueClass queueClass;
    };

    ///  A reserved-lane entry awaiting delivery.
    struct ReservedEntry {
        ///  Already-encoded, session-stamped wire text.
        std::string encoded;
        ///  Instant this entry was admitted, used to report
        ///  `IPublicationDiagnostics::RecordDequeueLatency` on delivery.
        std::chrono::steady_clock::time_point enqueuedAt;
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
    ///  @return `true` when the caller must request socket shutdown after
    ///  releasing `mutex_`.
    [[nodiscard]] bool AdmitReservedOrDisconnectLocked(std::string encoded);

    ///  Removes queued, not-yet-in-flight Event entries for `stateArea`
    ///  whose own revision is at or below `revision`: superseded by the
    ///  recovery snapshot establishing that revision as the new baseline.
    ///  Never removes an entry already handed to `Send` -- its bytes are
    ///  already on their way to the peer and cannot be recalled. Caller must
    ///  hold `mutex_`.
    ///  @return Number of entries removed.
    [[nodiscard]] std::size_t
    SupersedeQueuedEventsLocked(const std::string& stateArea,
                                std::int64_t revision);

    ///  Starts draining the next pending entry when nothing is currently in
    ///  flight, preferring the reserved control/recovery lane over the data
    ///  lanes. Caller must hold `mutex_`. The returned message is handed to
    ///  the socket only after the caller releases `mutex_`.
    [[nodiscard]] std::optional<std::string> MaybeStartSendLocked();

    ///  Hands one already-selected message to the socket without holding the
    ///  queue mutex. The completion is guarded by `completionState_`.
    void DispatchSend(std::string encoded) noexcept;

    ///  Handles one `Send` completion: releases the delivered entry's
    ///  capacity, stops the queue and disconnects on failure, and otherwise
    ///  promotes a retained dirty value and continues draining. The callback
    ///  boundary is noexcept so transport threads cannot observe an exception.
    void OnSendComplete(bool ok) noexcept;

    ///  Lifetime gate shared by queued transport completions. It is nested
    ///  because it exists only to protect this queue's private state from a
    ///  completion that may outlive the queue object itself.
    struct CompletionState {
        ///  Synchronizes owner detachment and callback admission.
        std::mutex mutex;
        ///  Wakes queue destruction after callbacks already using the owner
        ///  have returned.
        std::condition_variable changed;
        ///  Queue owner while destruction has not begun.
        BoundedOutboundQueue* owner = nullptr;
        ///  Prevents new callbacks from entering a destroying queue.
        bool destroying = false;
        ///  Number of callbacks currently executing queue code.
        std::size_t callbacksInFlight = 0;
    };

    ///  Live session socket this queue drains into.
    transport::ISocket& socket_;

    ///  Diagnostics sink observing this queue's behavior.
    IPublicationDiagnostics& diagnostics_;

    ///  Identity stamped onto every delivered envelope.
    std::string sessionId_;

    ///  Detaches this queue's router binding before destruction.
    std::function<void()> detachCallback_;

    ///  Serializes all queue state.
    std::mutex mutex_;

    ///  Guards the queue owner pointer captured by asynchronous completions.
    std::shared_ptr<CompletionState> completionState_;

    ///  Reserved-lane entries (initial/recovery snapshots) in delivery
    ///  order, already fully encoded.
    std::list<ReservedEntry> reservedPending_;

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
