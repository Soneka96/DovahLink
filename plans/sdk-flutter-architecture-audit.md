# SDK and Flutter architecture audit

**Status:** AUDIT REPORT. Documentation and investigation only. No production code, tests, protocol,
dependency, or governing document was changed by this audit.

**Location note.** This file lives in `plans/` because the maintainer's task named that path. On
`main` the `plans/` directory had no tracked files when this audit began, and
`roadmap/03a-host-adapter-production-migration.md` describes `plans/stage-*` documents as temporary
migration records that are deleted once their invariants are transferred. Several governing documents
still link into `plans/documentation-and-composition-normalization/`, which no longer exists (see
Section 8). If the maintainer wants this report kept permanently, the alternative home that fits an
existing convention is `roadmap/deviations/<topic>/`, which would also require an index entry.

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
sequencing, revision handling, or subscription recovery. No Critical finding was confirmed.

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
| Subscription intent and recovery | SDK | `SubscriptionService`, recovery services | Decides which areas the Overview wants | ARCH-007 |
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
- **Current behavior:** After a real transport drop the app tears down and re-attaches its listeners, clears the Overview slice, and re-issues eight sequential `subscribeStateArea` calls on top of the SDK's own `restoreDesiredStateAreas`.
- **Why problematic:** A consumer cannot tell a final disconnect from the first step of recovery. Documented behavior, test fake, and real SDK disagree. The SDK itself also clears gameplay values at teardown (`onTeardown` closure in `dovahlink_client.dart:243-250` calls `SubscriptionService.onSessionEnded`, which resets every area to `notSubscribed`), so the documented "stale retained values across reconnect" is not what either side does for transport loss.
- **Consequence if unchanged:** Redundant subscribe traffic and a visible Overview reset on every drop; any new consumer must rediscover the transient state; tests keep passing against a state sequence production never emits.
- **Recommended correction:** A maintainer decision is needed first because the connection-state sequence is public SDK behavior: either (a) the SDK stops publishing `disconnected` between `connected` and `reconnecting` when recovery will follow, or (b) the documented contract is changed to say the transient is part of the API and the app handles it. Either way the app test must be driven by the real sequence (for example an SDK-level contract fixture).
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
- **Paths and symbols:** `dovahlink_connections.dart` `renameDevice` (154-179) and `disconnect` (183-189); `dovahlink_client.dart` `close` (513-542); `dovahlink_pairing.dart` four `restoreDesiredStateAreas` calls (123, 137, 163, 173); `session_admission_service.dart:66-68`.
- **Evidence:** `renameDevice` builds the request, applies `RequestPolicy`, decodes the reply, and reports protocol violations inside the facade. The four-step cancel sequence (`cancelPendingAuthentication`, `stopInitialConnectionRetry`, `stopRecovery`, `clearDesiredStateAreas`) is duplicated in `disconnect` and `close`, and `connectWithInitialRetry` repeats a subset. Trusted admission already restores desired areas, and the pairing facade restores them again after pairing recovery. `api-design.md` says these groups are views that do not own behavior.
- **Why problematic:** Anything that ends a session must remember the sequence; a fifth caller will miss a step. Restoration being triggered from two layers makes ownership unclear.
- **Recommended correction:** Give deliberate-disconnect and rename each a service owner; make admission the single trigger for restoration after pairing becomes trusted.
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
- **Why problematic:** Every new domain (map, inventory, equipment in the roadmap) repeats ~10 edits per side with no compile-time reminder to wire recovery.
- **Recommended correction:** SDK only: a small domain registration abstraction so a module declares its area, decoder, availability rule, and event support once and recovery is derived from it, and `currentHost` exposes domains without a field each. Do not create one global stream or merge domain modules. App-side repetition is presentation-owned and should be handled when the next domain is actually added.
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
| D-5 | Contradiction with behavior | `ai/context/flutter/architecture.md` "Live gameplay state" | Says ordinary reconnect keeps listeners attached; real SDK emits a transient `disconnected` (ARCH-001). | Resolve with ARCH-001's decision. |
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
| T5 | SDK state-domain registration | ARCH-006 | Medium | Independent of SAS |
| T6 | SDK subscription and typed-failure API additions | ARCH-007 | Medium | Coordinate with 5A.7 (public SDK change) |
| T7 | Documentation corrections and guardrails | D-1..D-8, S-5 | Low | Independent of SAS |

