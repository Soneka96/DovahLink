# SDK and Flutter Architecture Refactoring

**Status:** Active — planning. This record holds the SDK/Flutter architecture audit and the ordered
refactoring backlog. Documentation and investigation only: no production code, tests, protocol,
dependency, or governing document was changed by this audit.

**Why this left the normal roadmap.** Stage 5 is complete and the next ordinary planning action is
the Stage 6 acceptance audit ([`ROADMAP.md`](../../../ROADMAP.md)). Before more SDK and Flutter
feature work lands, the maintainer requested an architecture audit so that later features build on
verified ownership boundaries rather than on accumulated structure.

**Scope and authority.** The SDK (`sdk/dart/dovahlink_client/`) and Flutter app (`app/`) boundary,
with Host, Adapter, and protocol read only where they bind that boundary. This record decides
nothing on its own: architecture authority stays in [`ARCHITECTURE.md`](../../../ARCHITECTURE.md),
[SDK architecture](../../../ai/context/sdk/architecture.md),
[SDK API design](../../../ai/context/sdk/api-design.md),
[SDK persistence](../../../ai/context/sdk/persistence.md), and
[Flutter architecture](../../../ai/context/flutter/architecture.md); implementation authority stays
in [`AGENTS.md`](../../../AGENTS.md).

**Relationship to the roadmap.** This record does not renumber, reopen, or complete any stage. Each
backlog task below needs its own explicit maintainer instruction naming its scope (`AGENTS.md`), and
tasks that overlap [Stage 5A](../../05a-windows-sas-integration-validation.md) state how they are
sequenced against it. Normal roadmap progression resumes at whatever point the maintainer chooses;
the backlog is advisory ordering, not a gate on Stage 6.

**Location.** The audit was first drafted at `plans/sdk-flutter-architecture-audit.md` and moved
here, the repository's documented home for intentional work outside the ordered roadmap
([deviations index](../README.md)). `plans/` holds only temporary migration records
(`roadmap/03a-host-adapter-production-migration.md`), and several governing documents already carry
dangling links into it (D-1 in Section 8).

## 1. Audit metadata

