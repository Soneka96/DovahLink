# Product

## Problem

Useful information about a Skyrim playthrough is trapped inside the game interface or scattered across unrelated websites and tools. Players using large mod lists often need a second source of information while keeping the game view clear.

## Product idea

DovahLink creates a connected companion interface for Skyrim. A player can use an external screen to view live game information and choose which tools are visible without changing the main game experience.

## First user

The first user is a PC Skyrim player with a modded setup and access to a second screen or mobile device. They value immersion, customization, and practical information while exploring or managing a playthrough.

## Initial success criteria

The first milestone is successful when a player can:

1. Connect Skyrim to one external client.
2. See a small set of trustworthy live values.
3. Recover cleanly when the connection is interrupted.
4. Understand what is happening without reading developer documentation.

## Product principles

- **Useful before broad:** prove one reliable workflow before adding many panels.
- **Player-controlled:** layouts and information density should be configurable.
- **Mod-aware:** design for real modded load orders, not only a clean install.
- **Safe by default:** read-only companion features come before actions that can alter the game.
- **Open development:** decisions, limitations, and contribution paths stay visible.

## Out of scope for the first milestone

- Building the complete Flutter application
- Supporting every Skyrim edition or mod immediately
- Remote gameplay or full controller replacement
- A public hosted service or account system
- A large plugin ecosystem

## Runtime compatibility

Skyrim Special Edition 1.6.1170 pauses the game whenever its window loses focus, which would make the
pairing code and the companion app unusable the moment a player switches to it. The Bridge forces
Skyrim's always-active setting on by default (`bridge/README.md`'s "Runtime compatibility options") so
the game keeps running while DovahLink has focus; this is a runtime compatibility fix, not a gameplay
feature, and can be disabled per-player through the Bridge's own configuration.

## Open questions

- Which Skyrim editions should be supported first?
- Which game values are stable and useful enough for the first client?
- What should the minimum connection and pairing flow look like?
- Which information belongs in the bridge, and which belongs in the client?
