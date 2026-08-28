#include "application/outbound_publication_sink.hpp"

#include "protocol/state_event_payload.hpp"
#include "security/constants.hpp"

#include <utility>

namespace dovahlink::application {

BoundedOutboundQueue::BoundedOutboundQueue(
    transport::ISocket& socket, IPublicationDiagnostics& diagnostics,
    std::string sessionId)
    : socket_(socket), diagnostics_(diagnostics),
      sessionId_(std::move(sessionId)),
      completionState_(std::make_shared<CompletionState>()) {
    completionState_->owner = this;
}

BoundedOutboundQueue::~BoundedOutboundQueue() noexcept {
    std::unique_lock<std::mutex> lock(completionState_->mutex);
    completionState_->destroying = true;
    completionState_->owner = nullptr;
    completionState_->changed.wait(
        lock, [this] { return completionState_->callbacksInFlight == 0; });
}

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
    if (bytes > security::kOutboundQueueByteBudget ||
        totalBytesUsed_ > security::kOutboundQueueByteBudget - bytes) {
        return false;
    }
    return laneUsed < laneCapacity;
}

bool BoundedOutboundQueue::CanReplaceLocked(
    QueueClass oldClass, std::size_t oldBytes, QueueClass newClass,
    std::size_t newBytes) const {
    if (oldBytes > totalBytesUsed_ ||
        newBytes > security::kOutboundQueueByteBudget ||
        totalBytesUsed_ - oldBytes >
            security::kOutboundQueueByteBudget - newBytes) {
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
    entry.enqueuedAt = std::chrono::steady_clock::now();
    bool isSnapshot = entry.isSnapshot;
    std::string stateArea = entry.stateArea;
    dataPending_.push_back(std::move(entry));
    if (isSnapshot) {
        snapshotSlots_[std::move(stateArea)] = std::prev(dataPending_.end());
    }
    diagnostics_.RecordQueueDepth(normalSlotsUsed_, heavySlotsUsed_,
                                  reservedSlotsUsed_, totalBytesUsed_);
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
    slot->enqueuedAt = std::chrono::steady_clock::now();
    diagnostics_.RecordCoalesced(slot->stateArea);
    diagnostics_.RecordQueueDepth(normalSlotsUsed_, heavySlotsUsed_,
                                  reservedSlotsUsed_, totalBytesUsed_);
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
        if (!it->second.encoded.has_value()) {
            continue;
        }
        std::size_t bytes = it->second.encoded->size();
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
        std::string encoded = std::move(*it->second.encoded);
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

std::size_t BoundedOutboundQueue::SupersedeQueuedEventsLocked(
    const std::string& stateArea, std::int64_t revision) {
    std::size_t supersededCount = 0;
    auto it = dataPending_.begin();
    if (sendInFlight_ && !sendingReserved_ && it != dataPending_.end()) {
        //  The front entry is already in flight; its bytes cannot be recalled.
        ++it;
    }
    while (it != dataPending_.end()) {
        if (!it->isSnapshot && it->stateArea == stateArea &&
            it->revision.has_value() && *it->revision <= revision) {
            totalBytesUsed_ -= it->encoded.size();
            DecrementLaneLocked(it->queueClass);
            it = dataPending_.erase(it);
            ++supersededCount;
        } else {
            ++it;
        }
    }
    return supersededCount;
}

std::optional<std::string> BoundedOutboundQueue::MaybeStartSendLocked() {
    if (sendInFlight_) {
        return std::nullopt;
    }
    if (!reservedPending_.empty()) {
        sendInFlight_ = true;
        sendingReserved_ = true;
        return reservedPending_.front().encoded;
    }
    if (!dataPending_.empty()) {
        sendInFlight_ = true;
        sendingReserved_ = false;
        return dataPending_.front().encoded;
    }
    return std::nullopt;
}

void BoundedOutboundQueue::DispatchSend(std::string encoded) noexcept {
    auto completionState = completionState_;
    socket_.Send(
        std::move(encoded),
        [completionState](bool ok) noexcept {
            BoundedOutboundQueue* owner = nullptr;
            {
                std::lock_guard<std::mutex> lock(completionState->mutex);
                if (completionState->destroying ||
                    completionState->owner == nullptr) {
                    return;
                }
                ++completionState->callbacksInFlight;
                owner = completionState->owner;
            }

            owner->OnSendComplete(ok);

            {
                std::lock_guard<std::mutex> lock(completionState->mutex);
                --completionState->callbacksInFlight;
            }
            completionState->changed.notify_all();
        });
}

void BoundedOutboundQueue::OnSendComplete(bool ok) noexcept {
    std::optional<std::string> next;
    bool disconnect = false;
    try {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            sendInFlight_ = false;
            if (sendingReserved_) {
                if (!reservedPending_.empty()) {
                    ReservedEntry finished = std::move(reservedPending_.front());
                    reservedPending_.pop_front();
                    --reservedSlotsUsed_;
                    diagnostics_.RecordDequeueLatency(
                        std::chrono::steady_clock::now() - finished.enqueuedAt);
                    diagnostics_.RecordQueueDepth(
                        normalSlotsUsed_, heavySlotsUsed_, reservedSlotsUsed_,
                        totalBytesUsed_);
                }
            } else if (!dataPending_.empty()) {
                PendingEntry finished = std::move(dataPending_.front());
                dataPending_.pop_front();
                if (finished.isSnapshot) {
                    snapshotSlots_.erase(finished.stateArea);
                }
                DecrementLaneLocked(finished.queueClass);
                totalBytesUsed_ -= finished.encoded.size();
                diagnostics_.RecordDequeueLatency(
                    std::chrono::steady_clock::now() - finished.enqueuedAt);
                diagnostics_.RecordQueueDepth(
                    normalSlotsUsed_, heavySlotsUsed_, reservedSlotsUsed_,
                    totalBytesUsed_);
            }

            if (!ok) {
                stopped_ = true;
                diagnostics_.RecordDisconnect(DisconnectReason::kSendFailed);
                disconnect = true;
            } else {
                TryPromoteOneDirtyLocked();
                next = MaybeStartSendLocked();
            }
        }
        if (disconnect) {
            socket_.Shutdown();
        } else if (next.has_value()) {
            DispatchSend(std::move(*next));
        }
    } catch (...) {
        //  Completion handlers are a noexcept boundary. A diagnostics or
        //  allocation failure still ends the session rather than escaping the
        //  transport callback.
        try {
            {
                std::lock_guard<std::mutex> lock(mutex_);
                stopped_ = true;
                sendInFlight_ = false;
            }
            socket_.Shutdown();
        } catch (...) {
            //  Keep this final guard for non-conforming test doubles.
        }
    }
}

void BoundedOutboundQueue::PublishSnapshot(std::string stateArea,
                                           protocol::Envelope envelope) {
    std::optional<std::string> next;
    {
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
        //  The front data-lane entry's bytes have already been handed to
        //  Send while it is in flight, so a newer value uses the dirty marker
        //  until completion accounts for the original entry.
        bool slotInFlight = hasSlot && sendInFlight_ && !sendingReserved_ &&
                            slotIt->second == dataPending_.begin();

        if (hasSlot && !slotInFlight &&
            CanReplaceLocked(slotIt->second->queueClass,
                             slotIt->second->encoded.size(), queueClass,
                             bytes)) {
            dirtyMarkers_.erase(stateArea);
            ApplyReplaceLocked(slotIt->second, queueClass, std::move(encoded));
        } else if (!hasSlot && CanAdmitNewLocked(queueClass, bytes)) {
            dirtyMarkers_.erase(stateArea);
            ApplyAdmitNewLocked(PendingEntry{
                .isSnapshot = true,
                .stateArea = std::move(stateArea),
                .revision = std::nullopt,
                .encoded = std::move(encoded),
                .queueClass = queueClass,
            });
        } else {
            //  A publication larger than the whole data-byte budget cannot
            //  ever be admitted. Keep only bounded metadata and rely on the
            //  authoritative store plus a later bounded capture to retry it.
            std::optional<std::string> retained =
                bytes <= security::kOutboundQueueByteBudget
                    ? std::optional<std::string>(std::move(encoded))
                    : std::nullopt;
            dirtyMarkers_[std::move(stateArea)] = DirtyEntry{
                .encoded = std::move(retained),
                .encodedBytes = bytes,
                .queueClass = queueClass};
        }

        next = MaybeStartSendLocked();
    }
    if (next.has_value()) {
        DispatchSend(std::move(*next));
    }
}

void BoundedOutboundQueue::PublishEvent(std::string stateArea,
                                        protocol::Envelope envelope) {
    std::optional<std::string> next;
    bool disconnect = false;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (stopped_) {
            return;
        }

        auto decodedPayload = protocol::DecodeStateEventPayload(envelope.payload);
        std::optional<std::int64_t> revision;
        if (decodedPayload.has_value()) {
            revision = decodedPayload->revision;
        }

        auto barrierIt = barriers_.find(stateArea);
        if (barrierIt != barriers_.end() && revision.has_value() &&
            *revision <= barrierIt->second) {
            return;
        }

        envelope.sessionId = sessionId_;
        std::string encoded = protocol::EncodeEnvelope(envelope);
        QueueClass queueClass = Classify(encoded);
        std::size_t bytes = encoded.size();

        if (!CanAdmitNewLocked(queueClass, bytes)) {
            stopped_ = true;
            diagnostics_.RecordDisconnect(DisconnectReason::kEventOverflow);
            disconnect = true;
        } else {
            ApplyAdmitNewLocked(PendingEntry{
                .isSnapshot = false,
                .stateArea = std::move(stateArea),
                .revision = revision,
                .encoded = std::move(encoded),
                .queueClass = queueClass,
            });
            next = MaybeStartSendLocked();
        }
    }
    if (disconnect) {
        socket_.Shutdown();
    } else if (next.has_value()) {
        DispatchSend(std::move(*next));
    }
}

