# 03 — Connection / Pairing Convergence

**Status:** Partial — 03.1–03.3 complete; 03.4–03.10 paused and require re-planning.

> **Numbering warning:** 03.1–03.10 are historical identifiers inside this deviation. They are not
> official Stage 3 roadmap phases. Official Stage 3.1 is “Live Pairing Challenge UX”; official 3.2
> and 3.3 describe Known Device trust administration and client trust-state integration in
> [`roadmap/03-local-device-pairing-and-reconnection.md`](../../../../roadmap/03-local-device-pairing-and-reconnection.md).

## Purpose

Converge the prototype's Host selection, connection, pairing, and post-pair handoff with real SDK and
Host behavior. The UI presents application truth; it does not create trust or infer pairing success.

## Ordered convergence slices

| Historical slice | Status | Record / history |
| --- | --- | --- |
| 03.1 Host identity contract | Complete | [Step record](03.1-host-identity.md) · [PR #97](https://github.com/Soneka96/DovahLink/pull/97) |
| 03.2 SDK local Host discovery | Complete | [Step record](03.2-sdk-local-host-discovery.md) · [PR #98](https://github.com/Soneka96/DovahLink/pull/98) |
| 03.3 Known Host persistence | Complete | [Step record](03.3-known-host-persistence.md) · [PR #99](https://github.com/Soneka96/DovahLink/pull/99) |
| Security detour after 03.3 | Closed, not merged | [PR #100](https://github.com/Soneka96/DovahLink/pull/100) exposed the identity-check race; see the separate [security deviation](../../initial-pairing-security/README.md). |
| 03.4 Companion device identity | Paused / re-planning required | [Step record](03.4-companion-device-identity.md) |
| 03.5 App Host lifecycle integration | Paused / re-planning required | [Step record](03.5-app-host-lifecycle.md) |
| 03.6 Discovery / trust UI | Paused / re-planning required | [Step record](03.6-discovery-trust-ui.md) |
| 03.7 Typed pairing state | Paused / re-planning required | [Step record](03.7-typed-pairing-state.md) |
| 03.8 Pairing UI | Paused / re-planning required | [Step record](03.8-pairing-ui.md) |
| 03.9 Settings / device name | Paused / re-planning required | [Step record](03.9-settings-device-name.md) |
| 03.10 Connection cards / handoff | Paused / re-planning required | [Step record](03.10-connection-cards-handoff.md) |

## PR #100 security detour

After Known Host persistence, PR #100 attempted an identity preflight: probe a Host on one socket,
disconnect, reconnect, then send the saved credential. Review found that the identity check could
occur only after the credential-bearing hello, while a replacement listener could take over the
endpoint between connections. The change was closed and never merged. This TOCTOU/security boundary
paused connection/pairing convergence and led to the separate
[Initial Pairing Security Investigation and Extraction](../../initial-pairing-security/README.md).
The detailed security analysis stays in the linked deviation and
[`ai/context/security/identity-and-transport.md`](../../../../ai/context/security/identity-and-transport.md).

## Current next action

Proceed with the separately approved local discovery UI foundation: expose the existing loopback
candidate as routing information only and connect it through the existing SDK flow. This does not
resume or complete historical slice 03.6's broader discovery / Known Host presentation plan.

Re-plan 03.4–03.10 against current DovahLink security and SDK architecture before resuming those
slices. The exact schedule belongs to future reviewed work; this historical index assigns no new
milestones.