| Item | Value |
| --- | --- |
| Audit date | 2026-10-09 |
| Base branch | `main` |
| Audited commit | `64e8d1585ea39342422870f6d542ec6515472d36` (merge of PR #129, 2026-10-09) |
| Working branch | `refactor/architecture-inventory` (identical to `main` at audit start; clean tree) |
| Fetch | `git fetch origin` run; `origin/main` equals the audited commit |
| Scope examined | `sdk/dart/dovahlink_client/lib/` (143 files, ~12.3k lines) and `app/lib/` (206 files, ~20.4k lines); tests under `sdk/dart/dovahlink_client/test/` (~35k lines) and `app/test/` (183 test files) |
| Governing documents read | `AGENTS.md`, `ARCHITECTURE.md`, `CONTRIBUTING.md`, `ROADMAP.md`, `sdk/README.md`, `app/README.md`, `ai/context/sdk/{architecture,api-design}.md`, `ai/context/flutter/architecture.md`, `roadmap/05a-windows-sas-integration-validation.md` |
| Open PRs | None. The unauthenticated GitHub API returned `[]` for `pulls?state=open`. |
| Active branches | Six recently updated remote branches were compared to `origin/main` (`feature/sas-pairing-host-integration`, `chore/stage-5-version-audit-and-closeout`, `fix/phase-5-4-overview-parity-and-roadmap-rebaseline`, `refactor/authoritative-state-store`, `investigate/partial-state-after-hot-restart`, `docs/5a-windows-sas-rebaseline`). Each has zero commits ahead of `main`, so none carries unmerged work. |

**Audit revision (2026-10-09).** An independent review raised four issues, resolved on the same
branch: ARCH-001 conflated connection-lifecycle correctness with gameplay-value retention; ARCH-004
proposed admission as the only subscription-restoration trigger, missing trust upgrades on an
existing session; ARCH-006 proposed a new domain-registration abstraction although
`StateDomainDefinition<T>` already exists; and the report had no permanent location. The revision
re-read the cited source. Between the audited commit and this revision only Markdown under
`roadmap/deviations/` changed (this record and its index entry; `git diff --stat 64e8d158 HEAD` lists
no source or test file), so every line reference still points at `main@64e8d158`. In the same
revision task the maintainer approved two future product requirements, persistent offline-accessible
Overviews and Skyrim gameplay readiness; this record is where that approval is recorded, and no
roadmap stage owns them yet. Additional SDK persistence, Host, and Adapter source was read for them;
that reading is source inspection, not runtime validation.

**Baseline checks (observations, not behavior validation)**

| Check | Result |
| --- | --- |
| `dart analyze` (SDK) | No issues found |
| `dart test` (SDK) | 1213 tests passed, 12.7 s |
| `flutter analyze` (app) | No issues found |
| `flutter test` (app) | 3090 tests passed, 78.6 s |

**Inaccessible or unverified**

- `gh` is not authenticated, so PRs were checked through the unauthenticated REST API only. Closed
  PRs and review comments were not read.
- CodeGraph: `.codegraph/` contains only a `.gitignore`, so no index was available. All navigation
  was by direct file reading and search.
- No runtime run against a real Host or Skyrim. Findings that depend on runtime ordering are marked
  accordingly in Sections 6 and 7.
- Host, Adapter, protocol schemas, and `integration/` were read only where they bound the SDK/app
  contract. They were not audited.
- Visual fidelity to the approved prototype was not re-verified (the prototype is not in the
  repository).
- `app/lib/shared/theme/` (64 files) and the Windows lifecycle bridge were inventoried but not read
  line by line.

## 2. Executive summary

DovahLink's SDK/Flutter split is in good structural condition. The SDK is a coherent single client
engine with a documented composition root, a single owner for session state, a single inbound reader
with correlation, and generation-guarded teardown. The Flutter app consumes only the SDK's public
barrel, mirrors SDK values into Redux, and does not re-implement reconnect, authentication, pairing
sequencing, revision handling, or subscription recovery. It does issue its own redundant
desired-area requests on top of SDK restoration (ARCH-001, ARCH-004, ARCH-007); those are intent
requests, not a competing recovery engine. No Critical finding was confirmed.

**Verified strengths**

- Dependency direction is clean: the SDK imports no Flutter, Redux, or GetIt; the app never imports
  `package:dovahlink_client_sdk/src/`. Only 24 app files import the SDK barrel.
- `SessionState` is the sole owner of session-scoped facts, and privileged capabilities
  (`ISessionAdmissionService`, `ISessionTrustService`) are reachable only from the services that need
  them.
- Initial-retry and established-session recovery are SDK-owned and separate; the app only projects
  their status.
- Live gameplay values pass through Redux unchanged as `StateSynchronization<T>`; the app does not
  copy domain fields.
- App shutdown is idempotent and budgeted, and middleware subscriptions are cancelled on shutdown.
- Test volume is high and both baselines are green and static analysis clean.

**Problems found (7 confirmed, 5 suspected, 8 documentation discrepancies)**

- One High correctness-adjacent mismatch where the real SDK state sequence differs from what the
  app's tests and documentation assume (ARCH-001).
- One High unjustified layering chain in the pairing feature that the project's own conventions
  forbid (ARCH-002).
- Five Medium items: behavior living in the composition root, protocol and lifecycle logic in public
  facades, a duplicated rejection-policy rule, change amplification when adding a gameplay domain,
  and SDK API gaps that force app-side shadow state.

The large files (for example the 6.7k-line `dovahlink_client_test.dart`, 599-line composition root,
549-line `PairingService`) were treated as investigation candidates only. Size alone produced no
finding.

## 3. SDK architecture inventory

| Subsystem | Main types (under `lib/src/`) | Owns | Boundary notes |
| --- | --- | --- | --- |
| Composition root and public views | `dovahlink_client.dart` (`DovahLinkClient`); `dovahlink_hosts.dart`, `dovahlink_connections.dart`, `dovahlink_pairing.dart`, `dovahlink_current_host.dart`, `dovahlink_character.dart` | Wiring of one engine; four grouped views `hosts`, `connections`, `pairing`, `currentHost`; terminal `close()`; discovery reconciliation (see ARCH-003) | Public barrel `lib/dovahlink_client.dart` exports interfaces and typed values only |
| Session | `internal/session/`: `SessionService`, `SessionState`, `ConnectionTeardownCoordinator`, `LifecycleOperationQueue`, `SessionAdmissionService`, `SessionTrustService` | Transport lifecycle, connection state, session ID, trust state, Known Host relationship, generation counter, invalidation events | `SessionState` is the single owner; three late-bound callbacks (`onTeardown`, `onIncomingMessage`, `onOrdinaryTransportLoss`) break construction cycles |
| Requests | `internal/requests/`: `RequestService`, `MessageRouter`, `PendingOperationBookkeeping`, `PendingOperationTransmitter`, `UnsolicitedMessageHandler`, `ReplyValidator` | One inbound reader, correlation by `correlationId`, retry-safety and timeout policy, orphaned-operation retry | Only `MessageRouter` and `UnsolicitedMessageHandler` have interfaces among the supporting collaborators |
| Authentication | `internal/authentication/`: `AuthenticationService`, `ClientIdResolver`, `ClientIdCache` | `hello`, candidate and Known Host authentication, credential rejection recovery, Host identity checks, version compatibility check | 539 lines; combines handshake, persistence of refreshed Host metadata, and availability writes |
| Pairing | `internal/pairing/pairing_service.dart` | Challenge request, renotify, cancel, confirm, acknowledge, crash recovery of `CONFIRMING` | Composes authentication and initial retry through their contracts |
| Reconnect | `internal/reconnect/`: `ReconnectService`, `ReconnectRejectionClassifier` | Initial retry (3 s, unbounded until cancelled) and bounded established-session recovery | Drives `ISessionService` and `IAuthenticationService` only; never assigns connection state |
| Client state and persistence | `internal/persistence/ClientStateService`; `persistence/` (`IClientStorage`, `PersistedClientState`, DPAPI, unsupported storage) | Serialized load and save-before-publish of client ID, credentials, Known Hosts, pending pairing recovery | Windows storage behind `dovahlink_client_windows.dart` |
| Availability and discovery | `internal/availability/`: `HostAvailabilityService`, `KnownHostPresenceMonitor`; `host_presence_probe.dart`; `dovahlink_discovery_service.dart` | Runtime Known Host availability and session projection; sessionless presence probes; local discovery claims | Availability written by four collaborators (authentication, pairing, reconnect, presence monitor) (see ARCH-005) |
| State synchronization | `internal/state/`: `StateMessageHandler`, `StateDomainDefinition<T>`, `StateRevisionTracker<T>`, `StateRecoveryService<T>`, `SubscriptionService`, domain modules (`CharacterStateModule`, `GameTimeStateModule`, `PlayerLocationStateModule`, `TrackedQuestsStateModule`) | Desired-area intent, accepted-area gate, authority and play-context identity, revisions, Snapshot and Event application, recovery | Generic machinery plus one module per domain; recovery wired for Level only |
| Domain models | `state/`: eight public domain states, `StateSynchronization<T>` | Typed immutable values and sync metadata | Exported unchanged to the app |
| Protocol and transport | `protocol/` (DTOs, validators, generated `.g.dart`), `transport/websocket_transport.dart`, `internal/compatibility/` | Wire decoding, validation, Host version range `0.5.x` | Typed DTOs never cross into the app |
| Shared | `shared/enums.dart` (614 lines), `constants.dart`, `current_value_stream.dart`, `request_policy.dart` | Public typed enums and replaying stream primitive | `DovahLinkStateArea` lives in `enums.dart` |

## 4. Flutter architecture inventory

| Feature or area | Responsibility | Data origin | Semantic-state owner | SDK dependencies | Layers present |
| --- | --- | --- | --- | --- | --- |
| `features/connection` | Connections screen, Host cards, discovery dialog, Host selection | SDK `hosts.knownHostStatesChanges`, `pairing.candidates`, `pairing.discoverHosts()`, `connections.knownHostInvalidations` | SDK for Known Hosts, availability, session projection and candidates; app for selection and dialog state | `ConnectionMiddleware`, `HostMapper` | domain entities, presentation only |
| `features/pairing` | Pairing dialog and journey, code entry, countdowns | SDK `pairing.*`, `connections.initialConnectionRetryChanges`, `connections.stateChanges` | SDK for protocol facts; app for `PairingPhase`, typed digits, countdown clocks | `PairingRemoteDataSource`, `PairingMiddleware`, `PairingHandshakeModel`, `PairingFailureMapper` | data, domain (7 use cases, repository), presentation |
| `features/live_state` | Redux projection of eight gameplay domains | SDK `currentHost.*Changes` and `subscribeStateArea` | SDK (values and sync status are stored unchanged) | `LiveStateMiddleware`, `SessionLiveState` | presentation only |
| `features/session` | Session shell, navigation tabs, Overview | Redux (`live_state`, `connection`) | App presentation | `SessionOverviewViewModel` (SDK value types); `SessionShellMiddleware` (navigation) | presentation only |
| `features/device_identity` | Companion device-name override | `SharedPreferencesAsync`; passes name to SDK pairing | App owns the override; SDK owns `clientId` | `DeviceIdentityMiddleware` | data, domain, presentation |
| `features/appearance` | Theme preset selection | `SharedPreferencesAsync` | App | none | data, domain, presentation |
| `features/settings` | Settings dialog | Redux | App | none | presentation only |
| `shared/theme` | Tokens, materials, metrics, themed widgets (64 files) | n/a | App | none | shared |
| `shared/navigation`, `shared/state`, `shared/utils`, `app/`, `platform/windows` | Router, store composition, shutdown service, client holder, Windows lifecycle bridge | n/a | App | `AppShutdownService`, `ExistingDovahLinkClient` | shared |
| `injection_container.dart`, `main.dart` | GetIt registration; selects `DpapiClientStorage` or `UnsupportedClientStorage`; creates one lazy `DovahLinkClient` | n/a | App composition | `DovahLinkClient`, `IClientStorage` | n/a |

## 5. SDK/Flutter ownership matrix

| Capability | Authoritative owner | SDK responsibility | Flutter responsibility | Finding |
| --- | --- | --- | --- | --- |
| Skyrim game values | Host | Decode, cache, revision and recover; never fabricate | Mirror and format | None |
| Host-side trust, revocation, blocking | Host | Typed outcomes and invalidation events | Display only | None |
| Host discovery and candidate membership | SDK | `discoverHosts()`, reconciliation against Known Hosts | Mirror `candidates` stream; own selection | ARCH-003 (logic sits in the root) |
| Known Hosts, availability, session projection | SDK | `HostAvailabilityService`, presence monitor | Map to cards | ARCH-005 (rule spread) |
| Client identity, credentials, trust persistence | SDK | `ClientStateService`, `IClientStorage` | Selects storage at composition | None |
| Connect, authenticate, session admission | SDK | `AuthenticationService`, `SessionService` | Invoke, map results | ARCH-001, ARCH-004 |
| Initial retry and established-session recovery | SDK | `ReconnectService` | Project status only | ARCH-001 (transient `disconnected`) |
| Pairing protocol sequencing | SDK | `PairingService`, `DovahLinkPairing` | Invoke; own dialog phase, code digits, countdown clocks | ARCH-002 |
| Pairing UI lifecycle (`PairingPhase`) | Flutter | None | Reducer-driven presentation lifecycle | Suspected S-1 |
| Subscription intent and recovery | SDK | `SubscriptionService`, recovery services | Decides which areas the Overview wants | ARCH-004 (restoration triggers spread), ARCH-007 |
| Revision and identity tracking, stale suppression | SDK | `StateRevisionTracker`, `StateMessageHandler` | None | None |
| Domain state and typed streams | SDK | Models and modules | Redux mirror, unchanged | ARCH-006 |
| Typed failures and operation outcomes | SDK | Typed exceptions and enums | Map to user wording | ARCH-007 (invalidation classification) |
| Presentation state, wording, navigation, themes | Flutter | None | Everything | None |
| Device-name override | Flutter | Carries `displayName` in pairing and `renameDevice` | Persist and resolve label | None |
| App shutdown | Flutter | `DovahLinkClient.close()` | Budgeted orchestration | Suspected S-4 |

## 6. Confirmed findings

Severity follows the task's scale. Line references are to `main@64e8d158`. "Confirmed" means the
code on both sides of the claim was read; where runtime ordering matters, the supporting test is
named. No finding below proposes a security, pairing-semantics, protocol, or persistence change.

### ARCH-001: SDK publishes a transient `disconnected` on ordinary transport loss; the app and its docs assume it does not

- **Severity:** High. **Evidence:** Confirmed (code and SDK test); not exercised end to end.
- **Paths and symbols:** `sdk/dart/dovahlink_client/lib/src/internal/session/session_service.dart` `_beginRecoveryAfterOrdinaryTransportLoss` (434-454); `connection_teardown_coordinator.dart` `tearDown` (56-90); `session_state.dart` `resetAfterTeardown` (312-331) and `markReconnecting` (219); `app/lib/features/live_state/presentation/state/live_state.middleware.dart` `_connectionStateChanged` (136-157), `_endAdmittedSession` (317-330), `_ensureTrustedSession` (172-184).
- **Evidence:** Ordinary loss from `connected` tears down with `preserveReconnecting: true`, but `resetAfterTeardown` only preserves `reconnecting` when the session was already recovering, so the state goes `connected → disconnected`, and only a later queued step calls `markReconnecting`. The SDK test `dovahlink_client_test.dart:5730` asserts the sequence `connected, disconnected, reconnecting, reauthenticating, connected` and comments that this is intended. The app middleware treats every `disconnected` as the end of the admitted session: it cancels all eight listeners, clears `_requestedDesiredAreaStores`, and dispatches `SessionLiveStateResetAction`. The app test `live_state.middleware_test.dart:738` ("ordinary reconnect keeps projection listeners") feeds `reconnecting → reauthenticating → connected` and so never produces the `disconnected` the real SDK emits. `ai/context/flutter/architecture.md` (Live gameplay state) states that ordinary reconnect keeps those listeners attached.
- **SDK value reset (revision evidence):** Independently of the connection-state sequence, the SDK discards gameplay values on every teardown. The `onTeardown` closure (`dovahlink_client.dart:243-250`) calls `SubscriptionService.onSessionEnded` (`subscription_service.dart:229`), which bumps the session generation, clears the accepted set, and calls `StateMessageHandler.setSubscribedStateAreas({})`. That resets every previously accepted area through `resetToNotSubscribed` (`state_message_handler.dart:85`, `state_revision_tracker.dart:133`), and `StateSynchronization.notSubscribed()` carries `value: null` with no authority, play context, or revision (`state_synchronization.dart:39-44`). Desired intent survives (`onSessionEnded` does not touch `_desiredStateAreas`); values do not.
- **Current behavior:** After a real transport drop the app tears down and re-attaches its listeners, clears the Overview slice, and re-issues eight sequential `subscribeStateArea` calls on top of the SDK's own `restoreDesiredStateAreas`. Even without the app reset, every area the Host had accepted would already have been reset by the SDK to `notSubscribed` with no value.
- **Why problematic:** A consumer cannot tell a final disconnect from the first step of recovery. Documented behavior, test fake, and real SDK disagree. The documented "stale retained values across reconnect" (`ai/context/flutter/architecture.md`, Live gameplay state) is not what either side does for transport loss.
- **Consequence if unchanged:** Redundant subscribe traffic and a visible Overview reset on every drop; any new consumer must rediscover the transient state; tests keep passing against a state sequence production never emits.
- **Four separate concerns.** This finding is only the first. They must not be merged into one change, and fixing an earlier one does not deliver a later one:
  1. *Connection-lifecycle correctness* — the public `DovahLinkConnectionState` sequence on ordinary loss and how consumers interpret it. This is ARCH-001's defect.
  2. *In-memory gameplay retention* — whether a domain's last accepted value stays visible, explicitly marked non-current, while the SDK recovers. Today it does not (the value reset above). Correcting item 1 alone leaves the Overview empty during every reconnect, because the trackers are already reset to `notSubscribed`.
  3. *Durable historical snapshots* — last-known values that survive application or computer restart. Not implemented and not an architecture defect; a future requirement approved in this revision (Section 1).
  4. *Offline Overview presentation* — showing historical values when no session exists. Not implemented; an approved future requirement.
- **Recommended correction (item 1 only):** A maintainer decision is needed first because the connection-state sequence is public SDK behavior: either (a) the SDK stops publishing `disconnected` between `connected` and `reconnecting` when recovery will follow, or (b) the documented contract is changed to say the transient is part of the API and the app handles it. Either way the app test must be driven by the real sequence (for example an SDK-level contract fixture), and the app must end an admitted observation only on a terminal state. The `ai/context/flutter/architecture.md` sentence about retained values is corrected to match actual SDK behavior in the same change, not left implying item 2.
- **Compatibility with later work:** Item 2 is a separate SDK decision (what `status`, `value`, `playContextId`, and `revision` a tracker exposes between teardown and the next accepted baseline). Whatever it decides must keep values from the ended session distinguishable from newly synchronized ones and must never assign them a fabricated revision or identity. Items 3 and 4 must not be implemented inside the reconnect fix: durable persistence belongs to a separate SDK capability that reads from the live trackers and never writes into them.
- **Risk of changing:** Medium. Touches `SessionState` transitions that `KnownHostSessionState`, pairing status, and presence evidence also read. Protected by `session_state_test.dart`, `session_service_test.dart`, and `dovahlink_client_test.dart`.
- **Protecting tests:** SDK `dovahlink_client_test.dart:5730`; app `live_state.middleware_test.dart:738`, which currently does not cover the real path.
- **SAS:** Independent of SAS. Safe before SAS.
- **Follow-up PR:** T1 in Section 9.

### ARCH-002: Pairing feature keeps a forwarding-only UseCase/Repository/Model chain over an SDK that already owns the operation

- **Severity:** High (maintainability and rule conflict). **Evidence:** Confirmed.
- **Paths and symbols:** `app/lib/features/pairing/domain/usecases/*.usecase.dart` (7), `data/repositories/pairing.repository.dart`, `domain/repositories/pairing_repository.dart`, `data/models/pairing_handshake.model.dart`, `domain/entities/pairing_handshake.entity.dart`, `pairing.injection_container.dart`.
- **Evidence:** `PairingRepository` forwards each of its seven members to `IPairingRemoteDataSource` unchanged; each use case forwards one repository call (for example `AuthenticateUseCase.call` → `_repository.authenticate`). `PairingHandshakeModel` extends `PairingHandshake` and maps an already-decoded `DovahLinkPairingHandshake`; no external representation crosses a boundary. `ai/context/flutter/architecture.md` ("Feature call chain" and "Domain and presentation values") forbids a layer whose only job is forwarding an SDK operation or copying fields from a typed SDK result. `ai/context/sdk/api-design.md:76-78` acknowledges the layers and says to remove one only after its behavior has moved.
- **Current behavior:** Middleware resolves a use case through `sl`, the use case calls the repository, which calls the data source, which calls `DovahLinkClient.pairing`. The data source does real work: exception to `Failure` mapping, credential-rejection wording, and the `connectionStatus` mapping.
- **Why problematic:** Ten extra types (seven use cases, the repository and its interface, the Model) plus nine test files carry no decision. Each new pairing operation, including SAS 5A.1/5A.10 states, must be threaded through all of them.
- **Consequence if unchanged:** Every SAS-era pairing change pays a multi-file tax and the repository's own rule stays violated.
- **Recommended correction:** Keep the SDK-to-app mapping (failure mapper, handshake mapping, status mapping) in one app boundary and let `PairingMiddleware` call it directly, per the conventions. Do not delete failure mapping or the Redux flow.
- **Risk of changing:** Low to medium. Behavior-preserving; the risk is in middleware tests that stub use cases.
- **Protecting tests:** `pairing_remote.datasource_test.dart`, `pairing.middleware_test.dart`, `pairing.repository_test.dart` and seven `*.usecase_test.dart` (these would be removed with their types).
- **SAS:** Overlaps 5A.1 and 5A.10 (Flutter pairing presentation). Coordinate: do it immediately before 5A.1, or fold into it.
- **Follow-up PR:** T2.

### ARCH-003: The composition root contains discovery and credential-cleanup behavior

- **Severity:** Medium. **Evidence:** Confirmed.
- **Paths and symbols:** `dovahlink_client.dart` `_discoverHosts` (369-417), `_candidateHostsChanges` (420-431), `_observeCandidateKnownHosts` (454-476), `_reconcileCandidateHosts` (480-506), `_handleKnownHostInvalidation` (435-451), fields at 57-83, 314, and the `candidateKnownHostsObservation` constructor parameter.
- **Evidence:** About 150 lines and six mutable fields implement candidate reconciliation, generation guarding, and invalidation cleanup directly in `DovahLinkClient`. `ai/context/sdk/architecture.md` says exactly one place constructs the graph and gives every behavior-bearing class an interface. A test-only `List<bool>? candidateKnownHostsObservation` recorder is threaded through production constructors to observe this logic (`dovahlink_client_test.dart:1453`). The root also keeps `_sessionState` as a field, which the same document says it never does.
- **Why problematic:** The only place to test this logic is the full client. It cannot be reused or mocked, and it hides a service-shaped owner (discovery reconciliation) outside the documented service list.
- **Consequence if unchanged:** Discovery work for LAN/mDNS (Stages 10, 22) grows the root further.
- **Recommended correction:** Extract a discovery-reconciliation collaborator with its own contract, move the invalidation handler to the owner of credential state, and delete the test hook. Public API unchanged.
- **Risk of changing:** Medium (race suppression and storage-error behavior are subtle). Protected by the `discoverHosts` group in `dovahlink_client_test.dart` and `dovahlink_discovery_service_test.dart`.
- **SAS:** Better after 5A.7/5A.8 only if those touch discovery; otherwise independent.
- **Follow-up PR:** T3.

### ARCH-004: Public facades carry protocol and lifecycle logic

- **Severity:** Medium. **Evidence:** Confirmed.
- **Paths and symbols:** `dovahlink_connections.dart` `renameDevice` (154-179) and `disconnect` (183-189); `dovahlink_client.dart` `close` (513-542); `dovahlink_pairing.dart` four `restoreDesiredStateAreas` calls (123, 137, 163, 172); `session_admission_service.dart:66-68`; `session_trust_service.dart` `markTrusted`; `pairing_service.dart` `_authenticate` (167-186), `acknowledgeTrustedCredential` (405-501, `markTrusted` at 494), `recoverPendingPairing` (505-548); app `live_state.middleware.dart` `_sessionTrusted` (161-167, on `PairingSessionTrustedAction` at 84).
- **Evidence:** `renameDevice` builds the request, applies `RequestPolicy`, decodes the reply, and reports protocol violations inside the facade. The four-step cancel sequence (`cancelPendingAuthentication`, `stopInitialConnectionRetry`, `stopRecovery`, `clearDesiredStateAreas`) is duplicated in `disconnect` and `close`, and `connectWithInitialRetry` repeats a subset. `api-design.md` says these groups are views that do not own behavior.
- **Restoration evidence (revised).** A session becomes trusted through exactly two SDK paths, and both are legitimate restoration triggers:
  - *Trusted admission.* `SessionAdmissionService.admitSession` restores desired areas only when the admitted `trustState` is `trusted` (`session_admission_service.dart:66-68`). This covers trusted initial admission of a Known Host and ordinary reconnect, because `ReconnectService` re-authenticates through `AuthenticationService`, which admits through this service.
  - *Trust upgrade of an existing session.* A session admitted `unpaired` never passes the admission check. It becomes trusted later only through `ISessionTrustService.markTrusted()`, which only `PairingService.acknowledgeTrustedCredential` calls (`pairing_service.dart:494`), and only while the acknowledged session is still current. That path is reached from successful code confirmation (`confirmPairingCodeAndAcknowledge`, 400) and from pending-pairing recovery (`recoverPendingPairing`, 525), which `_authenticate` invokes for every `unpaired` hello (174-177); `recoverPendingPairing` itself returns `unpaired` unless a pending `CONFIRMING` record exists for the same Host.
  - The four facade calls therefore are not duplicates of admission: `authenticateCandidate` and `authenticateKnownHost` restore when `hello` was `unpaired` but recovery produced `trusted`; `confirmCode` restores after confirmation; `recoverPendingPairing` restores when recovery returns `trusted`. Removing them while keeping admission as the only trigger would leave a freshly paired session with no restored subscriptions.
  - The facade placement has two weaknesses. `confirmCode` restores unconditionally, even when `acknowledgeTrustedCredential` skipped `markTrusted` because the session changed; the request then fails its `requiredTrustState: trusted` policy, so this is harmless today but shows the decision sits in the wrong layer. And Flutter adds a third trigger: `_sessionTrusted` reacts to the app's own `PairingSessionTrustedAction` and runs `_ensureTrustedSession`, which issues its own `subscribeStateArea` sequence.
- **Why problematic:** Anything that ends a session must remember the sequence; a fifth caller will miss a step. Restoration policy is spread across an internal service, a public facade, and the app, so no single owner answers "when does a session that became trusted restore its desired areas?"
- **Recommended correction:** Give deliberate-disconnect and rename each a service owner. Move restoration policy into one SDK-internal owner that reacts to *the session becoming trusted*, from either path: trusted admission or a successful `markTrusted` on the current session. The five cases that must keep restoring are: trusted initial admission, ordinary reconnect, trust upgrade of an existing unpaired session, successful pairing confirmation, and pending-pairing recovery. The facade calls are removed only after this owner covers all five, each with its own test, and the owner must not restore for an `unpaired` admission, for a stale session, or after administrative invalidation (desired areas stay dormant until an explicit retry, `api-design.md` "Subscription intent versus mechanics"). The app's `_sessionTrusted` subscribe sequence is then removed with T6's removal of app-side subscription requests, so Flutter only expresses intent and observes streams. No restoration trigger is eliminated; each moves to one owner.
- **Risk of changing:** Medium: ordering of cancellation matters. Protected by `dovahlink_connections_test.dart`, `dovahlink_pairing_test.dart`, `dovahlink_client_test.dart`.
- **SAS:** Better after 5A.7/5A.8 (they extend pairing operations).
- **Follow-up PR:** T3 (shared with ARCH-003).

### ARCH-005: Rejection-to-`pairingRequired` and availability rules are written in several places

- **Severity:** Medium. **Evidence:** Confirmed.
- **Paths and symbols:** `dovahlink_client.dart:439-443` (administrative reason to flag), `authentication_service.dart:444` and `reconnect_service.dart:393` (`!= blocked`), `reconnect_service.dart:372-426` (offline versus unknown mapping), `known_host_presence_monitor.dart:281-307`, `authentication_service.dart:385, 477`, `pairing_service.dart:368, 496`.
- **Evidence:** The rule "revoked/unrecognized/trustReset/factoryReset need pairing, blocked does not" is expressed three times in three vocabularies. `setAvailability` has four writers outside the owner, each deciding what evidence means (`offline` on connection failure, `unknown` on protocol failure, `online` after admission).
- **Why problematic:** A new rejection reason or evidence source must be added in each location; divergence would show up only as a wrong card state.
- **Recommended correction:** One policy object with its own contract maps rejection or invalidation evidence to `(pairingRequired, availability)`.
- **Risk of changing:** Low to medium; no behavior change intended. Protected by the three services' unit tests and `known_host_presence_monitor_test.dart`.
- **SAS:** Better after SAS (SAS adds new rejection and trust outcomes). Coordinate with 5A.7.
- **Follow-up PR:** T4.

### ARCH-006: Adding a gameplay domain touches too many places on both sides

- **Severity:** Medium. **Evidence:** Confirmed.
- **Paths and symbols (SDK):** `game_time_state_module.dart`, `player_location_state_module.dart`, `tracked_quests_state_module.dart` (identical 48-line shape), `character_state_module.dart`, `dovahlink_current_host.dart` (one constructor field, getter, and interface member per non-Character domain), `dovahlink_client.dart:158-172, 210-217, 292-299`, `shared/enums.dart` (`DovahLinkStateArea`), `lib/dovahlink_client.dart` exports.
- **Paths and symbols (app):** `live_state.middleware.dart` (`_requiredAreas`, eight `_observe` calls), `live_state.actions.dart`, `session_live_state.state.dart`, `live_state.reducer.dart`, `live_state.selectors.dart`, `session_overview.viewmodel.dart`.
- **Evidence:** `ai/context/sdk/architecture.md` ("State synchronization composition") says root facades must not gain one field or constructor dependency for every state area; Game Time, Location, and Tracked Quests each did. Recovery is hand-wired for Level only (`dovahlink_client.dart:210-216`, plus an `ICharacterStateModule.levelDomain` getter that exists only for that), so a future Event-mode domain can silently miss recovery.
- **Existing mechanism (revised).** The registration the original recommendation asked for already exists. `StateDomainDefinition<T>` (`internal/state/state_domain_definition.dart`) already binds one area's name, decoder, availability rule, tracker, and Event support, and `StateMessageHandler` already dispatches by definition without branching on area names (`state_message_handler.dart`). `StateRecoveryService<T>` already takes a definition. The gaps are narrower:
  - Event support is a private constructor flag (`_supportsEvents`), not readable through `IStateDomainDefinition<T>`, so the root cannot derive which definitions need recovery and wires Level by name. Level is the only Event domain today (`character_state_module.dart:124`).
  - `GameTimeStateModule`, `PlayerLocationStateModule`, and `TrackedQuestsStateModule` repeat the same single-definition module shape (47-48 lines each), each with its own interface.
  - `IDovahLinkCurrentHost` gains one constructor parameter, field, and getter per non-Character domain.
- **Why problematic:** Every new domain (map, inventory, equipment in the roadmap) repeats ~10 edits per side with no compile-time reminder to wire recovery.
- **Recommended correction:** Extend and consolidate the existing mechanism; do not add a second registration system. (1) Make Event support readable on `IStateDomainDefinition<T>` and have the composition root create one `StateRecoveryService<T>` for every registered Event-capable definition, removing `levelDomain`. (2) Collapse the three identical single-definition modules onto one generic internal module over `StateDomainDefinition<T>`, keeping a distinct typed definition per domain. (3) Stop the per-domain growth of `currentHost` for *future* domains by grouping them the way `currentHost.character` already groups Character domains; existing public getters (`playerLocationChanges`, `gameTimeChanges`, `trackedQuestsChanges`, the `character` group) stay unchanged. Public typed `Stream<StateSynchronization<T>>` streams and exported models are preserved, and no global stream is created. App-side repetition is presentation-owned and should be handled when the next domain is actually added.
- **Relevance to future capabilities:** The same set of registered definitions is the natural single source for any later SDK capability that must enumerate domains (for example a historical-snapshot capability that records each domain's last accepted value). That capability would read the definitions and trackers; it must not register a parallel definition list or apply its data back through the trackers.
- **Risk of changing:** Medium. Protected by the module tests and `state_*` tests.
- **SAS:** Independent of SAS.
- **Follow-up PR:** T5.

### ARCH-007: SDK API gaps force app-side shadow state and failure classification

- **Severity:** Medium. **Evidence:** Confirmed.
- **Paths and symbols:** `live_state.middleware.dart` `_requestedDesiredAreaStores` (68), `_requestRequiredAreas` (288-313); `pairing_remote.datasource.dart:85-88`; `ISubscriptionService.desiredStateAreas` (not on `IDovahLinkCurrentHost`).
- **Evidence:** The SDK has only per-area `subscribeStateArea`, each sending the complete desired set, so the app issues eight sequential round trips and keeps its own flag recording whether it already requested the set. `api-design.md` says the app must not maintain competing subscription truth. The data source reads `connections.state == administrativelyInvalidated` after catching a generic `DovahLinkConnectionException` to detect invalidation, instead of receiving a typed failure.
- **Why problematic:** Another Dart consumer would have to copy both patterns, which fails the task's ownership test.
- **Recommended correction:** SDK additions (a multi-area subscribe and a readable desired set, a typed invalidation failure) are public API changes and need maintainer approval before implementation.
- **Risk of changing:** Low to medium. Additive.
- **SAS:** Coordinate with 5A.7 (public SDK contract work).
- **Follow-up PR:** T6.

## 7. Suspected findings

| ID | Concern | What remains to inspect or reproduce |
| --- | --- | --- |
| S-1 | The app maps SDK `connected` to `PairingConnectionStatus.restored`, and `pairingConnectionRestoredReducer` (`pairing.reducer.dart:295`) then sets `PairingPhase.trusted` without checking SDK trust state. Evidence: `pairing_remote.datasource.dart:265`. | Reproduce recovery of a Known Host whose credential was cleared while the session still reaches `connected` unpaired. `ReconnectService` accepts any `connected` result (`reconnect_service.dart:360-371`). Medium if real. |
| S-2 | Reducers call `DateTime.now()` to turn `expiresInSeconds` and `retryAfterSeconds` into deadlines (`pairing.reducer.dart:130, 224, 240`), making them impure; Flutter docs assign challenge expiry to the SDK. | Decide whether the SDK result should carry an observation instant or deadline; check timer-based tests for flakiness. Low. |
| S-3 | A first initial-retry attempt that is superseded (`connectWithInitialRetry` at the start of a second call) may complete after admitting a session and then throw `_initialRetryCancelled` without disconnecting (`reconnect_service.dart:160-165`). | Write a throwaway test with two overlapping calls; `cancelPendingAuthentication` at line 157 may already prevent it. Medium if real. |
| S-4 | `StateRecoveryService.start()` subscription is never cancelled and tracker/`CurrentValueStream` controllers are never closed in `DovahLinkClient.close()`, so consumer state streams never complete (`dovahlink_client.dart:513-542`, `current_value_stream.dart`). | Check whether any leak or hang is observable in the app shutdown budget. Low. |
| S-5 | `connectionKnownHostChangedReducer` and `connectionCandidatesChangedReducer` duplicate the candidate-to-known selection conversion (`connection.reducer.dart`). | Confirm the two copies cannot diverge for the same inputs; consider one shared helper. Low. |

## 8. Documentation discrepancies

Source documents were not edited. Each row proposes a correction for maintainer review.

| ID | Class | Document and location | Discrepancy | Proposed correction |
| --- | --- | --- | --- | --- |
| D-1 | Dangling reference | `ARCHITECTURE.md:155,227`; `ai/context/host/architecture.md:315`; `ai/context/protocol/compatibility.md:10,117`; `roadmap/10-multi-instance-and-local-discovery-foundation.md:22`; `PLAN.md:15-16` | They link into `plans/documentation-and-composition-normalization/` and `plans/stage-*`, which are absent on `main`. | Re-home the normative content (public vocabulary and identity semantics) into a durable document and update the links. |
| D-2 | Documentation says X, code does Y | `ai/context/sdk/architecture.md` "Internal composition" | States `ConnectionTeardownCoordinator` and `PendingOperationTransmitter` have their own contract; only `MessageRouter` does (`IMessageRouter`). `PendingOperationBookkeeping`, `LifecycleOperationQueue`, `ClientIdResolver`, `ClientIdCache` also have none. | Either add the contracts or record these as accepted pre-existing exceptions under the "phase-forward" rule. |
| D-3 | Outdated description | Same section: "nine major Services" | `HostAvailabilityService`, `KnownHostPresenceMonitor`, and discovery are behavior-bearing and not in the list. | List them or define the category. |
| D-4 | Documentation says X, code does Y | Same document, "Session-state ownership" | Says the root never keeps `SessionState` as a field; `dovahlink_client.dart:314` does. Also says Authentication's only caches are `clientId`/`hostVersion`; code also caches `_lastHelloResult`. | Fix the code (ARCH-003) or the text. |
| D-5 | Contradiction with behavior | `ai/context/flutter/architecture.md` "Live gameplay state" | Says ordinary reconnect keeps listeners attached and that stale retained values stay distinguishable; real SDK emits a transient `disconnected` and resets every accepted area to `notSubscribed` with no value on teardown (ARCH-001). | Resolve the listener sentence with ARCH-001's decision; describe value retention only as the SDK actually implements it. |
| D-6 | Ambiguous ownership | `flutter/architecture.md` "Feature call chain" versus `sdk/api-design.md:76-78` | One forbids forwarding layers, the other keeps them with no exit criterion. | Record the removal condition and owner (ARCH-002). |
| D-7 | Outdated description | `ARCHITECTURE.md:17` | Says the SDK "is added when the Dart Client SDK Foundation phase begins" and points at planned status; Stage 5 is complete. | Update to the current state. |
| D-8 | Ambiguous ownership | `flutter/architecture.md` ownership table | Assigns pairing "expiry, attempts, cooldown" to the SDK, but the SDK returns relative seconds and Flutter computes deadlines (S-2). | Clarify which side owns the clock. |

## 9. Architecture improvement backlog

Ordered by the task's priority list (correctness first, then ownership, change amplification, SAS
overlap, isolation). File counts are estimates of tracked changed files, including tests and
documentation; all are below the 80-file limit and none needs splitting. Every task needs an explicit
maintainer instruction naming its scope (`AGENTS.md`), and any task marked "public SDK change" needs
maintainer approval of that contract change before implementation.

Hypothesis areas not carried forward: **Screens, widgets and shared UI** has no justified task. The
Session Overview widgets and dialogs were reviewed for orchestration mixed into layout and for
widget-local state; the only local state found (timers and text controllers in `pairing_countdown`,
`device_name_editor`, `pairing_code_form`) is legitimately local. The 64-file theme area was not read
line by line (Section 1), so this is "no evidence of a problem", not a clean bill. **Flutter
infrastructure and lifecycle** is folded into T3 and T7 because its only item is suspected S-4.
**Flutter connection and pairing presentation** and **live-state and session architecture** reduce to
T2 and the app half of T1.

| Order | Task | Findings | Priority | SAS overlap |
| --- | --- | --- | --- | --- |
| T1 | Resolve the reconnect state sequence | ARCH-001 | High | Independent of SAS |
| T2 | Pairing feature: call the SDK boundary directly | ARCH-002, S-1, S-2 | High | Coordinate with 5A.1 and 5A.10 |
| T3 | SDK composition root and facade responsibilities | ARCH-003, ARCH-004, S-3, S-4 | Medium | Better after 5A.7/5A.8 |
| T4 | Single owner for rejection and availability policy | ARCH-005 | Medium | Better after SAS |
| T5 | Consolidate the existing state-domain definitions | ARCH-006 | Medium | Independent of SAS |
| T6 | SDK subscription and typed-failure API additions | ARCH-007 | Medium | Coordinate with 5A.7 (public SDK change) |
| T7 | Documentation corrections and guardrails | D-1..D-8, S-5 | Low | Independent of SAS |

### T1. Resolve the reconnect state sequence

- **Branch:** `fix/reconnect-state-sequence`
- **Scope:** Connection-lifecycle correctness only (ARCH-001 concern 1). The maintainer decides between the two options in ARCH-001. Implement the chosen one: either the SDK suppresses the transient `disconnected` when recovery will follow, or the documented contract and the app's `LiveStateMiddleware` are updated to treat it explicitly. The app ends an admitted observation only on a terminal state. Replace the app test's hand-built sequence with one derived from the real SDK sequence, and correct the matching sentences in `ai/context/flutter/architecture.md` so they describe value behavior as the SDK actually implements it.
- **Dependencies:** none. **Public SDK change:** yes if option (a).
- **Non-goals:** changing reconnect timing, retry budgets, subscription semantics, or any security behavior; adding new states; retaining gameplay values across teardown (concern 2, a separate decision); durable snapshots or offline presentation (concerns 3-4).
- **Benefit:** the documented, tested, and real behavior agree; no redundant app-side subscribe traffic or app-initiated Overview reset on drops. The Overview still shows no values during recovery until concern 2 is decided, because the SDK trackers reset to `notSubscribed`; this task must not claim otherwise.
- **Estimated files:** 10-14 (3 SDK source, 4 SDK tests, 1-2 app source, 1-2 app tests, 2 docs).
- **Regression risks:** `KnownHostSessionState` and pairing connection-status mapping read the same transitions; presence monitor evidence.
- **Acceptance criteria:** a test asserts the real emitted sequence on ordinary loss and the app reacts exactly as documented; SDK and app analyze and test pass; no change to `PendingOperation` retry behavior.

### T2. Pairing feature: call the SDK boundary directly

- **Branch:** `refactor/pairing-direct-sdk-boundary`
- **Scope:** Remove the seven pairing use cases, `IPairingRepository`/`PairingRepository`, and the `PairingHandshakeModel`/entity pass-through. Keep failure mapping, handshake mapping, and connection-status mapping in one app boundary. Resolve S-1 and S-2 in the same branch if the decisions are quick; otherwise split them out.
- **Dependencies:** best done before or inside 5A.1.
- **Non-goals:** changing pairing UX, copy, phases, or SDK calls; touching the device-identity or appearance layers (their repositories do real persistence).
- **Benefit:** removes ten forwarding types and nine tests; each later SAS state is one change, not five.
- **Estimated files:** 28-38 (about 20 deletions, edits to middleware, DI, mapper and their tests, 2 docs).
- **Regression risks:** middleware tests that stub use cases; `sl` registration order; shutdown behavior.
- **Acceptance criteria:** no forwarding-only type remains; behavior tests for authenticate, confirm, renotify, cancel, disconnect, status mapping, and shutdown pass with unchanged intent; `flutter analyze` is clean.

### T3. SDK composition root and facade responsibilities

- **Branch:** `refactor/sdk-root-and-facade-ownership`
- **Scope:** Move discovery reconciliation and Known Host invalidation cleanup out of `DovahLinkClient` into contracted collaborators; give deliberate disconnect and rename a service owner; move subscription-restoration policy into one SDK-internal owner keyed on the session becoming trusted (trusted admission or a successful `markTrusted` on the current session), then remove the four `DovahLinkPairing` restore calls; delete the test-only observation hook; verify S-3 and S-4 with throwaway tests and fix only if confirmed.
- **Dependencies:** none, but sequence after 5A.7/5A.8 if those restructure pairing.
- **Non-goals:** new public API, a second client engine, changes to the three documented callbacks.
- **Benefit:** the root only wires; discovery logic becomes independently testable.
- **Estimated files:** 20-30.
- **Regression risks:** discovery race suppression, storage-error propagation, cancellation order on close.
- **Acceptance criteria:** the root holds no behavior or mutable discovery fields; all existing `discoverHosts`, `close`, and disconnect tests pass; no public export changes; one restoration test exists per trigger (trusted initial admission, ordinary reconnect, trust upgrade of an existing unpaired session, successful pairing confirmation, pending-pairing recovery) plus negative tests showing no restoration for an `unpaired` admission, a superseded session, or after administrative invalidation.

### T4. Single owner for rejection and availability policy

- **Branch:** `refactor/sdk-rejection-availability-policy`
- **Scope:** One contracted policy maps rejection or invalidation evidence to `pairingRequired` and availability, used by the root, authentication, reconnect, pairing, and the presence monitor.
- **Dependencies:** after SAS phases that add rejection outcomes (5A.7).
- **Non-goals:** changing which outcomes require pairing or what availability each yields.
- **Benefit:** one place to extend when trust outcomes grow.
- **Estimated files:** 12-18.
- **Regression risks:** subtle availability transitions during recovery.
- **Acceptance criteria:** table-driven policy tests reproduce today's mapping exactly; all callers delegate.

### T5. Consolidate the existing state-domain definitions

- **Branch:** `refactor/sdk-state-domain-definitions`
- **Scope:** Extend `IStateDomainDefinition<T>` so Event support is readable; derive one `StateRecoveryService<T>` per registered Event-capable definition and remove `ICharacterStateModule.levelDomain`; collapse the three identical single-definition modules onto one generic internal module over `StateDomainDefinition<T>`; record in `ai/context/sdk/architecture.md` how future domains are grouped under `currentHost` without one root field each. No new registration type.
- **Dependencies:** none; do before the next gameplay domain (Stage 15 onward) and before any SDK capability that enumerates domains.
- **Non-goals:** a second registration system, a global state stream, merging domains, changing existing public getters or exported models, protocol changes, app-side Redux changes.
- **Benefit:** a new domain becomes a definition plus a model; Event domains cannot miss recovery.
- **Estimated files:** 15-25.
- **Regression risks:** gate and baseline behavior in `StateMessageHandler` and `SubscriptionService`; recovery start order relative to `RequestService`.
- **Acceptance criteria:** existing state, module, recovery, and subscription tests pass unchanged; `lib/dovahlink_client.dart` exports are unchanged; a test registers a stub Event-capable definition and observes recovery wired without touching `DovahLinkClient`.

### T6. SDK subscription and typed-failure API additions

- **Branch:** `feature/sdk-subscription-and-failure-api`
- **Scope:** Add a multi-area subscribe, a readable desired set, and a typed administrative-invalidation failure; remove the app's `_requestedDesiredAreaStores`, its `_sessionTrusted` subscribe sequence (once T3's SDK restoration owner covers trust upgrades), and the connection-state probe.
- **Dependencies:** maintainer approval of the public contract; coordinate with 5A.7.
- **Non-goals:** protocol or Host changes; changing intent-retention semantics.
- **Benefit:** another Dart consumer gets the same capability without copying app logic.
- **Estimated files:** 15-25.
- **Regression risks:** intent clearing on disconnect; duplicate subscribes.
- **Acceptance criteria:** the app issues one subscribe per admitted session; failure classification reads a typed value; SDK and app tests pass.