void BoundedOutboundQueue::PublishRecoverySnapshot(
    std::string stateArea, protocol::Envelope envelope,
    std::int64_t revision) {
    std::optional<std::string> next;
    bool disconnect = false;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (stopped_) {
            return;
        }

        std::size_t supersededCount =
            SupersedeQueuedEventsLocked(stateArea, revision);
        barriers_[stateArea] = revision;
        diagnostics_.RecordRecovery(stateArea, revision, supersededCount);

        envelope.sessionId = sessionId_;
        disconnect = AdmitReservedOrDisconnectLocked(
            protocol::EncodeEnvelope(envelope));
        if (!disconnect) {
            next = MaybeStartSendLocked();
        }
    }
    if (disconnect) {
        socket_.Shutdown();
    } else if (next.has_value()) {
        DispatchSend(std::move(*next));
    }
}

void BoundedOutboundQueue::PublishControl(protocol::Envelope envelope) {
    std::optional<std::string> next;
    bool disconnect = false;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        if (stopped_) {
            return;
        }

        envelope.sessionId = sessionId_;
        disconnect = AdmitReservedOrDisconnectLocked(
            protocol::EncodeEnvelope(envelope));
        if (!disconnect) {
            next = MaybeStartSendLocked();
        }
    }
    if (disconnect) {
        socket_.Shutdown();
    } else if (next.has_value()) {
        DispatchSend(std::move(*next));
    }
}

bool BoundedOutboundQueue::AdmitReservedOrDisconnectLocked(
    std::string encoded) {
    if (reservedSlotsUsed_ >= security::kReservedControlRecoverySlots) {
        stopped_ = true;
        diagnostics_.RecordDisconnect(DisconnectReason::kReservedLaneFull);
        return true;
    }

    ++reservedSlotsUsed_;
    reservedPending_.push_back(
        ReservedEntry{.encoded = std::move(encoded),
                      .enqueuedAt = std::chrono::steady_clock::now()});
    diagnostics_.RecordQueueDepth(normalSlotsUsed_, heavySlotsUsed_,
                                  reservedSlotsUsed_, totalBytesUsed_);
    return false;
}

} //  namespace dovahlink::application
