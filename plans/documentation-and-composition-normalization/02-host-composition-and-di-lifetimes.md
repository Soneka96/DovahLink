# Concept 02 -- Host composition, DI scopes, and service lifetimes

**Status:** Complete on branch `feature/02-host-composition-and-di-lifetimes`, not yet
opened as a PR (see `CONTEXT.md`'s Active concept entry, D8, and D9 for the full
step-build history). D8 introduced a real `Microsoft.Extensions.DependencyInjection`
composition mechanism, but that pass's registrations still manually forwarded
constructor dependencies through `sp => new Foo(sp.GetRequiredService<...>(), ...)`
lambdas in most services -- using the container without gaining its actual benefit (a
constructor change no longer requiring a composition edit). D9 (this pass, six
step-build steps) removed that remaining constructor-forwarding across all five
composition modules, per the maintainer's clarified requirement that ordinary Host
services use automatic constructor-injection resolution, not hand-threaded lambdas --
and, per a further maintainer refinement mid-pass, that no production consumer resolve
a service by its concrete type at all, only by interface. See this file's final
composition audit table below for the complete accounting.

**Covers:** R2.1-R2.10 (see `PLAN.md` Requirement IDs; original wording in `SOURCE.md`
Block A Issue 2).

**Depends on:** Concept 01.3c merged to `main` (per `DIVERGENCES.md` D4: corrected
conventions must be authoritative, and the public compatibility/version and
instance-identity vocabulary must be stable, before new Host composition code and its
documentation are written -- there is little value composing Host around names and
identity concepts about to be renamed).

## Why this is a stable concept

`DovahLink.Host`'s composition root (`Program.cs`) and its service lifetimes are one
ownership boundary: nothing here changes wire behavior, only how the same object graph
is assembled and how long each piece lives. It is independently reviewable from the
Adapter side (Concept 03, no shared files) and must land before Concept 04 documents
the result.

## Design

- Introduce `host/DovahLink.Host/Composition/` with cohesive
  `*ServiceExtensions.cs` registration modules (exact names follow the actual current
  service graph rather than the illustrative list in `SOURCE.md`).
- Audit every current Host service and classify it as host-lifetime singleton,
  client-scoped, session-scoped, connection-scoped, or transient, per the domain
  distinctions in `SOURCE.md` Block A Issue 2 -- decide from responsibility, not from
  the issue's example lists.
- If the lifetime audit discovers static/global keyed storage being used to emulate
  client/session/connection scoping (a `static Dictionary<string, PublicStateSubscription>
  Instances`-shaped pattern is the illustrative shape, not a claim that one currently
  exists), replace it with an explicit session/connection aggregate or factory
  (`IPublicSessionFactory`, `IPublicConnectionFactory`, or equivalent names matching
  current Host vocabulary). Do not assume such a pattern exists, and do not introduce
  a factory/aggregate abstraction solely to satisfy this example when the audit finds
  nothing that needs it.
- Keep DI responsible only for constructing dependencies. Introduce or retain one
  explicit Host lifecycle orchestrator (`DovahLinkHostRuntime` or equivalent) that
  controls startup and shutdown ordering explicitly rather than delegating it to
  implicit `IHostedService` ordering. This concept is about **composition
  equivalence** -- making today's ownership and lifetimes explicit -- not a license to
  redesign startup or networking behavior. Do not treat any illustrative lifecycle
  sequence as authority over the current implementation: before restructuring
  anything, establish the actual current startup/shutdown ordering by reading
  `Program.cs` and its existing tests, and preserve that exact ordering. The one
  non-negotiable invariant, already verified in the current code, is that trust/
  security state finishes loading (and fails closed on error) strictly before any
  externally reachable listener or admission path is exposed; everything else about
  the current sequence is preserved as observed, not re-derived from a template.
- No sync-over-async in any registration; no service locator or static
  `IServiceProvider` access anywhere in the new composition code.
- Reduce `Program.cs` to bootstrap/composition wiring only.

## Files this concept may change

- `host/DovahLink.Host/Program.cs`
- New `host/DovahLink.Host/Composition/*.cs`
- Existing Host service classes whose constructor signature or registration lifetime
  changes as a direct result of the audit (not a broader rewrite of their internals)
- `host/DovahLink.Host.Tests/**` additions proving the lifetime/composition contracts

## Tests / proof obligations

Per R2.9: composition/lifetime tests proving --

- host-lifetime services resolve to the same instance across resolutions;
- two different sessions never share a session-owned service instance;
- collaborators within one session observe the same session-owned instance;
- two different connections never share a connection-owned service instance;
- a reconnect/new session receives fresh session/connection-scoped state;
- `clientId` persistence does not leak session- or connection-scoped state across
  reconnects;
