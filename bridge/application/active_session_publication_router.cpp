#include "application/active_session_publication_router.hpp"

#include <utility>

namespace dovahlink::application {

void ActiveSessionPublicationRouter::Attach(IOutboundPublicationSink& sink) {
    std::lock_guard<std::mutex> lock(mutex_);
    attached_ = &sink;
}

void ActiveSessionPublicationRouter::Detach() {
    std::lock_guard<std::mutex> lock(mutex_);
    attached_ = nullptr;
}

void ActiveSessionPublicationRouter::Detach(IOutboundPublicationSink& expected) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (attached_ == &expected) {
        attached_ = nullptr;
    }
}

void ActiveSessionPublicationRouter::PublishSnapshot(
    std::string stateArea, protocol::Envelope envelope) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (attached_ != nullptr) {
        attached_->PublishSnapshot(std::move(stateArea), std::move(envelope));
    }
}

void ActiveSessionPublicationRouter::PublishEvent(std::string stateArea,
                                                  protocol::Envelope envelope) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (attached_ != nullptr) {
        attached_->PublishEvent(std::move(stateArea), std::move(envelope));
    }
}

void ActiveSessionPublicationRouter::PublishRecoverySnapshot(
    std::string stateArea, protocol::Envelope envelope,
    std::int64_t revision) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (attached_ != nullptr) {
        attached_->PublishRecoverySnapshot(std::move(stateArea),
                                           std::move(envelope), revision);
    }
}

void ActiveSessionPublicationRouter::PublishControl(
    protocol::Envelope envelope) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (attached_ != nullptr) {
        attached_->PublishControl(std::move(envelope));
    }
}

} //  namespace dovahlink::application
