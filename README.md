# DovahLink

<p align="center">
  <img src="branding/dovahlink-bridge-header.png" alt="DovahLink Bridge — second-screen companion for Skyrim" width="100%" />
</p>

<p align="center"><strong>Your Dragonborn's second screen.</strong></p>

<p align="center">
  <strong>The DovahLink Bridge is under active development</strong>
</p>

DovahLink is an open-source companion platform for modded Skyrim. It connects Skyrim's live game state with a second monitor, tablet, or phone so players can build the companion interface that fits their playthrough.

The first supported release will focus on the foundation: a reliable local connection between Skyrim
and an external client, with read-only character information.

## Current development baseline

The repository contains the historical Bridge baseline `0.3.2`. It is not a supported public release
or a compatibility target; the previous Nexus listing was removed because the companion application
was not publicly downloadable.

That historical baseline provides:

- Local, authenticated communication between Skyrim and one external client
- Read-only character state with the player's current level
- Known Device trust administration, including revoke, block, unblock, forget, and reset operations
- Administrative session invalidation with developer-token isolation
- Clear handling for unsupported runtimes and failed connections
- A Vortex-ready installation package

The Bridge development target supports Steam Skyrim Special Edition `1.6.1170` with SKSE64 `2.2.6`.
The companion client is developed in this repository but is not included in a supported public
release yet.

## Visual identity

DovahLink uses a shared visual language across the Skyrim bridge and companion app:

<p align="center">
  <img src="branding/dovahlink-bridge.png" alt="DovahLink Bridge branding" width="49%" />
  <img src="branding/dovahlink-app.png" alt="DovahLink app branding" width="49%" />
</p>

## Planned direction

- Live interactive map
- Character and status dashboard
- Inventory, equipment, and spell views
- World exploration tools
- Customizable companion layouts
- Support for heavily modded load orders

These are product goals, not promises for the current release.

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

## Project documentation

- [PRODUCT.md](PRODUCT.md) defines the product and its boundaries.
- [ARCHITECTURE.md](ARCHITECTURE.md) defines the system boundaries and technical direction.
- [ROADMAP.md](ROADMAP.md) is the source of truth for phase status, order, and dependencies.
- [CONTRIBUTING.md](CONTRIBUTING.md) defines the development and proposal workflow.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) addresses known issues and solutions.

## Name

"Dovah" means dragon in the dragon language of Skyrim. "Link" describes the bridge between the game and the player's companion devices.