### T7. Documentation corrections and guardrails

- **Branch:** `docs/architecture-guardrails`
- **Scope:** Apply the corrections in Section 8 after the maintainer decides each; re-home the content the dangling `plans/` links refer to; add a reviewable check for forwarding-only layers or app imports of SDK `src/` if the maintainer wants one. Include S-5 only if still relevant.
- **Dependencies:** after T1 (D-5) and T2 (D-6) so the text matches the code.
- **Non-goals:** editing code or rules the maintainer has not approved.
- **Benefit:** governing documents match the code.
- **Estimated files:** 10-14.
- **Regression risks:** none to runtime.
- **Acceptance criteria:** no governing document links to a missing path; each discrepancy is resolved or recorded as accepted.

## 10. Single recommended next task

**T1. Resolve the reconnect state sequence** (`fix/reconnect-state-sequence`).

It is the only item where the documented behavior, the test fake, and the real SDK disagree, and it
affects every transport drop for a connected player. It is independent of SAS, so it can land at any
time, and its first step is a maintainer decision between two options rather than a speculative
refactor. Everything else in the backlog is structural improvement with no demonstrated
user-visible effect today.

The audit found no Critical issue and no ownership violation that lets Flutter implement reconnect,
authentication, pairing sequencing, revision handling, or subscription recovery on its own. The
SDK/Flutter split is sound; the remaining work is targeted, not a rewrite.

