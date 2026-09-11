# Stage 3A — Host/Adapter Production Migration

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./03-local-device-pairing-and-reconnection.md) · [Next stage](./04-live-state-synchronization-foundation.md)

## 3A. Host/Adapter Production Migration

**Status:** Complete. Host + Adapter are the current production implementation; the native Bridge (`bridge/`) has been deleted.

### Outcome

`host/` and `adapter/` become the real production implementation, `bridge/` is deleted, and the
repository describes Host + Adapter as its active architecture instead of a migration in progress.
Product Stage 4+ development then continues exclusively on Host + Adapter.

3A is an architectural migration/interstitial gate, not a new user-facing product feature. It sits
logically after the released Stage 3 baseline and before further Stage 4 product development, and it
does not renumber the existing Product Stage 4: Stage 4's own numbering, scope, and status (tracked in
`roadmap/04-live-state-synchronization-foundation.md`) are unaffected by 3A's insertion into the
sequence.

The target compatibility baseline for cutover is the last released Stage 3 behavior, not unreleased
Stage 4 Bridge development. The last actual released/production Bridge baseline contains the completed
Stage 3 functionality; Stage 4 work that exists in the repository is development work that was never
released, so unreleased Stage 4 Bridge functionality must not block replacing the Bridge. Live-state
publication, capture, queues, revisions, and recovery — the Stage 4-equivalent engineering work
currently reachable through the Bridge — are not cutover prerequisites; they continue as ordinary
Product Stage 4 work on Host + Adapter after 3A completes, per
`roadmap/04-live-state-synchronization-foundation.md`.

While 3A was open, no new product functionality was developed in `bridge/`; only a
maintainer-approved compatibility or safety fix needed to keep the frozen reference usable was
permitted there. This included the remaining Bridge-authored Stage 4 phases (4.2 onward, per
`roadmap/04-live-state-synchronization-foundation.md`), which were paused rather than in progress.
Now that 3A is complete, all further Stage 4+ development happens only through Host + Adapter.

### Scope and behavior

3A is split into exactly three ordered subphases, each a separately reviewable PR. The sequence is
strict: 3A.1 proves the replacement in production without touching `bridge/`'s source; 3A.2 removes
the now-obsolete implementation; 3A.3 makes the repository describe the resulting architecture
directly.

### 3A.1 — Host/Adapter Production Cutover

**Status:** Complete

**Purpose:** Make Host + Adapter the real production implementation before deleting the Bridge source.

**Acceptance criteria:**

- The Host public WebSocket listener works through the normal production launch path, not a
  test-only environment variable.
- The Adapter launches and supervises the packaged Host correctly.
- A real installable/Vortex-ready package contains Adapter + Host in the expected layout.
- A production .NET publishing strategy is defined; a self-contained Windows x64 Host is strongly
  preferred unless a documented repository constraint establishes a better choice.
- The released Stage 3 pairing, trust, authentication, reconnect, administration, lifecycle, and
  security behavior required by the supported product is preserved.
- The retained native runtime compatibility behavior currently owned by Bridge — especially
  `bAlwaysActive` and `bAchievementCompat` — is ported into the Adapter before Bridge removal.
  (Both are implemented in `adapter/runtime/commonlib_adapter_game_behavior_compatibility.{cpp,hpp}`.)
- Old Bridge-specific configuration naming/paths are replaced where appropriate, with compatibility
  and migration behavior documented.
- Production packaging exposes exactly one active runtime implementation of the `DovahLinkAdmin`
  Papyrus/native script. Both `bridge/game_state/commonlib_trust_admin_papyrus_adapter.{cpp,hpp}`
  (legacy) and `adapter/papyrus/commonlib_adapter_trust_admin_papyrus_adapter.{cpp,hpp}` (replacement)
  exist in the repository as of this writing; the legacy registration is removed or disabled before
  Adapter production activation.
- Whether old Bridge trust data is migrated is decided explicitly. A documented one-time re-pair is
  an acceptable outcome if intentionally chosen.
- Real end-to-end validation exists for launch, Host startup, client connection, pairing, trusted
  reconnect, revoke/block/reset, orderly shutdown, and forced Skyrim termination/orphan cleanup.
- Production no longer depends on Bridge to operate.
- `bridge/` is **not** deleted in this PR; it remains only as frozen reference evidence until the
  cutover has been proven.

No Product Stage 4 live-state feature development belongs here.

**Depends on:** Stage 3 (released baseline). Does not depend on Stage 4's live-state work.

### 3A.2 — Legacy Bridge Removal

**Status:** Complete

**Purpose:** Remove the obsolete implementation and Bridge-only validation/build infrastructure after
Host + Adapter are already proven as production by 3A.1.

**Acceptance criteria:**

- Delete `bridge/`.
- Remove Bridge CI/build/package wiring.
- Remove the Bridge-specific SDK real-process harness/tests once equivalent supported Host
  integration coverage exists.
- Remove the old Bridge-oriented .NET validation client/scenario infrastructure where it is no
  longer useful.
- Preserve useful transport-independent SDK/unit tests.
- `integration/` is not blindly deleted: shared replacement contracts are identified first. In
  particular, `integration/private-ipc-limits.json` is currently part of the Host↔Adapter
  architecture and is moved to an appropriate durable shared-contract location before legacy
  integration infrastructure is removed.
- Bridge paths are removed from local CI, repository consistency checks, and development tooling.
- Obsolete Bridge packaging artifacts/configuration are removed.
- Historical changelog/release history is kept where appropriate.
- No unrelated features or broad refactors.

**Depends on:** 3A.1's production cutover being proven.

### 3A.3 — Repository Normalization

**Status:** Complete

**Purpose:** Make the repository describe the final architecture directly instead of permanently
describing Host + Adapter as a migration/replacement.

**Acceptance criteria:**

- Root `README.md`, `PRODUCT.md`, `ARCHITECTURE.md`, `TROUBLESHOOTING.md`, `CONTRIBUTING.md`, and
  applicable AI instructions are rewritten/simplified so Host + Adapter are the active architecture.
- Obsolete "frozen Bridge", "replacement architecture", and migration-gate language that is no
  longer relevant is removed.
- `.vscode` configuration no longer treats `bridge/` as the primary CMake project.
- Bridge-specific generic tooling such as `BridgeBuilder` is renamed/refactored into product-level
  DovahLink tooling terminology (Adapter/Host, not Bridge) where the tooling remains useful.
- Bridge-specific AI context such as the old native Bridge architecture/testing documentation is
  removed or rewritten, while still-applicable C++ style and SKSE/runtime-quirk knowledge is
  preserved for Adapter.
- Completed migration-only documents — `host/PLAN.md`, `ai/context/host/migration-audit.md`,
  temporary `plans/stage-*` documents — are evaluated and deleted only after any still-current
  invariant has been transferred into durable Host/Adapter/protocol documentation.
- Obsolete Bridge branding/assets are cleaned up or renamed to current DovahLink branding where
  appropriate.
- A repository-wide stale-reference audit is performed for `bridge/`, `DovahLinkBridge`,
  Bridge-specific environment/configuration names, old build directories, CI names, documentation
  links, and tooling assumptions.
- Legitimate historical references such as changelog history are preserved where deleting them
  would erase project history.

**Depends on:** `bridge/` already being removed by 3A.2.

**Not in scope for 3A as a whole:** any Product Stage 4 live-state feature (publication, capture,
queues, revisions, recovery) — that work is re-homed to
`roadmap/04-live-state-synchronization-foundation.md` and continues only after 3A completes.
