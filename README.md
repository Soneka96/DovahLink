# DovahLink

## Your Dragonborn's second screen.

> Hey, you're finally connected.

DovahLink is an open-source companion platform for modded Skyrim. It is intended to connect Skyrim's live game state with a second monitor, tablet, or phone so players can build the companion interface that fits their playthrough.

The project is starting with the smallest useful question: can Skyrim communicate reliably with an external device?

## Planned direction

- Live interactive map
- Character and status dashboard
- Inventory, equipment, and spell views
- World exploration tools
- Customizable companion layouts
- Support for heavily modded load orders

These are product goals, not promises for the first release.

## Project shape

```text
Skyrim
   |
SKSE integration
   |
DovahLink protocol
   |
PC / tablet / phone
```

Implementation begins only after a direct instruction from the maintainer in the current task explicitly names the feature and requested scope. That instruction has now started the Phase 1 implementation foundation; the bridge and transport are still pending. Roadmap status, Issues, prior conversations, suggestions, and `continue` messages do not authorize unrelated work.

## Status

Phase 0.5, the client and protocol foundation, is complete. Phase 1, the Skyrim Bridge Foundation,
has not started.

The completed foundation includes client entities and models, connection use cases, Redux state
and selectors, dependency injection, and a read-only connection status screen. The Skyrim bridge
and real loopback transport are the specific scope of Phase 1.

See [PRODUCT.md](PRODUCT.md) for the product definition, [ARCHITECTURE.md](ARCHITECTURE.md) for the technical direction, and [ROADMAP.md](ROADMAP.md) for planned milestones.

## Discussion and suggestions

Suggestions, feedback, and feature ideas are welcome through GitHub Issues. DovahLink is currently maintained by its creator, and all code changes are made by the maintainer.

Please use Issues to discuss ideas rather than opening pull requests. The maintainer will decide which suggestions fit the project's architecture and roadmap.

## Name

"Dovah" means dragon in the dragon language of Skyrim. "Link" describes the bridge between the game and the player's companion devices.
