#pragma once

#include <cstdint>

#include "dispatch/sample_capture_result.hpp"

namespace dovahlink::adapter::dispatch {

///  The adapter's native capture boundary: the last step before Skyrim, per
///  `ai/context/adapter/architecture.md`. A host-directed sample token or
///  event key maps here to one approved synchronous read or persistent event
///  registration; no real key is registered yet. Sampling and event
///  registration are two explicit operations rather than one generic
///  translation, because they behave fundamentally differently: a sample
///  read finishes synchronously and produces one captured value, while an
///  event registration finishes synchronously but produces no value of its
///  own -- any later captured value arrives asynchronously, whenever the
///  registered native event actually fires, through a separate capture path.
class IAdapterNativeCaptureRouter {
  public:
    virtual ~IAdapterNativeCaptureRouter() = default;

    ///  Performs the one approved synchronous read for a host-directed
    ///  sample token, synchronously on the calling thread. Callers are
    ///  responsible for calling this only from the Skyrim game thread once a
    ///  real translation is registered for a token.
    ///  @param sampleToken The host-owned sample token.
    ///  @return The result, distinguishing a known token's currently
    ///  unavailable Skyrim value from a sample token with no approved
    ///  translation at all; see `SampleCaptureResult`'s own documentation.
    virtual SampleCaptureResult CaptureSample(std::uint32_t sampleToken) = 0;

    ///  Registers persistent interest in a host-directed event key's native
    ///  event, synchronously on the calling thread. Idempotent: registering
    ///  an already-registered key succeeds without adding a second
    ///  registration. Callers are responsible for calling this only from the
    ///  Skyrim game thread once a real translation is registered for a key.
    ///  @param eventKey The host-owned event key.
    ///  @return Whether `eventKey` now has, or already had, an approved
    ///  persistent registration; `false` for an event key with no approved
    ///  translation.
    virtual bool RegisterEvent(std::uint32_t eventKey) = 0;
};

///  @copydoc IAdapterNativeCaptureRouter
class AdapterNativeCaptureRouter final : public IAdapterNativeCaptureRouter {
  public:
    ///  @copydoc IAdapterNativeCaptureRouter::CaptureSample
    SampleCaptureResult CaptureSample(std::uint32_t sampleToken) override;

    ///  @copydoc IAdapterNativeCaptureRouter::RegisterEvent
    bool RegisterEvent(std::uint32_t eventKey) override;
};

} //  namespace dovahlink::adapter::dispatch