## 11. Future Product Requirements — Persistent Overview and Gameplay Readiness

The maintainer approved these two requirements during the audit revision (Section 1). They are
product requirements, not architecture defects: their absence is never-implemented scope, and no
finding in Section 6 is raised because of them. No roadmap stage owns them yet; placing them is a
maintainer decision (11.11). "Current" statements below come from source inspection at
`main@64e8d158`, not from a runtime run.

### 11.1 Approved product intent

**Persistent Overview.** DovahLink stays useful when Skyrim is closed. After the client has received
gameplay information, the SDK can keep a trustworthy last-known snapshot, so the player can:

- view live information while connected;
- lose the connection without immediately losing what is displayed, with an explicit disconnected or
  reconnecting indication;
- close Skyrim, or close and reopen DovahLink, and still view the last-known Overview;
- open a Known Host's last-known Overview while that Host is offline;
- reconnect and receive new authoritative information without confusing it with historical data.

The existing Overview design is retained; this is not a visual redesign.

**Gameplay readiness.** A running DovahLink Host is not necessarily a loaded Skyrim gameplay
session. DovahLink must not show the player as gameplay-connected, or offer pairing as ready, when
the Skyrim functionality those need is unavailable. An authenticated transport session may
legitimately exist at the main menu, and authentication is not redesigned to require a loaded save.

