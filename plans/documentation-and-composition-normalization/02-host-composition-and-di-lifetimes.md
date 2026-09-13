# Concept 02 -- Host composition, DI scopes, and service lifetimes

**Status:** pending

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
