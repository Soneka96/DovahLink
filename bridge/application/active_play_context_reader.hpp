#pragma once

#include "application/play_context_lifecycle.hpp"

#include <memory>

namespace dovahlink::application {

///  Read-only capability for consumers that need the currently active context.
class IActivePlayContextReader {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextReader() = default;

    ///  Returns the currently active play context.
    [[nodiscard]] virtual std::shared_ptr<PlayContext>
    AcquireCurrent() const = 0;
};

///  Adapts the lifecycle-owned context to its read-only consumer contract.
class ActivePlayContextReader final : public IActivePlayContextReader {
  public:
    ///  Binds the reader to the lifecycle-owned context.
    ///  @param playContextLifecycle Lifecycle aggregate used as the read source.
    explicit ActivePlayContextReader(
        const IPlayContextLifecycle& playContextLifecycle);

    ///  @copydoc IActivePlayContextReader::AcquireCurrent
    [[nodiscard]] std::shared_ptr<PlayContext>
    AcquireCurrent() const override;

  private:
    ///  Lifecycle aggregate whose current context is exposed read-only.
    const IPlayContextLifecycle& playContextLifecycle_;
};

} //  namespace dovahlink::application