### 11.2 Current implementation gaps

| Area | Current implementation (evidence) | Gap |
| --- | --- | --- |
| Values during reconnect | Teardown resets every accepted area to `notSubscribed` with `value: null` (ARCH-001) | Nothing to show while recovering |
| Durable gameplay history | `PersistedClientState` holds only `clientId`, Known Hosts with credentials and the `pairingRequired` hint, and pending pairing recovery (`persistence/persisted_client_state.dart`) | No historical storage or API |
| Session Shell without a session | The `/session/:hostId` route redirects home unless that Known Host's `sessionState` is `connected` (`app/lib/shared/navigation/app_router.dart:25-38`); `SessionShellMiddleware` enters only through `_enterWhenConnected` and `_connectionHostReentryRequested`, both gated on a connected Known Host | No read-only historical mode |
| Host-scoped projection | `SessionLiveState` is one global Redux slice (`session_live_state.state.dart:16`), reset by `_endAdmittedSession` | No per-Host historical presentation source |
| Host offline when Skyrim closes | The Adapter launches the Host and asks it to shut down when Skyrim closes (`ARCHITECTURE.md`, "Host and native adapter"); the SDK presence monitor projects `DovahLinkHostAvailability` (`unknown`, `online`, `offline`, `checking`) | Supported by design; not runtime-verified here |
| Play-context signals | Adapter: `kPreLoadGame` sends play-context-ended (`adapter/plugin/dovahlink_adapter_plugin.cpp:435-436`), a main-menu open sends it too (`commonlib_adapter_main_menu_sink.cpp:17-25`), and `kNewGame`/`kPostLoadGame` send a fresh ID for every load (443-446). Host: `PlayContextTracker` records it (`AdapterIpcSession.cs:344-359`); every envelope carries `playContextId`, `null` outside an active play context (`protocol/schema/README.md:29`) | A wire signal of play context exists; not a readiness signal |
| Play context on Adapter loss | The only `ClearCurrent` caller is the Adapter's play-context-ended message (`AdapterIpcSession.cs:359`); Adapter disconnect rotates `stateAuthorityId` but does not clear the play context | A non-null `playContextId` does not prove gameplay is available |
| Host-internal readiness | `AdapterAvailabilityTracker` tracks availability and `NeedsResynchronization`, re-armed on every play-context transition (`RearmResynchronizationForPlayContextTransition`); ordinary sampling is gated on it (`LiveStateScheduler.cs:238`) | Host-internal only; no public readiness fact |
| SDK readiness surface | `IDovahLinkCurrentHost` exposes `host`, `trustState`, `sessionId`, and domain streams; `playContextId` appears only inside each `StateSynchronization<T>` | No typed readiness capability |
| Pairing availability | The Host reports `pairing_status` `unavailable` when the Adapter display is not acknowledged (`ClientMessageDispatcher.cs:283-287`); `AdapterPairingNotifier` returns `false` without an Adapter connection; the Adapter's `Display` shows a HUD message and always returns `true` (`adapter/ipc/commonlib_adapter_pairing_notification_sink.cpp:7-15`) | At the main menu or during loading, the Host can report a code as displayed that the player may not see (inferred from source; not runtime-verified) |
| Pairing outcome detail | `PairingAvailability.unavailable` carries no reason; the app maps it to generic copy (`pairing_remote.datasource.dart:116-121`) | No typed "not ready" outcome |

