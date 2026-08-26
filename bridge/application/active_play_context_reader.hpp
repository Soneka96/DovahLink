#pragma once

#include "application/play_context_lifecycle.hpp"

#include <optional>
#include <string>

namespace dovahlink::application {

///  Read-only capability for consumers that need the current context identity.
class IActivePlayContextReader {
  public:
    ///  Allows destruction through the interface.
    virtual ~IActivePlayContextReader() = default;

    ///  Returns a copy of the currently active play-context identifier.
    [[nodiscard]] virtual std::optional<std::string>
    CurrentPlayContextId() const = 0;
};

///  Adapts the lifecycle aggregate to its read-only identity contract.
class ActivePlayContextReader final : public IActivePlayContextReader {
  public:
    ///  Binds the reader to the lifecycle-owned context.
    ///  @param playContextLifecycle Lifecycle aggregate used as the read source.
    explicit ActivePlayContextReader(
        const IPlayContextLifecycle& playContextLifecycle);

    ///  @copydoc IActivePlayContextReader::CurrentPlayContextId
    [[nodiscard]] std::optional<std::string>
    CurrentPlayContextId() const override;

  private:
    ///  Lifecycle aggregate whose current identity is exposed read-only.
    const IPlayContextLifecycle& playContextLifecycle_;
};

} //  namespace dovahlink::application
