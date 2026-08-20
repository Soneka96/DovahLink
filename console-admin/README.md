# Trust-administration console adapter (optional)

This directory is not part of the native DovahLink Bridge core (`bridge/`). It is an optional
in-game console integration for administering DovahLink's persistent trust store — listing,
revoking, resetting, and blocking/unblocking known devices — documented in
[`ai/context/protocol/security.md`](../ai/context/protocol/security.md)'s "Trust administration
surface" section, which records the full design decision and rationale.

DovahLink Bridge works completely normally without anything in this directory installed. The
bridge attempts to register its native Papyrus functions
(`bridge/game_state/commonlib_trust_admin_papyrus_adapter.cpp`); the files here are only what makes
those functions reachable from Skyrim's in-game console.

## Commands

Once installed (see below), the following work at Skyrim's console:

```text
dovahlink list
dovahlink list trust
dovahlink list block
dovahlink help
dovahlink revoke -id <shortId>
dovahlink block -id <shortId>
dovahlink unblock -id <shortId>
dovahlink forget -id <shortId>
dovahlink reset trust
dovahlink reset
dovahlink reset -confirm <code>
```

`<shortId>` is the five-digit administration-only identifier printed by `dovahlink list` — never
the client's real (long) identity, and never a credential. `dovahlink list` lists every known device
with its current state, sorted oldest-to-newest by creation time; repeated display names receive
temporary `#1`, `#2`, ... suffixes in that presentation only. `dovahlink list trust` shows only
trusted devices, and `dovahlink list block` shows only blocked devices. `dovahlink help` prints
descriptions for every command. `block` targets a trusted or revoked known device by `<shortId>` —
an unpaired device is not eligible; `unblock` targets a blocked device by `<shortId>`, returning it
to unpaired.
`forget` deletes a known device's record entirely and frees its `<shortId>` for future allocation,
but only from revoked or unpaired — a trusted device must be revoked first, and a blocked device
must be explicitly unblocked first; `forget` never implicitly lifts a block.
`reset trust` (Reset Trust) immediately converts every currently trusted device to revoked, cancels
any pending pairing, and disconnects each affected session; recoverable, since every device stays
known and can re-pair. `reset` starts the separate Factory Reset confirmation challenge (a six-digit
code, printed in Skyrim, expiring after 60 seconds) without changing anything yet; `reset -confirm
<code>` confirms it with a matching code, permanently erasing every known device record and
revocation tombstone and invalidating every session, including developer-token ones. A wrong
confirmation code cancels the challenge; start over with `reset`. See
[`ai/context/protocol/security.md`](../ai/context/protocol/security.md)'s "Administrative session
invalidation" for the full Reset Trust/Factory Reset distinction.

## Dependency

This adapter requires [ConsoleUtil Extended](https://github.com/KrisV-777/ConsoleUtil-Extended), a
third-party SKSE plugin, installed separately as its own mod. DovahLink Bridge does not bundle it
and does not check for its presence at plugin load — if it (or the files in this directory) are
missing, `dovahlink list`/`dovahlink list trust`/`dovahlink list block`/`dovahlink help` and the
mutation commands are simply unrecognized console commands, exactly like any other unknown input;
nothing else about the bridge is affected.

**Known compatibility risk, not yet resolved:** ConsoleUtil Extended's own build instructions
require CommonLibSSE's `powerof3/dev` branch, a different lineage than the `commonlibsse-ng-flatrim`
fork DovahLink Bridge itself vendors (see [`bridge/README.md`](../bridge/README.md)'s "Dependency
baselines"). The two plugins have not been verified to coexist in the same load order against this
project's pinned Skyrim runtime. Confirm this before relying on the integration.

## Supported runtime

Same as the rest of DovahLink Bridge: Steam Skyrim `1.6.1170` with SKSE `2.2.6` (see
[`bridge/README.md`](../bridge/README.md)'s "Supported runtime"). This adapter introduces no
additional runtime requirement of its own on the DovahLink Bridge side; ConsoleUtil Extended's own
supported-runtime range is whatever that mod separately documents.

## Install steps

1. Compile `DovahLinkAdmin.psc` into `DovahLinkAdmin.pex` using Creation Kit's Papyrus Compiler (or
   an equivalent standalone compiler), against Skyrim's own `TESV_Papyrus_Flags.flg` and source
   folders. The compiled `.pex` is not committed to this repository — build artifacts are never
   checked in.
2. Place the compiled `DovahLinkAdmin.pex` in `Data\Scripts\` (the shared Papyrus script folder
   every mod's compiled scripts live in — not colocated with DovahLink Bridge's native `.dll`,
   which installs separately under `Data\SKSE\Plugins\`).
3. Place `dovahlink.yaml` in `Data\SKSE\CustomConsole\`, per ConsoleUtil Extended's documented
   config location.
4. Install ConsoleUtil Extended itself, per its own instructions.

## Verification status

Not yet manually verified end-to-end in a real Skyrim session — this repository's automated tests
cannot exercise a live Papyrus VM or a third-party plugin
([`ai/context/skse/testing.md`](../ai/context/skse/testing.md)'s "Manual verification"). Before
relying on this adapter, confirm in-game that:

- `dovahlink list`, `dovahlink list trust`, `dovahlink list block`, `dovahlink help`,
  `dovahlink revoke -id <shortId>`, `dovahlink reset trust`, `dovahlink reset`,
  `dovahlink reset -confirm <code>`, `dovahlink block -id <shortId>`,
  `dovahlink unblock -id <shortId>`, and `dovahlink forget -id <shortId>` are recognized and
  produce the expected output.
- ConsoleUtil Extended tolerates a `global native` Papyrus function body (its own documentation
  never states this explicitly, only that functions must be `global` — every other SKSE
  native-Papyrus-function integration works this way, but this specific combination is unconfirmed).
- ConsoleUtil Extended and DovahLink Bridge coexist without conflict in the same load order, given
  the CommonLibSSE branch-lineage difference noted above.
- Whether ConsoleUtil Extended requires `dovahlink.yaml`'s filename to match its own `name: dovahlink`
  field, or accepts any filename — its documentation does not state this either way.
- Whether ConsoleUtil Extended actually supports a two-word sub-command name (`reset trust`) and
  registering the same sub-command name twice with different required arguments (`reset` bare
  versus `reset -confirm <code>`) routing to the distinct native functions `dovahlink.yaml` binds
  them to — this file's best reading of ConsoleUtil Extended's documented syntax, not yet confirmed
  against its actual parser.

Record the outcome using `bridge/README.md`'s "Manual verification record template" pattern.
