# Current Execution Flow

**Purpose:** The maintainer's current near-term work order. This short-lived document does not
replace the ordered product roadmap and may be updated when a deviation closes or re-planning changes
the next work. Authoritative behavior remains in the relevant roadmap, architecture, security, SDK,
and deviation documents. [`ROADMAP.md`](../../ROADMAP.md) defines normal delivery order; deviation
records explain why work left that order and what happened; this page orders the next work across
active deviations.

## Work order

1. **Local Host discovery foundation — complete.** Keep the strategy-neutral SDK `discover()`
   contract behind the direct middleware boundary. Redux/ViewModels expose typed discovery states,
   candidates, selected Host, and semantic failure reasons. Discovery returns candidates only; its
   claims are not identity, trust, authentication, or Known Host state. The temporary UI does not
   claim canonical prototype parity.
2. **Known Host lifecycle + discovery integration — complete.** The SDK owns the complete,
   Host-ID-keyed Known Hosts collection and exposes it through `loadKnownHosts()` and complete
   committed snapshots on `knownHostsChanges`. `ConnectionMiddleware` subscribes at store
   initialization, maps SDK Hosts through `HostMapper`, and dispatches a typed observation action;
   Redux `knownHosts` is only that app-mapped projection. Pairing, authentication, credential
   recovery, and metadata refresh continue to follow SDK-owned rules. Discovery remains a separate
   command/result candidate list. A claimed `hostId` does not establish trust or mutate Known Hosts.
   The canonical UI now consumes this projection alongside discovery candidates/status, selected
   Host, and real connection/pairing state.
3. **Canonical Discovery / Connections / Pairing / Session Shell convergence — implemented;
   screenshot audit partial.** Durable Known Hosts stay on Connections and ephemeral candidates stay
   in Discover. Real Redux state drives discovery, authentication, and typed pairing outcomes. The
   SDK's persisted `pairingRequired` hint controls the Online-only Pair again card and is revalidated
   through normal authentication. An admitted Host opens the minimal Session Shell only after the
   exact Known Host reports `connected`; the route guard rejects direct navigation for missing or
   disconnected Hosts. Typed administrative invalidation closes only the matching Host's shell,
   while reconnecting and reauthenticating leave it open. Back preserves the session. The
   Notifications control remains prototype-only with a future-feature TODO. Historical slices
   03.4–03.10 remain paused for re-planning, and screenshot comparison remains unverified. See the
   [convergence deviation](prototype-flutter-convergence/README.md) and
   [final canonical audit](prototype-flutter-convergence/04-final-canonical-audit.md) for current
   state mapping and verification.

   Tapping an Offline Known Host opens an informational “Skyrim isn’t running” dialog from the real
   offline projection; it does not select the Host or start authentication.
4. **Character Core Data Foundation — active.** Add maximum vitals, character name and identity
   race, and typed supernatural traits through the existing Adapter, Host, protocol, and SDK
   boundaries. This is a backend/data-contract phase; it adds no Flutter UI or playable-context
   lifecycle. Identity-race and supernatural source semantics must be verified before those values
   are captured. See the [Character Core Data Foundation deviation](character-core-data-foundation/README.md).
5. **Later — production LAN discovery and secure initial pairing.** Production LAN exposure and
   secure first contact remain gated by the security requirements and integration evidence. If an
   approved SAS profile preserves the current human interaction, pairing implementation changes
   may stay behind the SDK boundary and use the same Flutter presentation. If SAS requires materially
   different human interaction, Flutter pairing presentation may change while SDK-owned Host,
   session, and trust lifecycle remains reusable. No SAS profile or UX is selected by this work.
## Remaining convergence planning

- **Re-plan historical connection/pairing slices 03.4–03.10 before resuming them.** Compare each
  slice with current SDK, Host, and security architecture; work already completed; the closed PR
  #100 findings; the SAS research extraction; and the selected DovahLink authorization direction.
  The old sequence is history, not implementation authorization. For each slice, decide whether to
  keep, narrow, reorder, combine, defer, or remove it; this document makes none of those decisions.
