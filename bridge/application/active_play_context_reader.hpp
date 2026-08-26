#pragma once

#include "application/active_play_context.hpp"

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
    ///  @param activePlayContext Context owner used as the read source.
    explicit ActivePlayContextReader(
        const IActivePlayContext& activePlayContext);

    ///  @copydoc IActivePlayContextReader::AcquireCurrent
    [[nodiscard]] std::shared_ptr<PlayContext>
    AcquireCurrent() const override;

  private:
    ///  Lifecycle owner whose current context is exposed read-only.
    const IActivePlayContext& activePlayContext_;
};

} //  namespace dovahlink::application
