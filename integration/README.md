# DovahLink integration tests

This directory holds the independent validation client and the deterministic end-to-end scenarios
that exercise the Skyrim bridge's real protocol behavior. Everything here is a separate protocol
consumer, hand-written with `System.Text.Json`; nothing links against or shares code with the
bridge's own C++ codec.

## Contents

- `DovahLinkValidationClient/` — a .NET 9 console app: `Envelope.cs` (encode/decode), `BridgeConnection.cs`
  (a `ClientWebSocket` wrapper), and `Program.cs`, a small interactive client for manual verification
  against a real, running Skyrim plugin.
- `DovahLinkValidationClient.Tests/` — xUnit scenarios that launch `bridge/harness/dovahlink_bridge_harness.cpp`
  (a Skyrim-independent process running the real bridge stack against a fake character level; see
  `bridge/README.md`) as a subprocess and drive it over a real socket. Every scenario launches its
  own harness instance; test-class parallelization is disabled (`AssemblyInfo.cs`) since every
  instance binds the same fixed, documented port.
- `run-scenarios.ps1` — builds the harness, then runs the full test suite in one command.

## Running the automated scenarios

Requires the .NET 9 SDK and the bridge's own pinned toolchain (`bridge/README.md`'s "Toolchain"
section) to build the harness.

```bash
powershell -File integration/run-scenarios.ps1
```

or, if the harness is already built:

```bash
cd integration
dotnet test DovahLinkValidation.sln
```

The suite takes roughly 8-10 seconds; a few scenarios deliberately wait out real timeouts (the
5-second handshake timeout, a shortened token-expiry window) to prove OS-level enforcement over an
actual socket rather than only at the unit level. Two scenario groups are deliberately *not*
covered by real, full-duration waits — the exact 10,000-message session cap and the 60-second idle
timeout — each documented inline in `LimitsScenarioTests.cs` / `ReconnectScenarioTests.cs` with why:
both are already proven exactly by a C++ unit test with an injected clock or pre-filled state, and a
genuine live proof would cost minutes of suite time for no additional confidence in the same logic.

## Running the manual validation client

For manual verification against a real, running Skyrim + SKSE + DovahLink Bridge plugin, use the
record template in `bridge/README.md`:

```powershell
cd integration/DovahLinkValidationClient
$env:DOVAHLINK_BRIDGE_TOKEN = "<the same hex token the plugin was launched with>"
dotnet run
```

It connects, negotiates `hello`/`hello_ack`, exchanges capabilities, subscribes to `character`, and
prints every message the bridge sends. `DOVAHLINK_BRIDGE_HOST` / `DOVAHLINK_BRIDGE_PORT` override
the defaults (`127.0.0.1` / `58231`, the documented Phase 1 port) if needed.

## Known Phase 1 boundary

Every scenario here proves the pull side of state delivery (`subscribe` / `snapshot_request`) and
the transport/security/session machinery around it. Live, unprompted `state_event` push to an
already-subscribed client and genuine reconnect after a successful session are both out of scope
for Phase 1 by design — see `bridge/README.md`'s "Live event delivery is deferred to Phase 1.5" and
"Known limitation: no reconnect after a successful session" for why, and `ROADMAP.md`'s Phase 1.5
and Phase 1.25 entries for where each is planned.
