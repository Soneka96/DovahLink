# Concept 02 -- Host composition, DI scopes, and service lifetimes

**Status:** pending

**Covers:** R2.1-R2.10 (see `PLAN.md` Requirement IDs; original wording in `SOURCE.md`
Block A Issue 2).

**Depends on:** Concept 01 merged to `main` (corrected conventions must be
authoritative before new Host composition code and its documentation are written).

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
- Replace any static-dictionary-keyed scoped state (the
  `static Dictionary<string, PublicStateSubscription> Instances`-shaped pattern) with
  an explicit session/connection aggregate or factory (`IPublicSessionFactory`,
  `IPublicConnectionFactory`, or equivalent names matching current Host vocabulary).
- Keep DI responsible only for constructing dependencies. Introduce or retain one
  explicit Host lifecycle orchestrator (`DovahLinkHostRuntime` or equivalent) that
  enforces: build graph -> initialize persistent/security state -> start Adapter IPC ->
  publish readiness/rendezvous -> expose public listener/admission -> run -> ordered
  shutdown. This ordering must not be delegated to implicit `IHostedService` ordering.
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
- `PLAN.md` status table updated with PR number and merge SHA; unblocks Concept 04.
