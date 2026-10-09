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