**Required user-facing states**

| Situation | Expected presentation | Current support |
| --- | --- | --- |
| Connected | Live information | Supported: SDK streams pass unchanged through Redux to the Overview |
| Reconnecting | Last-known information with recovery indication | Not supported: recovery is indicated (`Reconnecting` card, SDK states), but values are cleared (ARCH-001) |
| Disconnected | Historical information with disconnected indication | Not supported: the Overview slice is reset, and the shell route requires a connected session |
| Host offline | Saved Overview accessible | Not supported: no historical storage; route redirects home |
| No saved information | Truthful empty state | Needs decision: live `notSubscribed`/`unavailable` states render truthfully, but no historical empty state exists and its wording is a presentation decision |
| Reconnected | Newly validated authoritative information | Supported for live state (new baselines after `subscription_ack`); precedence over historical data needs design (11.4) |
| Different play context | No mixing of unrelated values | Supported for live state: `StateMessageHandler._observeIdentity` resets trackers on any `(stateAuthorityId, playContextId)` change; historical data needs the rules in 11.8 |

### 11.3 Ownership

| Concern | Host | SDK | Flutter |
| --- | --- | --- | --- |
| Live gameplay values | Authoritative capture, revisions, play context | Synchronize, revision, recover; typed streams | Mirror and format |
| Gameplay readiness | Determines it from Adapter availability, resynchronization, and play context | Exposes a typed capability; never derives it from missing values | Presents it; never infers it from missing health, names, quests, or location |
| Pairing readiness | Decides and enforces whether a ceremony can start and be presented | Returns truthful typed outcomes | Presents availability; disabling Pair is advisory only |
| Historical snapshots | None | Records, stores, versions, recovers, retains, and deletes them behind an SDK capability another Dart client can use | Chooses when to show them and how they look |
| Offline navigation | None | None | Owns routes and the read-only shell mode |