- startup still fails closed when trust/security initialization fails;
- listeners/rendezvous are not exposed before security state has initialized
  successfully;
- shutdown is idempotent and ordered;
- existing test seams remain usable (no test rewritten merely because of this
  refactor's mechanics, only because a seam's construction path changed).

## Non-goals

- Documentation normalization of the resulting Host code (Concept 04, after this
  merges).
- Any change to wire protocol, message handling, or externally observable Host
  behavior.
- Adapter-side composition (Concept 03) -- no shared files, no coordinated design
  decision required beyond both concepts inheriting the corrected conventions from
  Concept 01.
- Introducing ASP.NET-style request scoping where it would make WebSocket/session
  ownership less clear than an explicit factory.

## Completion criteria and evidence

- Every R2.x acceptance bullet satisfied and traceable to a specific file/test.
- Full Host test suite green; new composition/lifetime tests included and passing.
- `PLAN.md` status table updated with this concept's PR number, marked `Complete` once
  merged; unblocks Concept 04.

### R2.1-R2.10 traceability

| Req | Requirement | Evidence |
| --- | --- | --- |
| R2.1 | `Program.cs` reduced to bootstrap/composition | `Program.ComposeAndRunAsync` (`host/DovahLink.Host/Program.cs`): resolve configuration, run the one pre-container fail-closed trust bootstrap, build one `IServiceCollection`/`ServiceProvider`, resolve `DovahLinkHostRuntime`, run it. No manual `new Foo(new Bar(...))` graph assembly remains. |
| R2.2 | Registrations split into cohesive composition modules | `Composition/CoreServiceExtensions.cs`, `TrustServiceExtensions.cs`, `AdapterIpcServiceExtensions.cs`, `PublicClientServiceExtensions.cs`, `HostRuntimeServiceExtensions.cs` -- one `Add*Services`/`AddHostRuntime` extension per area, one authoritative `IServiceCollection` graph. |
| R2.3 | Every behavior-bearing dependency has a deliberate lifetime | All Host services registered `AddSingleton` (host-lifetime, one per process); `PublicConnectionFactory`/`AdapterConnectionFactory` are themselves Host-lifetime singletons whose `Create(Stream)` produces connection-owned state per call, and are now themselves automatically DI-constructed (D9) rather than assembled by a multi-argument forwarding lambda. `CoreServiceExtensionsTests.AddCoreServices_ClockResolvedTwice_ReturnsSameInstance` and `TrustServiceExtensionsTests.AddTrustServices_SessionRegistryResolvedTwice_ReturnsSameInstance` prove singleton identity. D9's further refinement: `SessionRegistry`/`PairingCoordinator` no longer have a separate concrete-type registration at all -- `ISessionRegistry` gained `ActiveCount`/`MaxActiveSessions` so every consumer, including `Program.ComposeAndRunAsync`'s own `onComposed` test-observability hook, resolves by interface only, closing the concrete/interface dual-instance hazard entirely rather than merely proving it doesn't happen. |
| R2.4 | Client/session/connection identities remain distinct | `PublicHelloAdmissionHandler` keeps `ConnectionId`, `SessionId`, and `ClientId` as three separate fields (unchanged by this concept); `PublicClientConnectionLifetimeTests.Hello_ReconnectWithSameClientIdAndMessageId_GetsFreshSessionNotRejectedAsReplay` proves persistent `clientId` never collapses into session/connection identity. |
| R2.5 | No cross-session or cross-connection state sharing | `PublicClientConnectionLifetimeTests.CurrentConnections_TwoAcceptedConnections_OutboundStateIsIsolated` proves two simultaneously accepted connections' outbound state (`DataLaneOutboundQueue`) is isolated; `PublicConnectionFactoryTests`/`AdapterConnectionFactoryTests` prove the factories never return the same connection instance twice. |
| R2.6 | Fail-closed async security startup; no sync-over-async | `Program.ComposeAndRunAsync` awaits `TrustServiceExtensions.CreateTrustStoreAsync` (a real `await`, no `.Result`/`.Wait()`/`.GetAwaiter().GetResult()`) before the `ServiceCollection` is even built; `ProgramCompositionTests`' existing malformed-trust-store and bad-port `SocketException`/`InvalidDataException` tests pass unmodified, proving the fail-closed ordering and exception types are unchanged. |
| R2.7 | Explicit Host runtime owns lifecycle | `DovahLinkHostRuntime` (`host/DovahLink.Host/Process/DovahLinkHostRuntime.cs`) remains the sole lifecycle owner and its `RunAsync` orchestration is unchanged by D9; only its constructor changed (an injected `IAdapterPeerProofVerifier` replacing two raw `byte[]` parameters extracted manually in composition, and its `publicListener` parameter gaining a `= null` default). `HostRuntimeServiceExtensions.AddHostRuntime` now registers it with plain `services.AddSingleton<DovahLinkHostRuntime>()` -- DI constructs it automatically, including resolving `publicListener` as `null` via the default when `AddPublicClientServices` left `IPublicWebSocketListener` unregistered, proven by `HostRuntimeServiceExtensionsTests.AddHostRuntime_NoPublicListenerRegistered_DovahLinkHostRuntimeStillResolves`. |
| R2.8 | No service locator/static `IServiceProvider` | No production class holds an injected `IServiceProvider` field; the only remaining `sp.GetRequiredService<T>()` call in `Composition/` is `CoreServiceExtensions`'s single justified `IStateAuthorityLifecycle` event-wiring factory (see the final composition audit table below); `Program.ComposeAndRunAsync`'s two `provider.GetRequiredService<T>()` calls (resolving `DovahLinkHostRuntime` to run it, and the `onComposed` test-observability hook) are the composition root itself, not business logic. |
| R2.9 | Lifetime boundaries proven by tests | See R2.3 (singleton identity) and R2.5 (connection isolation) rows above, plus the reconnect/replay-state proof under R2.4. The previously recorded "structurally guaranteed but not independently wire-observable" gap is closed -- `CONTEXT.md`'s Active concept entry records its removal. |
| R2.10 | Behavior remains equivalent | `ProgramCompositionTests.cs` required zero edits across the whole D8 DI migration; D9 touched it only once, changing its `onComposed` callback's two local variable declarations from the concrete `SessionRegistry?`/`PairingCoordinator?` types to `ISessionRegistry?`/`IPairingCoordinator?` -- a direct, mechanical consequence of `Program.ComposeAndRunAsync`'s own `onComposed` parameter type changing for the interface-only-resolution refinement (D9), not a behavior change; every assertion in that file (`sessionRegistry.ActiveCount`, `pairingCoordinator.GetStatusSnapshot(...)`, `sessionRegistry!.MaxActiveSessions`) is unchanged because both interfaces already exposed everything those tests needed. `Program.ComposeAndRunAsync`'s public signature, fail-closed ordering, exception types, and rendezvous line ordering are otherwise unchanged; full Host suite green throughout both passes (1787/1787 after D9). |

