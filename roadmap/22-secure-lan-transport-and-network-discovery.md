# Stage 22 — Secure LAN Transport and Network Discovery

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./21-customizable-dashboard.md) · [Next stage](./23-mobile-tablet-client.md)

## 22. Secure LAN Transport and Network Discovery

**Status:** Planned

### Outcome

Approved LAN clients securely discover and connect to the intended bridge without trusting the LAN.

### Scope and behavior

- Complete the threat model and pairing design required by `ai/context/protocol/security.md`.
- Use established authenticated encryption; do not invent cryptography.
- Discover multiple hosts and bridges without treating address as identity.
- Authenticate endpoints before trusting advertised metadata.
- Preserve per-client authorization, revocation, replay protection, and session binding.
- Add approved wired and Wi-Fi/LAN candidates where platforms permit.
- Feed candidates into Phase 11 and preserve manual connection.

### Dependencies and boundaries

This phase depends on identity, multi-client isolation, local discovery, and automatic selection. It
does not imply internet exposure, hosted relay, accounts, or cloud presence.

### Acceptance criteria

Clients distinguish and securely connect to the intended bridge; spoofed or unpaired endpoints are
not trusted; revocation works; and localhost remains preferred where applicable.