Historical gameplay data is an SDK capability, not a Flutter implementation of DovahLink caching
rules (`ai/context/sdk/persistence.md`, "Ownership rule" and "Cache ownership").

### 11.4 Live versus historical state

- **Live state** is what the current admitted session synchronized: `StateSynchronization<T>` from
  the existing trackers, with real authority, play context, and revision.
- **Historical state** is a previously synchronized value read from local storage. It is not
  authoritative and is never presented as synchronized.

Rules for any implementation:

1. Historical values never enter `StateRevisionTracker`, `StateMessageHandler`, or
   `SubscriptionService`, and never receive a fabricated revision, authority, play context, or
   `synchronized` status. A snapshot capability reads from the live trackers; it never writes into them.
2. The SDK exposes historical data through a distinct read-only surface returning the existing typed
   domain models plus provenance (Host ID, capture time, the recorded `stateAuthorityId` and
   `playContextId`). Whether that surface reuses `StateSynchronization<T>` or a separate wrapper type
   is a decision; a separate type makes misuse harder and is the starting recommendation.
3. Keeping the last accepted value visible during recovery (ARCH-001 concern 2) is a *live-state*
   decision with its original identity intact. It is not historical data and needs no storage.
4. After reconnect, a domain's live value replaces its historical one only once a new baseline for
   that domain is accepted. Until then the historical value stays labelled historical.
5. Redux keeps live and historical sources distinguishable, with an explicit presentation state; it
   mirrors both SDK sources and runs no second synchronization engine.

### 11.5 Persistence considerations

- **Separate port.** `IClientStorage` holds credentials and pairing recovery, writes the whole
  `PersistedClientState` atomically, uses DPAPI on Windows, and fails closed: a store that cannot be
  read throws rather than becoming empty (`persistence/client_storage.dart`;
  `ai/context/sdk/persistence.md`). Historical snapshots need the opposite recovery policy (a
  corrupt snapshot is discarded and live gameplay continues), a different write rate, and deletion
  independent of trust. Extending `IClientStorage` or `PersistedClientState` would couple gameplay
  history to credential writes. History therefore gets its own SDK persistence port behind the
  platform-port rule (`ai/context/sdk/architecture.md`, "Platform ports"); credential and trust
  persistence stay unchanged.
- **Versioning.** The format is versioned independently of releases (`ai/context/common.md`,
  "Persisted-format migrations"). That rule fails closed on unknown future formats and forbids
  silently replacing a store with an empty one. For a non-authoritative cache, whether "fail closed"
  means "show nothing and leave the file untouched" or "discard and rewrite" needs an explicit
  decision.
- **Encoding.** The domain models are decode-only (`@JsonSerializable(createToJson: false)` in all
  current state models). Option (a): store each area's already validated protocol `data` object and
  decode it on read with the existing `StateDomainDefinition` decoder. This reuses the decoders but
  ties the stored format to each area's wire shape. Option (b): add encoders to the models. Evaluate
  (a) first because it adds no parallel serializer.
