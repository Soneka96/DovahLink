# Current phase context

Source: `host/PLAN.md`
Source fingerprint: `host/PLAN.md` — `4CFA216D1121044DCB046527AF7ED5A930300EC00934432709DDA575E2020584`
Phase: Stage 3 — Thin Native Adapter and Private IPC
Package: `plans/stage-3-thin-native-adapter-private-ipc/`
Status: active

## Active concept

- File: `01-private-ipc-contract.md`
- Status: pending
- Prerequisites: Stage 1 and Stage 2 complete; feature branch active
- Next action: Implement the smallest private, host-owned IPC contract and deterministic framing/limit tests, keeping all public protocol and Skyrim runtime types out of it.

## Completed concepts

- None.

## Decisions and approved deviations

- The adapter is intentionally thin: host-directed keys/tokens are mapped only
  at the final Skyrim boundary; application logic remains in the host.
- Papyrus is limited to Skyrim-facing command forwarding and host readiness or
  error status; it does not own policy or retry logic.
- No material divergence is approved; see `PLAN.md`.

## Deferred debt

- Real Skyrim runtime verification remains deferred until the adapter runtime
  integration concept; it cannot be replaced by unit tests.

## Changed files

- Phase package files only; no production implementation has started.

## Verification

- `git branch --show-current`: `feature/3-thin-native-adapter-and-private-ipc`
- `host/PLAN.md` SHA-256: matches recorded fingerprint

## Handoff

Next concept: `01-private-ipc-contract.md`
Blocked by: none
