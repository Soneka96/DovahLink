# Troubleshooting

## Skyrim pauses when I click away to use DovahLink

**Symptom:** When you click away from Skyrim to use DovahLink on another monitor, phone, or tablet, Skyrim pauses. When you click back into Skyrim, the game resumes.

**Root cause:** Skyrim Special Edition 1.6.1170 pauses the game when it loses focus.

**Solution:** The Bridge handles this natively as of version `0.2.0` -- no separate mod needed. It
forces Skyrim's `bAlwaysActive:General` setting on at startup by default, so the game keeps running
while DovahLink has focus. If you previously installed a third-party "Always Active" mod for this,
you can remove it; the Bridge's own setting takes effect regardless.

To disable this (for example, to restore Skyrim's default pause-on-focus-loss behavior), create
`Data/SKSE/Plugins/DovahLinkBridge.ini` next to your other SKSE plugin INI files with:

```ini
[DovahLink]
bAlwaysActive=0
```

See [`bridge/README.md`](bridge/README.md)'s "Runtime compatibility options" for the full set of INI
keys, including the achievement-compatibility patch this same file controls.

**Affected versions:** Skyrim Special Edition 1.6.1170 with SKSE64 2.2.6
