# Current Execution Flow

**Purpose:** The maintainer's current near-term work order. This short-lived document does not
replace the ordered product roadmap and may be updated when a deviation closes or re-planning changes
the next work. Authoritative behavior remains in the relevant roadmap, architecture, security, SDK,
and deviation documents. [`ROADMAP.md`](../../ROADMAP.md) defines normal delivery order; deviation
records explain why work left that order and what happened; this page orders the next work across
active deviations.

## Work order

1. **Implement the approved local Host discovery foundation.** Use the existing loopback discovery
   operation behind a narrow app-facing boundary. The app candidate exposes only the fixed display
   label and endpoint; discovery claims are not app Host identity or trust. The approved prototype
   remains the presentation authority, and the existing SDK connection path owns the outcome after
   selection. The discovery UI follows real state without simulated waits and remains stable if the
   discovery implementation changes. This advances the Connections experience without choosing
   production discovery.
2. **Record this convergence continuation.** Keep the candidate-only scope distinct from the
   broader historical 03.6 discovery / trust UI plan. Do not mark the broader step complete.
3. **Re-plan historical connection/pairing slices 03.4–03.10 before resuming them.** Compare each
   slice with current SDK, Host, and security architecture; work already completed; the closed PR
   #100 findings; the SAS
   research extraction; and the selected DovahLink authorization direction. The old sequence is
   history, not implementation authorization. For each slice, decide whether to keep, narrow,
   reorder, combine, defer, or remove it; this document makes none of those decisions.
4. **Implement only the work that survives that review.** Schedule the DovahLink-owned pairing
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
5. **Close or explicitly defer remaining connection/pairing convergence work.** Keep its disposition
   in the [prototype convergence deviation](prototype-flutter-convergence/README.md) and its
   [connection/pairing index](prototype-flutter-convergence/03-connection-pairing-convergence/README.md).
6. **Return to the normal roadmap:** [Phase 5.4 — Flutter Middleware and Minimal Live-State
   Proof](../05-dart-client-sdk-foundation.md); [Phase 5.5 — Version-Impact Audit and Stage 5
   Closure](../05-dart-client-sdk-foundation.md#55-version-impact-audit-and-stage-5-closure);
   [Stage 6 — PC / Second-Screen Baseline](../06-pc-second-screen-baseline.md); [Stage 7 — Core UI
   Theme System](../07-core-ui-theme-system.md); then [Stage 8 — Live Player State](../08-live-player-state.md).

## Return condition

Resume normal roadmap progression once this approved discovery foundation is complete, remaining
connection/pairing slices have been re-planned, necessary work has been completed or explicitly
deferred, and no active deviation still has a justified reason to precede Phase 5.4.

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
