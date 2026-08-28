#include "application/outbound_publication_sink.hpp"

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

void BoundedOutboundQueue::ApplyAdmitNewLocked(std::string stateArea,
                                               QueueClass queueClass,
                                               std::string encoded) {
    IncrementLaneLocked(queueClass);
    totalBytesUsed_ += encoded.size();
    pending_.push_back(PendingEntry{
        .stateArea = stateArea,
        .encoded = std::move(encoded),
        .queueClass = queueClass,
    });
    snapshotSlots_[std::move(stateArea)] = std::prev(pending_.end());
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
            ApplyAdmitNewLocked(std::move(area), queueClass,
                                std::move(encoded));
        }
        return;
    }
}

void BoundedOutboundQueue::MaybeStartSendLocked() {
    if (sendInFlight_ || pending_.empty()) {
        return;
    }
    sendInFlight_ = true;
    std::string next = pending_.front().encoded;
    socket_.Send(std::move(next), [this](bool ok) { OnSendComplete(ok); });
}

void BoundedOutboundQueue::OnSendComplete(bool ok) {
    std::lock_guard<std::mutex> lock(mutex_);
    sendInFlight_ = false;
    if (pending_.empty()) {
        return;
    }
    PendingEntry finished = std::move(pending_.front());
    pending_.pop_front();
    if (finished.stateArea.has_value()) {
        snapshotSlots_.erase(*finished.stateArea);
    }
    DecrementLaneLocked(finished.queueClass);
    totalBytesUsed_ -= finished.encoded.size();

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
    //  The front entry's `.encoded` has already been handed to socket_.Send()
    //  by the time it is in flight (MaybeStartSendLocked copies it out before
    //  calling Send) -- mutating it in place here would silently diverge the
    //  wire bytes from what OnSendComplete's byte accounting assumes was
    //  sent, and the caller's newer value would never actually reach the
    //  peer. Route that case through the dirty marker instead so it is
    //  applied only once this entry's completion has been accounted for.
    bool slotInFlight =
        hasSlot && sendInFlight_ && slotIt->second == pending_.begin();

    if (hasSlot && !slotInFlight &&
        CanReplaceLocked(slotIt->second->queueClass,
                         slotIt->second->encoded.size(), queueClass, bytes)) {
        ApplyReplaceLocked(slotIt->second, queueClass, std::move(encoded));
    } else if (!hasSlot && CanAdmitNewLocked(queueClass, bytes)) {
        ApplyAdmitNewLocked(std::move(stateArea), queueClass, std::move(encoded));
    } else {
        dirtyMarkers_[std::move(stateArea)] =
            DirtyEntry{.encoded = std::move(encoded), .queueClass = queueClass};
    }

    MaybeStartSendLocked();
}

void BoundedOutboundQueue::PublishEvent(std::string stateArea,
                                        protocol::Envelope envelope) {
    (void)stateArea;
    std::lock_guard<std::mutex> lock(mutex_);
    if (stopped_) {
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

    IncrementLaneLocked(queueClass);
    totalBytesUsed_ += bytes;
    pending_.push_back(PendingEntry{
        .stateArea = std::nullopt,
        .encoded = std::move(encoded),
        .queueClass = queueClass,
    });

    MaybeStartSendLocked();
}

} //  namespace dovahlink::application
