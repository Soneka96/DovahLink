#include "application/outbound_publication_sink.hpp"

#include "protocol/state_event_payload.hpp"
#include "security/constants.hpp"

#include <utility>

namespace dovahlink::application {

BoundedOutboundQueue::BoundedOutboundQueue(transport::ISocket& socket,
                                           std::string sessionId)
    : socket_(socket), sessionId_(std::move(sessionId)) {}

QueueClass BoundedOutboundQueue::Classify(const std::string& encoded) {
    return encoded.size() >= security::kHeavyPublicationThresholdBytes
               ? QueueClass::kHeavy
               : QueueClass::kNormal;
}

bool BoundedOutboundQueue::CanAdmitNewLocked(QueueClass queueClass,
                                             std::size_t bytes) const {
    std::size_t laneUsed =
        queueClass == QueueClass::kNormal ? normalSlotsUsed_ : heavySlotsUsed_;
    std::size_t laneCapacity = queueClass == QueueClass::kNormal
                                   ? security::kNormalDataSlots
                                   : security::kHeavyDataSlots;
    return laneUsed < laneCapacity &&
           totalBytesUsed_ + bytes <= security::kOutboundQueueByteBudget;
}

bool BoundedOutboundQueue::CanReplaceLocked(QueueClass oldClass,
                                            std::size_t oldBytes,
                                            QueueClass newClass,
                                            std::size_t newBytes) const {
    if (totalBytesUsed_ - oldBytes + newBytes >
        security::kOutboundQueueByteBudget) {
        return false;
    }
    if (newClass == oldClass) {
        return true;
    }
    std::size_t newLaneUsed =
        newClass == QueueClass::kNormal ? normalSlotsUsed_ : heavySlotsUsed_;
    std::size_t newLaneCapacity = newClass == QueueClass::kNormal
                                      ? security::kNormalDataSlots
                                      : security::kHeavyDataSlots;
    return newLaneUsed < newLaneCapacity;
}

void BoundedOutboundQueue::ApplyAdmitNewLocked(PendingEntry entry) {
    IncrementLaneLocked(entry.queueClass);
    totalBytesUsed_ += entry.encoded.size();
    bool isSnapshot = entry.isSnapshot;
    std::string stateArea = entry.stateArea;
    dataPending_.push_back(std::move(entry));
    if (isSnapshot) {
        snapshotSlots_[std::move(stateArea)] = std::prev(dataPending_.end());
    }
}

void BoundedOutboundQueue::ApplyReplaceLocked(
    std::list<PendingEntry>::iterator slot, QueueClass newClass,
    std::string encoded) {
    QueueClass oldClass = slot->queueClass;
    if (newClass != oldClass) {
        DecrementLaneLocked(oldClass);
        IncrementLaneLocked(newClass);
    }
    totalBytesUsed_ = totalBytesUsed_ - slot->encoded.size() + encoded.size();
    slot->encoded = std::move(encoded);
    slot->queueClass = newClass;
}

void BoundedOutboundQueue::IncrementLaneLocked(QueueClass queueClass) {
    if (queueClass == QueueClass::kNormal) {
        ++normalSlotsUsed_;
    } else {
        ++heavySlotsUsed_;
    }
}

void BoundedOutboundQueue::DecrementLaneLocked(QueueClass queueClass) {
    if (queueClass == QueueClass::kNormal) {
        --normalSlotsUsed_;
    } else {
        --heavySlotsUsed_;
    }
}

void BoundedOutboundQueue::TryPromoteOneDirtyLocked() {
    for (auto it = dirtyMarkers_.begin(); it != dirtyMarkers_.end(); ++it) {
        const std::string& stateArea = it->first;
        QueueClass queueClass = it->second.queueClass;
        std::size_t bytes = it->second.encoded.size();
        auto slotIt = snapshotSlots_.find(stateArea);
        bool fits = slotIt != snapshotSlots_.end()
                        ? CanReplaceLocked(slotIt->second->queueClass,
                                           slotIt->second->encoded.size(),
                                           queueClass, bytes)
                        : CanAdmitNewLocked(queueClass, bytes);
        if (!fits) {
            continue;
        }
        std::string area = stateArea;
        std::string encoded = std::move(it->second.encoded);
        dirtyMarkers_.erase(it);
        if (slotIt != snapshotSlots_.end()) {
            ApplyReplaceLocked(slotIt->second, queueClass, std::move(encoded));
        } else {
            ApplyAdmitNewLocked(PendingEntry{
                .isSnapshot = true,
                .stateArea = std::move(area),
                .revision = std::nullopt,
                .encoded = std::move(encoded),
                .queueClass = queueClass,
            });
        }
        return;
    }
}

void BoundedOutboundQueue::SupersedeQueuedEventsLocked(
    const std::string& stateArea, std::int64_t revision) {
    auto it = dataPending_.begin();
    if (sendInFlight_ && !sendingReserved_ && it != dataPending_.end()) {
        //  The front entry is already in flight; its bytes cannot be
        //  recalled, matching ISocket::Send's own "no take-backs" contract.
        ++it;
    }
    while (it != dataPending_.end()) {
        if (!it->isSnapshot && it->stateArea == stateArea &&
            it->revision.has_value() && *it->revision <= revision) {
            totalBytesUsed_ -= it->encoded.size();
            DecrementLaneLocked(it->queueClass);
            it = dataPending_.erase(it);
        } else {
            ++it;
        }
    }
}

void BoundedOutboundQueue::MaybeStartSendLocked() {
    if (sendInFlight_) {
        return;
    }
    if (!reservedPending_.empty()) {
        sendInFlight_ = true;
        sendingReserved_ = true;
        std::string next = reservedPending_.front();
        socket_.Send(std::move(next), [this](bool ok) { OnSendComplete(ok); });
        return;
    }
    if (!dataPending_.empty()) {
        sendInFlight_ = true;
        sendingReserved_ = false;
        std::string next = dataPending_.front().encoded;
        socket_.Send(std::move(next), [this](bool ok) { OnSendComplete(ok); });
    }
}

void BoundedOutboundQueue::OnSendComplete(bool ok) {
    std::lock_guard<std::mutex> lock(mutex_);
    sendInFlight_ = false;
    if (sendingReserved_) {
        if (!reservedPending_.empty()) {
            reservedPending_.pop_front();
            --reservedSlotsUsed_;
        }
    } else if (!dataPending_.empty()) {
        PendingEntry finished = std::move(dataPending_.front());
        dataPending_.pop_front();
        if (finished.isSnapshot) {
            snapshotSlots_.erase(finished.stateArea);
        }
        DecrementLaneLocked(finished.queueClass);
        totalBytesUsed_ -= finished.encoded.size();
    }

    if (!ok) {
        //  Send's own contract leaves closing the connection to its caller
        //  on failure; a broken socket cannot keep delivering the rest of
        //  this queue, so the queue stops draining and ends the session.
        stopped_ = true;
        socket_.Shutdown();
        return;
    }

    TryPromoteOneDirtyLocked();
    MaybeStartSendLocked();
}

void BoundedOutboundQueue::PublishSnapshot(std::string stateArea,
                                           protocol::Envelope envelope) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (stopped_) {
        return;
    }
    envelope.sessionId = sessionId_;
    std::string encoded = protocol::EncodeEnvelope(envelope);
    QueueClass queueClass = Classify(encoded);
    std::size_t bytes = encoded.size();

