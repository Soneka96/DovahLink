# Stage 23 — Mobile / Tablet Client

[Back to the roadmap index](../ROADMAP.md). [Previous stage](./22-secure-lan-transport-and-network-discovery.md) · [Next stage](./24-item-knowledge-and-search.md)

## 23. Mobile / Tablet Client

**Status:** Planned

### Outcome

The companion experience works naturally on supported phones and tablets through secure LAN.

### Scope and behavior

- Provide touch-optimized portrait navigation and module layouts.
- Support landscape second-screen presentation where appropriate.
- Reuse domain and protocol boundaries with device-specific presentation.
- Preserve pairing, recovery, background, resume, and network transitions.
- Use the Phase 11 policy with manual fallback.
- Keep layout preferences local and provide mobile defaults.

### Dependencies and boundaries

This phase depends on Stage 5A's Android development path and Stage 22's generalized secure LAN
transport. It does not imply internet access, hosted relay, accounts, or identical layouts.

### Acceptance criteria

A device pairs and reconnects securely, survives background and network changes, presents existing
features accessibly, and cannot confuse another discovered bridge.
