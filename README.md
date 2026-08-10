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

The repository currently contains documentation only. The Flutter companion app and Skyrim bridge will be added through feature branches after the initial documentation is agreed on.

## Status

🚧 Early planning

The first implementation milestone is a reliable, minimal connection between Skyrim and one external client.

See [PRODUCT.md](PRODUCT.md) for the product definition, [ARCHITECTURE.md](ARCHITECTURE.md) for the technical direction, and [ROADMAP.md](ROADMAP.md) for planned milestones.

## Contributing

Work should happen on a feature branch and be submitted as one pull request per feature. Start with [CONTRIBUTING.md](CONTRIBUTING.md).

## Name

“Dovah” means dragon in the dragon language of Skyrim. “Link” describes the bridge between the game and the player's companion devices.