### Final composition audit (D9)

Every remaining occurrence of `sp =>`, `IServiceProvider`, `GetRequiredService`/`GetService`, or `new <production service>` inside `Composition/`, after removing every pure constructor-forwarding registration:

| Remaining manual registration | Reason it must remain | Why constructor auto-resolution is inappropriate |
| --- | --- | --- |
| `CoreServiceExtensions.AddCoreServices`'s `IStateAuthorityLifecycle` factory (`sp => { var lifecycle = new StateAuthorityLifecycle(...); lifecycle.FatalFailureOccurred += () => shutdown.Cancel(); return lifecycle; }`) | Wires a composition-root-owned event handler that captures `shutdown` (the bootstrap `CancellationTokenSource`, never itself a registered service) to the constructed instance's own event, per R2.6's fail-closed shutdown requirement. | Automatic resolution can construct `StateAuthorityLifecycle` itself, but subscribing a closure to its event afterward is composition-time wiring, not a constructor dependency -- there is no constructor shape that expresses "and also do this side effect." |
| `CoreServiceExtensions.AddCoreServices`'s `new HostSettingsProvider()` / `TrustServiceExtensions.CreateTrustStoreAsync`'s `new WindowsDpapiTrustStorePersistence()` | Both are pre-existing default-fallback instantiations for an optional caller-supplied override (a test redirects either to a private file), computed *before* any DI registration -- `HostSettingsProvider.Load()`'s result is registered as a resolved `HostSettings` instance, and `WindowsDpapiTrustStorePersistence` feeds the pre-container async trust bootstrap. Neither is itself a DI-resolved service. | These values do not exist as container registrations at all; there is nothing for constructor auto-resolution to apply to. |

No other resolver, service-locator call, or manual `new` of a registered production service remains anywhere in `Composition/`. `Program.ComposeAndRunAsync`'s two `GetRequiredService` calls are the composition root's own required final resolution step (starting the runtime; the `onComposed` test-observability hook), not service location from business logic.
