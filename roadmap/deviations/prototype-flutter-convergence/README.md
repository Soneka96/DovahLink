# Prototype → Flutter Convergence

**Status:** Active — design and visual foundation complete; connection/pairing convergence partial;
remaining steps require re-planning.

## Why this deviation exists

The production Flutter app needed to become mechanically faithful to the approved landscape
prototype outside the normal ordered roadmap. Work began with design and visual foundations, then
expanded into connection, Host identity, discovery, Known Host persistence, pairing, and lifecycle
integration. The prototype governs presentation and interaction; SDK/domain/protocol contracts
govern actual application semantics.

## Canonical authority

The convergence history referred to the design as **DovahLink Prototype v17**. The current Flutter
architecture names the final approved visual source `DovahLink-Prototype-final`; use that current
authority for future work. The prototype governs composition, responsive behavior, interactions,
connection cards, pairing and Settings presentation, Host selection, and navigation/handoff. It does
not authorize the UI to invent connectivity, trust, or pairing outcomes. See
[`ai/context/flutter/architecture.md`](../../../ai/context/flutter/architecture.md) and the
[DovahLink security architecture](../../../ai/context/security/identity-and-transport.md).

## Progress

| Deviation step | Status | Detail |
| --- | --- | --- |
| 01 — Design contract | Complete | [Design contract](01-design-contract.md) · [PR #90](https://github.com/Soneka96/DovahLink/pull/90) |
| 02 — Visual/material foundation | Complete | [Visual foundation](02-visual-material-foundation.md) · [PR #93](https://github.com/Soneka96/DovahLink/pull/93) · [PR #94](https://github.com/Soneka96/DovahLink/pull/94) |
| 03 — Connection/pairing convergence | Partial | [Step index](03-connection-pairing-convergence/README.md) |
| 03.1–03.3 | Complete | Host identity, local discovery, and Known Host persistence |
| 03.4–03.10 | Paused / re-planning required | Client identity, lifecycle, discovery/trust UI, pairing state/UI, Settings, and handoff |
| 04 — Final canonical audit | Paused | [Final audit](04-final-canonical-audit.md) |

## Current position

Steps 03.1–03.3 completed in PRs [#97](https://github.com/Soneka96/DovahLink/pull/97),
[#98](https://github.com/Soneka96/DovahLink/pull/98), and
[#99](https://github.com/Soneka96/DovahLink/pull/99). The attempted follow-up in
[PR #100](https://github.com/Soneka96/DovahLink/pull/100) was closed after exposing a credential
identity-check race. That security detour is recorded separately in the
[Initial Pairing Security Investigation and Extraction](../initial-pairing-security/README.md);
generic SAS research continues in `Soneka96/sas-pairing`.

Remaining connection/pairing steps must be reconciled with current DovahLink security architecture
before implementation. Ordinary product work that does not depend on hostile-network first contact
may continue from Phase 5.4. Stage 5A and production LAN pairing remain gated.

## Related deviation

- [Initial Pairing Security Investigation and Extraction](../initial-pairing-security/README.md) is
  a separate top-level deviation, not a child of this one.

## Resume rule

Resume a paused convergence step only after its product intent has been reconciled with current SDK,
Host, and security contracts. The connection/pairing step index owns that re-planning; this README
does not invent new milestones or change the main roadmap's stage order.