    auto slotIt = snapshotSlots_.find(stateArea);
    bool hasSlot = slotIt != snapshotSlots_.end();
    //  The front data-lane entry's `.encoded` has already been handed to
    //  socket_.Send() by the time it is in flight (MaybeStartSendLocked
    //  copies it out before calling Send) -- mutating it in place here would
    //  silently diverge the wire bytes from what OnSendComplete's byte
    //  accounting assumes was sent, and the caller's newer value would never
    //  actually reach the peer. Route that case through the dirty marker
    //  instead so it is applied only once this entry's completion has been
    //  accounted for.
    bool slotInFlight = hasSlot && sendInFlight_ && !sendingReserved_ &&
                        slotIt->second == dataPending_.begin();

    if (hasSlot && !slotInFlight &&
        CanReplaceLocked(slotIt->second->queueClass,
                         slotIt->second->encoded.size(), queueClass, bytes)) {
        ApplyReplaceLocked(slotIt->second, queueClass, std::move(encoded));
    } else if (!hasSlot && CanAdmitNewLocked(queueClass, bytes)) {
        ApplyAdmitNewLocked(PendingEntry{
            .isSnapshot = true,
            .stateArea = std::move(stateArea),
            .revision = std::nullopt,
            .encoded = std::move(encoded),
            .queueClass = queueClass,
        });
    } else {
        dirtyMarkers_[std::move(stateArea)] =
            DirtyEntry{.encoded = std::move(encoded), .queueClass = queueClass};
    }