### T1. Resolve the reconnect state sequence

- **Branch:** `fix/reconnect-state-sequence`
- **Scope:** The maintainer decides between the two options in ARCH-001. Implement the chosen one: either the SDK suppresses the transient `disconnected` when recovery will follow, or the documented contract and the app's `LiveStateMiddleware` are updated to treat it explicitly. Replace the app test's hand-built sequence with one derived from the real SDK sequence, and correct the matching sentence in `ai/context/flutter/architecture.md`.
- **Dependencies:** none. **Public SDK change:** yes if option (a).
- **Non-goals:** changing reconnect timing, retry budgets, subscription semantics, or any security behavior; adding new states.
- **Benefit:** the documented, tested, and real behavior agree; no redundant subscribe traffic or Overview reset on drops.
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
- **Scope:** Move discovery reconciliation and Known Host invalidation cleanup out of `DovahLinkClient` into contracted collaborators; give deliberate disconnect and rename a service owner; make admission the single restore trigger; delete the test-only observation hook; verify S-3 and S-4 with throwaway tests and fix only if confirmed.
- **Dependencies:** none, but sequence after 5A.7/5A.8 if those restructure pairing.
- **Non-goals:** new public API, a second client engine, changes to the three documented callbacks.
- **Benefit:** the root only wires; discovery logic becomes independently testable.
- **Estimated files:** 20-30.
- **Regression risks:** discovery race suppression, storage-error propagation, cancellation order on close.
- **Acceptance criteria:** the root holds no behavior or mutable discovery fields; all existing `discoverHosts`, `close`, and disconnect tests pass; no public export changes.

### T4. Single owner for rejection and availability policy

- **Branch:** `refactor/sdk-rejection-availability-policy`
- **Scope:** One contracted policy maps rejection or invalidation evidence to `pairingRequired` and availability, used by the root, authentication, reconnect, pairing, and the presence monitor.
- **Dependencies:** after SAS phases that add rejection outcomes (5A.7).
- **Non-goals:** changing which outcomes require pairing or what availability each yields.
- **Benefit:** one place to extend when trust outcomes grow.
- **Estimated files:** 12-18.
- **Regression risks:** subtle availability transitions during recovery.
- **Acceptance criteria:** table-driven policy tests reproduce today's mapping exactly; all callers delegate.

### T5. SDK state-domain registration

- **Branch:** `refactor/sdk-state-domain-registration`
- **Scope:** A registration type declares area, decoder, availability rule, and event support once; recovery is derived from it; `currentHost` exposes domains without a field each; the three identical modules collapse onto it. Domain streams and models stay separate.
- **Dependencies:** none; do before the next gameplay domain (Stage 15 onward).
- **Non-goals:** a global state stream, merging domains, protocol changes, app-side Redux changes.
- **Benefit:** a new domain becomes a registration plus a model; Event domains cannot miss recovery.
- **Estimated files:** 25-35.
- **Regression risks:** gate and baseline behavior in `StateMessageHandler` and `SubscriptionService`.
- **Acceptance criteria:** existing state, module, and subscription tests pass; a test registers a stub domain without touching `DovahLinkClient`.

### T6. SDK subscription and typed-failure API additions

- **Branch:** `feature/sdk-subscription-and-failure-api`
- **Scope:** Add a multi-area subscribe, a readable desired set, and a typed administrative-invalidation failure; remove the app's `_requestedDesiredAreaStores` and connection-state probe.
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
