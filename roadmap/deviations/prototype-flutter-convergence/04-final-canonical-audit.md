# 04 — Final Canonical Audit

**Status:** Partial — prior automated verification is recorded below; current PR-wide verification
must be assessed separately. Visual screenshot comparison is unverified.

## Outcome

This is the final verification of convergence against the approved landscape prototype. The approved
current journey is implemented, but the audit remains partial until the running Flutter surfaces can
be compared with the approved prototype screenshots. No exact visual-parity claim is made.

## Why it exists

After the design/material and connection/pairing work is implemented, one focused audit confirms
that the real app consumes truthful SDK/domain state and records what has and has not been compared
visually.

## Scope

The audit covers visual and interaction parity, navigation and responsive behavior, connection and
pairing presentation, post-pair handoff, accessibility, and integration with authoritative domain
state. This approved pass covers Connections, Discover, real Pairing states, and the minimal Session
Shell. It does not implement full game tabs, gameplay content, or notification surfaces.

## Non-goals

- This audit does not implement missing UI or change the main roadmap.
- It does not override SDK, Host, protocol, or security authority with prototype behavior.

## Dependencies / authority

The audit depends on completion or re-planning of the preceding convergence work and uses the
current approved source named in
[`ai/context/flutter/architecture.md`](../../../ai/context/flutter/architecture.md).

## Acceptance criteria

- The production app is reviewed against the canonical prototype for the listed visual, interaction,
  responsive, accessibility, and handoff concerns.
- UI state is checked against current SDK/domain contracts; no prototype-only trust or success is
  accepted.
- Findings and any remaining differences are recorded before declaring the deviation complete.

## History

The approved journey's code-level state mapping, responsive widget checks, and CI verification have
been completed. Flutter screenshots were not captured in this workspace, so the prototype-to-Flutter
pixel comparison remains unverified.

## Current disposition

Partial. Structural and interaction parity are implemented for the scoped journey. The surface-level
status and typed projection table in the [parent convergence record](README.md) is authoritative.
A previously recorded full-app run passed 2,627 Flutter tests, 1,069 Dart SDK tests, both analyzers,
the Windows debug build, and the Windows lifecycle policy test. These historical results describe
that run only and do not establish the current PR branch's complete verification status. Its exact
local commands and outcomes were:

| Capability | Command | Result |
| --- | --- | --- |
| SDK generation | `dart run build_runner build` from `sdk/dart/dovahlink_client` | Passed; 18 outputs written. |
| SDK analysis and tests | `dart analyze`; `dart test` from `sdk/dart/dovahlink_client` | Passed; no analyzer issues, 1,069 tests. |
| Flutter generation | `dart run build_runner build` from `app` | Passed; zero outputs changed. |
| Flutter analysis and tests | `flutter analyze`; `flutter test` from `app` | Passed; no analyzer issues, 2,627 tests. |
| Windows client build | `flutter build windows --debug` from `app` | Passed. |
| Windows lifecycle policy | `cmake --build build/windows/x64 --config Debug --target window_lifecycle_tests`; `ctest --test-dir build/windows/x64 -C Debug --output-on-failure` from `app` | Passed; one CTest target. |

| Surface | Structural / interaction status | Screenshot visual comparison |
| --- | --- | --- |
| Connections and Offline dialog | Implemented; Online, Offline, Connected, Reconnecting, Checking, Unknown, and Pair again remain semantically distinct. | Unverified — no Flutter screenshot captured. |
| Discover | Implemented for searching, available, checking, empty, failure, and embedded pairing. | Unverified — no Flutter screenshot captured. |
| Pairing | Implemented for the currently typed code, redisplay, cooldown, confirming, success, failure, blocked, and repair states. | Unverified — no Flutter screenshot captured. |
| Session Shell | Implemented with real Host context, SDK-connected entry, theme-aware header, empty body, and Back that preserves the admitted session. | Unverified — no Flutter screenshot captured. |

The workspace's UI automation cannot capture the running Windows Flutter client, and the local
prototype source is outside the repository. Its markup and theme CSS were inspected for source-level
values; that does not substitute for pixel comparison. Historical 03.4–03.10 convergence slices
remain paused for re-planning and are not marked complete here.

## Next action

Keep the convergence deviation partial until a maintainer can capture the listed Flutter screens and
compare them with the approved reference, then record any remaining visual differences. Re-plan the
historical 03.4–03.10 slices independently; this pass does not reopen their security or product
decisions.