    MaybeStartSendLocked();
}

void BoundedOutboundQueue::PublishEvent(std::string stateArea,
                                        protocol::Envelope envelope) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (stopped_) {
        return;
    }

    //  Decoded from the envelope this queue is itself handed, rather than
    //  taken as a separate parameter: IOutboundPublicationSink::PublishEvent
    //  is Stage 2's frozen contract and does not carry a revision field of
    //  its own.
    auto decodedPayload = protocol::DecodeStateEventPayload(envelope.payload);
    std::optional<std::int64_t> revision;
    if (decodedPayload.has_value()) {
        revision = decodedPayload->revision;
    }

    auto barrierIt = barriers_.find(stateArea);
    if (barrierIt != barriers_.end() && revision.has_value() &&
        *revision <= barrierIt->second) {
        //  Superseded by an already-accepted recovery snapshot for this
        //  state area -- discarded rather than delivered after it.
        return;
    }

    envelope.sessionId = sessionId_;
    std::string encoded = protocol::EncodeEnvelope(envelope);
    QueueClass queueClass = Classify(encoded);
    std::size_t bytes = encoded.size();

    if (!CanAdmitNewLocked(queueClass, bytes)) {
        //  Reliable Event traffic is never dropped or coalesced -- a client
        //  that cannot keep up is disconnected instead
        //  (ai/context/skse/architecture.md's "Failure semantics").
        stopped_ = true;
        socket_.Shutdown();
        return;
    }

    ApplyAdmitNewLocked(PendingEntry{
        .isSnapshot = false,
        .stateArea = std::move(stateArea),
        .revision = revision,
        .encoded = std::move(encoded),
        .queueClass = queueClass,
    });

    MaybeStartSendLocked();
}

void BoundedOutboundQueue::PublishRecoverySnapshot(std::string stateArea,
                                                   protocol::Envelope envelope,
                                                   std::int64_t revision) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (stopped_) {
        return;
    }

    SupersedeQueuedEventsLocked(stateArea, revision);
    barriers_[stateArea] = revision;

    envelope.sessionId = sessionId_;
    AdmitReservedOrDisconnectLocked(protocol::EncodeEnvelope(envelope));
}

void BoundedOutboundQueue::PublishControl(protocol::Envelope envelope) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (stopped_) {
        return;
    }

    envelope.sessionId = sessionId_;
    AdmitReservedOrDisconnectLocked(protocol::EncodeEnvelope(envelope));
}

void BoundedOutboundQueue::AdmitReservedOrDisconnectLocked(std::string encoded) {
    if (reservedSlotsUsed_ >= security::kReservedControlRecoverySlots) {
        //  ai/context/protocol/security.md's "Input limits": "If reserved
        //  control/recovery capacity is full, the client is marked
        //  unavailable and the connection is closed."
        stopped_ = true;
        socket_.Shutdown();
        return;
    }

    ++reservedSlotsUsed_;
    reservedPending_.push_back(std::move(encoded));
    MaybeStartSendLocked();
}

} //  namespace dovahlink::application