- **Implement only the work that survives that review.** Schedule the DovahLink-owned pairing
  authorization boundary at the reviewed point where it naturally belongs. The selected direction
  is:

  ```text
  pairing/bootstrap evidence
      -> pending authorization for one exact attempt
      -> Skyrim user chooses Pair / Reject / Block
      -> durable application trust where applicable, only after Pair
  ```

  The architecture is selected; runtime implementation is deferred. Do not assign it to a numbered
  historical slice here. Its eventual approval must identify the exact pairing attempt so stale
  approval cannot authorize a later ceremony. DovahLink owns this application boundary; it does not
  define generic SAS cryptography or a new protocol schema.
- **Close or explicitly defer remaining connection/pairing convergence work.** Keep its disposition
  in the [prototype convergence deviation](prototype-flutter-convergence/README.md) and its
  [connection/pairing index](prototype-flutter-convergence/03-connection-pairing-convergence/README.md).
- **After the character data sequence, return to the normal roadmap:** complete the active
  [Character Core Data Foundation](character-core-data-foundation/README.md), then its follow-on
  World Context Data Foundation, Active Play Context Lifecycle, and Session Overview convergence
  phases in that order. Reconcile the resulting product work with [Phase 5.4 — Flutter Middleware
  and Minimal Live-State Proof](../05-dart-client-sdk-foundation.md), [Phase 5.5 — Version-Impact
  Audit and Stage 5 Closure](../05-dart-client-sdk-foundation.md#55-version-impact-audit-and-stage-5-closure),
  [Stage 6 — PC / Second-Screen Baseline](../06-pc-second-screen-baseline.md), [Stage 7 — Core UI
  Theme System](../07-core-ui-theme-system.md), and [Stage 8 — Live Player State](../08-live-player-state.md).

## Return condition

This condition sets the maintainer's chosen return point for the normal ordered roadmap; it is a
scheduling decision, not a technical or security dependency for unrelated work. Phase 5.4 work that
does not depend on hostile-network first contact remains technically permitted, but is not currently
scheduled ahead of this deviation unless the maintainer explicitly reprioritizes it.

Resume normal roadmap progression once the discovery foundation, Known Host lifecycle integration,
and separate canonical UI convergence are complete, remaining connection/pairing slices have been
re-planned, the character data sequence above is complete or explicitly re-planned, and no active
deviation still has a justified reason to precede Phase 5.4.

## Stage 7 remains planned

Prototype convergence has already pulled forward a substantial part of Stage 7's visual and theme
foundation; Stage 7 is not complete. When reached in normal order, it may focus on integration with
the real connected product, auditing pulled-forward foundations, missing shared states/components,
accessibility and responsive validation, and final canonical visual fidelity. Feature-specific
presentation stays with the feature that owns it. See [Stage 7](../07-core-ui-theme-system.md).

## Security boundary

Generic secure initial-pairing research continues in
[`Soneka96/sas-pairing`](https://github.com/Soneka96/sas-pairing). Phase 5.4 work independent of
hostile-network first contact remains technically permitted; current maintainer scheduling keeps it
behind this deviation unless explicitly reprioritized. Production secure first contact, unknown
non-loopback pairing, Stage 5A secure Android/Wi-Fi, and production LAN exposure remain blocked until
the required security profiles and integration gates pass. `sas-pairing` remains research; it is not
claimed complete or production-ready. See the [initial-pairing security deviation](initial-pairing-security/README.md)
and the [DovahLink identity and transport security architecture](../../ai/context/security/identity-and-transport.md).

## Maintainer rules

- Finish current approved work before opening another deviation.
- Give every deviation a clear stopping condition.
- Re-review old plans after architecture changes; do not implement them automatically.
- Keep future feature screens in their normal stages unless approved for earlier work.
- Keep generic SAS research outside DovahLink.
- Keep first-contact and production LAN exposure gated until security prerequisites pass.
- Return to Phase 5.4 once the current deviations have fulfilled their purpose.
