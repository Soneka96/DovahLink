# Current Execution Flow

**Purpose:** The maintainer's current near-term work order. This short-lived document does not
replace the ordered roadmap. [`ROADMAP.md`](../../ROADMAP.md) defines stage order and status;
deviation records explain intentional work outside that order.

## Delivered foundations

- **Local Host discovery — delivered.** The SDK owns candidate discovery, normalization, expiry,
  and typed outcomes; candidate metadata remains distinct from Host identity and trust.
- **Known Host lifecycle and discovery integration — delivered.** The SDK owns durable Known Host
  state. Flutter displays that projection separately from ephemeral candidates and preserves the
  selected authentication source.
- **Connection, pairing, and Session Shell convergence — delivered for the current product.** The
  app uses real Known Host, authentication, pairing, and session state. An admitted session opens the
  Session Shell; Back preserves it; terminal invalidation closes only the matching Host's shell.
  The Notifications control remains a prototype-only placeholder.
- **Character Core Data — delivered.** Vitals, XP, and Level are separate typed domains with real
  Skyrim values.
- **Character Identity and Traits — delivered.** Identity and independent supernatural predicates
  flow through the production Adapter, Host, SDK, and Flutter state pipeline.
- **World Context and Tracked Quests — delivered.** Location, Skyrim game time, and the complete
  tracked-quest collection are available as real synchronized state. Empty quests remain distinct
  from unavailable quest state.
- **Session Overview and live-state reliability follow-up — delivered.** PR #120 added the
  SDK-to-Redux pipeline; PR #122 connected the state-backed Overview. Merged reliability follow-ups
  corrected subscription convergence and recovery behavior. Runtime validation is recorded in the
  Phase 5.4 acceptance review.

Historical connection/pairing slices 03.4–03.10 are not the current work order. Revisit them only if
a future approved task depends on unresolved behavior; do not resume them from their old status text.

## Current closeout

**Stage 5 — Dart Client SDK Foundation:** Phases 5.1–5.4 delivered the typed SDK contract,
synchronization, subscription/recovery lifecycle, and Flutter live-state proof. Phase 5.5 audited the
complete Stage 5 range, recorded version and compatibility decisions, reviewed all acceptance
criteria, and closed Stage 5. The maintainer's Phase 5.4 runtime validation remains recorded in the
Stage 5 specification.

## Next

**Next planning action:** audit the remaining Stage 6 acceptance criteria against the implementation
already delivered before adding new work. Keep Stage 6 planned until that review identifies its
remaining requirements. Do not begin Stage 6 implementation as part of the Stage 5 closeout.

Stages 6–8 remain planned. Implementation pulled forward into the Session Shell, theme system, and
live-state Overview does not by itself close those stages. Their acceptance must be reviewed against
the current product and implementation when reached.

## Separate security track

The S2.2 initial-pairing security decision remains **STOP**. Generic `sas-pairing` research and its
separately authorized pre-alpha integration work remain on the security track. The maintainer-authorized
P10 work adds only a persistent Host identity and a dormant, test-exercised Host `sas-pairing`
integration foundation that the running product does not use; the six-digit flow and bearer reconnect
remain current behavior, and Stage 5A owns activation, limited to Windows loopback. The running six-digit pairing flow is not
production security for hostile-network first contact. Stage 5B secure
Android/Wi-Fi, unknown non-loopback peers, and production LAN exposure remain blocked until the
required security profiles and integration gates pass. This work does not claim secure LAN is
solved.