- **Failure isolation.** Read or write failure never fails connection, synchronization, or the live
  Overview; it surfaces as a typed, non-fatal condition.
- **Update frequency.** Writes are coalesced (for example on accepted baselines, session end, and
  best-effort shutdown), not one per Event. The exact policy is part of the feature design.
- **Retention and deletion.** One last-known Overview per Known Host, deleted when that Host is
  forgotten. Whether trust reset, revocation, or block also delete it, and whether the player gets an
  explicit "clear history" action, are decisions.
- **Storage choice.** No database is assumed; the smallest durable store that meets these rules is
  preferred. Snapshots contain character names and locations, so they stay in per-user storage;
  whether they also need encryption is a decision.

### 11.6 Gameplay-readiness semantics

These are separate facts and must not be conflated:

| Fact | Owner today | Public today |
| --- | --- | --- |
| Host discovery (candidate claim) | SDK discovery | Yes (`pairing.candidates`) |
| Host availability (reachability) | SDK presence monitor | Yes (`DovahLinkHostAvailability`) |
| Transport connectivity | SDK session | Yes (`DovahLinkConnectionState`) |
| Authenticated session | Host, mirrored by SDK | Yes (`sessionId`) |
| Trusted session | Host, mirrored by SDK | Yes (`trustState`) |
| Active Skyrim play context | Adapter notification, Host `PlayContextTracker` | Only as the envelope `playContextId` |
| Gameplay readiness | Host-internal facts only | No |
| Pairing readiness | Not modelled | No |

| Skyrim situation | Host status | Gameplay | Pairing | Saved Overview |
| --- | --- | --- | --- | --- |
| Closed | Offline (Host exits with Skyrim) | Unavailable | Not offered | Available (future) |
| Main menu | Online if reachable | Not ready | Not offered; Host must refuse | Available (future) |
| Loading | Online | Transitioning; gameplay operations wait for Host confirmation | Not offered; Host must refuse | Available (future) |
| Gameplay loaded | Online | Ready once the Host's authoritative conditions hold | Offered per Host capability, trust state, and security policy | Live data takes precedence |
| Connection interrupted | Recovering or offline | Unknown to the client | Not offered | Last-known data remains available |

- The Host decides readiness from real Adapter and Host state (Adapter available, resynchronized,
  play context current). The exact predicate, including whether particular areas must have
  baselines, is a Host decision recorded in the protocol documentation.
- Exposing it is a public protocol change (a new field or message), so it needs maintainer approval
  and follows `ai/context/protocol/compatibility.md`. Until then no client may present readiness.
- The SDK may keep a trusted transport session open at the main menu in order to observe readiness
  changes; readiness never gates authentication.

### 11.7 Pairing availability

- The Host is the enforcement point: a pairing start at the main menu or during loading must not
  report a code as available. Disabling Pair in the UI is presentation, not a security boundary.
- Options for the current six-digit flow: (a) the Host answers with the existing `unavailable`
  status when readiness does not hold (Host-only, no wire change); (b) the Adapter declines display
  outside gameplay (Adapter change); (c) leave the six-digit flow as is and deliver readiness-aware
  pairing through Stage 5A, whose 5A.2 and 5A.3 already require prompt invalidation on loading and
  main-menu transitions. The starting recommendation is (c), with (a) only if the maintainer wants an
  interim fix before 5A lands.
- The SDK reports a typed outcome that distinguishes "not ready" from other unavailability; that
  needs a contract addition, coordinated with 5A.7.
- All existing security and SAS invariants are preserved. This record changes no SAS implementation.

### 11.8 Identity and play-context safety

- Snapshots are keyed by durable `hostId`, one per Known Host, and shown only for the matching Host.
- `playContextId` is runtime identity: the Adapter mints a fresh one for every load, including a
  reload of the same save (`dovahlink_adapter_plugin.cpp:438-446`). It cannot identify a character
  or save across loads, and character name or race is not a save identity either. It is recorded as
  provenance only.
- A snapshot is coherent: every domain value in it comes from one `(stateAuthorityId, playContextId)`
  pair. When the play context changes, the snapshot for that Host is replaced as a whole, and a
  domain not yet baselined in the new context is absent rather than carried over from the old one.
- The approved requirement keeps future multi-character or save-profile history possible without a
  redesign. The versioned format (11.5) is enough to meet it: a later format version can add a
  profile key beside `hostId` through an ordinary migration. No profile field, multi-character, or
  save management is added now.

### 11.9 SAS coordination

| Stage 5A phase | Overlap | Coordination |
| --- | --- | --- |
| 5A.1, 5A.10 (Flutter pairing presentation) | Pairing readiness presentation; T2 forwarding removal | Do T2 before or inside 5A.1; add readiness presentation beside the SAS states, not as a separate pairing flow |
| 5A.2, 5A.3 (Adapter prompt, typed IPC) | Both need the same loading and main-menu lifecycle signals | Define Host readiness so 5A.3 consumes it rather than building a second detector |
| 5A.4 (pending approval) | Readiness at ceremony start and on transitions | The Host checks readiness when it starts or resumes an attempt |
| 5A.7 (public contract) | Readiness exposure and a typed "not ready" pairing outcome are public changes | Sequence or combine their compatibility impact with 5A.7 |

Persistent Overview has no SAS dependency.

### 11.10 Refactoring implications

What already supports the requirements: typed public domain models; `StateSynchronization<T>`
carrying authority and play-context identity; durable `hostId`-keyed Known Hosts; SDK Host
availability; the `ClientStateService` pattern of save-before-publish; platform ports; the existing
`unavailable` pairing status; Host `PlayContextTracker` and `AdapterAvailabilityTracker`; and the
live-state rule that Flutter mirrors SDK values unchanged.

What should change, and where:

- **SDK domain state (T5).** One registered definition list becomes the single source any snapshot
  capability enumerates, so history reuses the decoders and models without a second synchronization
  path.
- **SDK in-memory retention (ARCH-001 concern 2).** Decide what a tracker exposes between teardown
  and the next baseline. This is the only change needed for "Reconnecting keeps last-known values",
  and it needs no persistence.
- **SDK persistence.** A separate historical port (11.5) belongs to the feature itself; no
  preparatory abstraction is added before it.
- **SDK composition (T3).** New capabilities arrive as contracted services behind a grouped view,
  not as more `DovahLinkClient` fields and logic, and without a second engine
  (`ai/context/sdk/architecture.md`, "One engine, multiple API views").
- **Connection (T1).** Lifecycle states stay transport facts; readiness is a separate fact and never
  a new `DovahLinkConnectionState` value.
- **Flutter session.** The route guard and shell entry must stop depending on a connected session
  only when the offline Overview is built; changing them earlier has no product purpose.
- **Flutter Redux.** Live and historical sources stay separate slices or explicitly tagged sources,
  decided in the offline-Overview feature, with no synchronization logic in reducers.
- **Pairing (T2).** Removing the forwarding chain keeps one app mapping boundary where a typed "not
  ready" outcome can be added without threading it through use cases.

### 11.11 Proposed implementation sequence

The per-task detail lives in the Section 9 backlog. This is the ordering rationale for the two
requirements:

1. Correct the reconnect lifecycle (T1).
2. Consolidate state-domain definitions (T5).
3. Decide and implement in-memory retention across recovery (ARCH-001 concern 2).
4. Simplify SDK root and facade ownership, including the restoration owner (T3), so new capabilities
   do not grow the root.
5. Remove the pairing forwarding chain (T2), timed with 5A.1.
6. Establish Host-authoritative gameplay and pairing readiness: Host and protocol first, then SDK,
   coordinated with 5A.3 and 5A.7.
7. Implement the SDK historical snapshot capability.
8. Implement the Flutter offline Overview.
9. Integrate gameplay and pairing readiness into Flutter, with the pairing parts alongside 5A.10.
10. Consolidate documentation and guardrails (T7); T4 and T6 follow their SAS coordination points.

Steps 6 and 7 do not depend on each other; the maintainer may swap them. Roadmap placement of steps
6-9 (a Stage 6 or 8 phase, or a deviation phase) is a maintainer decision; this record does not
change any stage status.

### 11.12 Explicitly deferred capabilities

- Multi-character or save-profile history, and any save-file identity.
- History timelines or more than one snapshot per Host.
- Historical data for future domains (map, inventory, equipment); each feature decides when added.
- Cloud, cross-device, or exported history (already deferred in `ROADMAP.md`).
- Readiness-driven automatic connection (Stage 11).
- Requiring a loaded save for authentication.
- Any SAS implementation change, and any Overview visual redesign.
