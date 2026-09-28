# Current Execution Flow

**Purpose:** The maintainer's current near-term work order. This short-lived document does not
replace the ordered product roadmap and may be updated when a deviation closes or re-planning changes
the next work. Authoritative behavior remains in the relevant roadmap, architecture, security, SDK,
and deviation documents. [`ROADMAP.md`](../../ROADMAP.md) defines normal delivery order; deviation
records explain why work left that order and what happened; this page orders the next work across
active deviations.

## Work order

1. **Complete the Local Host discovery foundation in the current branch.** Keep the SDK-owned loopback
   probe behind the direct middleware boundary. Redux/ViewModels expose typed discovery states,
   candidates, selected Host, and semantic failure reasons. The temporary candidate uses only the
   fixed “Local Host” label and endpoint; discovery claims are not identity or trust. Temporary UI
   exercises the states but does not claim canonical prototype parity.
2. **Next PR — Known Host Lifecycle + Discovery Integration.** Connect the existing SDK-owned
   Known Host loading and persistence to application state so saved Hosts and discovery candidates
   are represented separately. Prove first-run discovery and pairing, persistence, loading after
   restart, and local-route correlation that prevents an already-associated localhost Host from
   appearing as new. Discovery claims, including a claimed `hostId`, remain untrusted; this local
   correlation is product routing behavior, not authentication. Known Host metadata remains known
   when the Host is offline, revoked, blocked, unrecognized, or needs repair. Only a future explicit
   forget/remove action clears the association. Existing SDK persistence is the foundation; this
   work integrates and verifies it end to end rather than inventing it again.
3. **Then — Canonical Discovery / Connections UI Convergence.** Reproduce the approved prototype
   presentation using the tested Known Host lifecycle, discovery candidates/status, selected Host,
   and real connection/pairing state. Keep presentation faithful to the prototype and add no fake
   delays. This follows Known Host lifecycle integration so the final UI can be built and tested
   against real saved/discovered Host behavior rather than temporary assumptions. It does not mark
   historical slice 03.6 complete.
4. **Later — production LAN discovery and secure initial pairing.** Keep today's localhost flow as
   candidate → connect → loopback development pairing → successful association → Known Host. When
   an approved SAS/secure bootstrap is ready, the production flow becomes candidate → connect →
   secure bootstrap → Pair / Reject / Block → successful association → Known Host. That security
   work replaces the initial pairing ceremony; it should not require rebuilding discovery, Known
   Host lifecycle, saved Host presentation, reconnect behavior, offline/repair presentation, or the
   Connections UI. Production LAN exposure and secure first contact remain gated by the security
   requirements and integration evidence.
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
- **Return to the normal roadmap:** [Phase 5.4 — Flutter Middleware and Minimal Live-State
  Proof](../05-dart-client-sdk-foundation.md); [Phase 5.5 — Version-Impact Audit and Stage 5
  Closure](../05-dart-client-sdk-foundation.md#55-version-impact-audit-and-stage-5-closure);
  [Stage 6 — PC / Second-Screen Baseline](../06-pc-second-screen-baseline.md); [Stage 7 — Core UI
  Theme System](../07-core-ui-theme-system.md); then [Stage 8 — Live Player State](../08-live-player-state.md).

## Return condition

Resume normal roadmap progression once the discovery foundation, Known Host lifecycle integration,
and separate canonical UI convergence are complete, remaining connection/pairing slices have been
re-planned, necessary work has been completed or explicitly deferred, and no active deviation still
has a justified reason to precede Phase 5.4.

## Stage 7 remains planned

Prototype convergence has already pulled forward a substantial part of Stage 7's visual and theme
foundation; Stage 7 is not complete. When reached in normal order, it may focus on integration with
the real connected product, auditing pulled-forward foundations, missing shared states/components,
accessibility and responsive validation, and final canonical visual fidelity. Feature-specific
presentation stays with the feature that owns it. See [Stage 7](../07-core-ui-theme-system.md).

## Security boundary

Generic secure initial-pairing research continues in
[`Soneka96/sas-pairing`](https://github.com/Soneka96/sas-pairing). DovahLink does not wait for that
research to continue unrelated local or product work, including Phase 5.4 when the current deviations
have fulfilled their purpose. Production secure first contact, unknown non-loopback pairing, Stage
5A secure Android/Wi-Fi, and production LAN exposure remain blocked until the required security
profiles and integration gates pass. `sas-pairing` remains research; it is not claimed complete or
production-ready. See the [initial-pairing security deviation](initial-pairing-security/README.md)
and the [DovahLink identity and transport security architecture](../../ai/context/security/identity-and-transport.md).

## Maintainer rules

- Finish current approved work before opening another deviation.
- Give every deviation a clear stopping condition.
- Re-review old plans after architecture changes; do not implement them automatically.
- Keep future feature screens in their normal stages unless approved for earlier work.
- Keep generic SAS research outside DovahLink.
- Keep first-contact and production LAN exposure gated until security prerequisites pass.
- Return to Phase 5.4 once the current deviations have fulfilled their purpose.
