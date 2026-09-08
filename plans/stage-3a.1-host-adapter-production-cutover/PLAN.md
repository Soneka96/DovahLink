# 3A.1 — Host/Adapter Production Cutover

## Stage 1: Host Production Launch Path
- [ ] Complete

**Scope:**
Make the Host's public WebSocket listener start through its normal production entry point rather
than being gated behind a test-only environment variable. The production launch path must be the
one and only way the listener is enabled outside of test code.

**Acceptance criteria:**
- The Host public WebSocket listener works through the normal production launch path, not a
  test-only environment variable.

**Not in scope:** packaging the Host into the installable mod package, or proving the Adapter
launches/supervises it (Stage 4).

**Depends on:** None

**Notes:**

## Stage 2: Adapter Native Runtime Compatibility and Admin Consolidation
- [ ] Complete

**Scope:**
Port the native runtime compatibility behavior currently owned by Bridge into the Adapter --
specifically `bAlwaysActive` and `bAchievementCompat` -- so the Adapter carries this behavior
before Bridge is ever removed. Separately, make production packaging expose exactly one active
runtime implementation of the `DovahLinkAdmin` Papyrus/native script: the legacy Bridge
implementation (`bridge/game_state/commonlib_trust_admin_papyrus_adapter.{cpp,hpp}`) is removed or
disabled so only the Adapter's replacement
(`adapter/papyrus/commonlib_adapter_trust_admin_papyrus_adapter.{cpp,hpp}`) registers at runtime.

**Acceptance criteria:**
- The retained native runtime compatibility behavior currently owned by Bridge -- especially
  `bAlwaysActive` and `bAchievementCompat` -- is ported into the Adapter before Bridge removal.
- Production packaging exposes exactly one active runtime implementation of the `DovahLinkAdmin`
  Papyrus/native script; the legacy registration is removed or disabled before Adapter production
  activation.

**Not in scope:** any change to `TrustAdminService` itself or its host-owned mutation/invalidation
behavior -- only which native implementation registers the Papyrus/console surface.

**Depends on:** None

**Notes:**

## Stage 3: Configuration Naming and Trust-Data Migration
- [ ] Complete

**Scope:**
Replace old Bridge-specific configuration naming and paths with their Host/Adapter equivalents
where appropriate, and document the compatibility and migration behavior for anyone upgrading from
a Bridge-based install. Separately, decide explicitly whether old Bridge trust data is migrated
into the Host's trust store or left behind; a documented one-time re-pair is an acceptable outcome
if intentionally chosen, but the decision and its consequence for existing paired devices must be
recorded rather than left implicit.

**Acceptance criteria:**
- Old Bridge-specific configuration naming/paths are replaced where appropriate, with
  compatibility and migration behavior documented.
- Whether old Bridge trust data is migrated is decided explicitly. A documented one-time re-pair is
  an acceptable outcome if intentionally chosen.

**Not in scope:** the packaging layout that ships this configuration (Stage 4), or end-to-end proof
that a real upgrade/re-pair flow works (Stage 6).

**Depends on:** None

**Notes:**

## Stage 4: .NET Publishing and Vortex Packaging
- [ ] Complete

**Scope:**
Define the production .NET publishing strategy for the Host, strongly preferring a self-contained
Windows x64 build unless a documented repository constraint establishes a better choice. Build the
real installable/Vortex-ready package that contains the Adapter and the published Host together in
the expected layout, and confirm that the Adapter launches and supervises the packaged Host
correctly from that real package layout (as opposed to a development/manual arrangement of the two
binaries).

**Acceptance criteria:**
- A production .NET publishing strategy is defined; a self-contained Windows x64 Host is strongly
  preferred unless a documented repository constraint establishes a better choice.
- A real installable/Vortex-ready package contains Adapter + Host in the expected layout.
- The Adapter launches and supervises the packaged Host correctly.

**Not in scope:** removing any remaining production dependency on `bridge/`'s own packaging path
(Stage 5), or end-to-end validation of the packaged product's runtime behavior (Stage 6).

**Depends on:** Stage 1, Stage 2

**Notes:** Stage 1 must land first because the package can only publish the Host's real production
entry point, not the test-only launch path. Stage 2 must land first so the packaged Adapter binary
already carries its final native compatibility and admin-registration behavior rather than being
repackaged again afterward.

## Stage 5: Production Bridge-Independence Cutover
- [ ] Complete

**Scope:**
Remove every remaining production launch, build, and packaging dependency on `bridge/` now that the
Host/Adapter package (Stage 4), the ported native compatibility behavior (Stage 2), and the
replaced configuration (Stage 3) exist. After this stage, no production path links, launches, or
depends on the Bridge tree to operate. `bridge/` itself is not deleted by this stage -- it remains
in the repository, untouched, as frozen reference evidence, and its removal is out of scope for
this PR.

**Acceptance criteria:**
- Production no longer depends on Bridge to operate.
- `bridge/` is **not** deleted in this PR; it remains only as frozen reference evidence until the
  cutover has been proven.

**Not in scope:** deleting `bridge/` or its build/test wiring (a later, separate PR), and end-to-end
proof that the cutover product actually works (Stage 6).

**Depends on:** Stage 1, Stage 2, Stage 3, Stage 4

**Notes:**

## Stage 6: Real End-to-End Production Validation
- [ ] Complete

**Scope:**
Prove, against the real cutover-production package built in Stage 5, that the released Stage 3
pairing, trust, authentication, reconnect, administration, lifecycle, and security behavior
required by the supported product is preserved. Real end-to-end validation must exist for: launch,
Host startup, client connection, pairing, trusted reconnect, revoke/block/reset, orderly shutdown,
and forced Skyrim termination/orphan cleanup.

**Acceptance criteria:**
- The released Stage 3 pairing, trust, authentication, reconnect, administration, lifecycle, and
  security behavior required by the supported product is preserved.
- Real end-to-end validation exists for launch, Host startup, client connection, pairing, trusted
  reconnect, revoke/block/reset, orderly shutdown, and forced Skyrim termination/orphan cleanup.

**Not in scope:** any Product Stage 4 live-state feature (publication, capture, queues, revisions,
recovery).

**Depends on:** Stage 5

**Notes:** This is the acceptance gate for 3A.1 as a whole -- it is the only stage that proves the
preceding five stages actually compose into a working production cutover rather than five
independently-passing pieces.
