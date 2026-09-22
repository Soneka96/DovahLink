# Troubleshooting

## Skyrim pauses when I click away to use DovahLink

**Symptom:** When you click away from Skyrim to use DovahLink on another monitor, phone, or tablet, Skyrim pauses. When you click back into Skyrim, the game resumes.

**Root cause:** The supported Skyrim Special Edition 1.7.104 runtime pauses the game when it loses focus.

**Solution:** The native Adapter handles this natively as of version `0.2.0` -- no separate mod
needed. It forces Skyrim's `bAlwaysActive:General` setting on at startup by default, so the game
keeps running while DovahLink has focus. If you previously installed a third-party "Always Active"
mod for this, you can remove it; the Adapter's own setting takes effect regardless.

To disable this (for example, to restore Skyrim's default pause-on-focus-loss behavior), create
`Data/SKSE/Plugins/DovahLinkAdapter.ini` next to your other SKSE plugin INI files with:

```ini
[DovahLink]
bAlwaysActive=0
```

The same file also controls the achievement-eligibility runtime patch that keeps achievements
available with SKSE plugins loaded, through a second key in the same section:

```ini
[DovahLink]
bAchievementCompat=0
```

Both keys default to enabled (`1`); a missing file, missing key, or malformed value falls back to
that default per-key rather than failing plugin load.

**Affected versions:** Skyrim Special Edition 1.7.104 with SKSE64 2.3.1

The current Adapter accepts only this exact Skyrim/SKSE runtime pair; Skyrim Special Edition
1.6.1170 with SKSE64 2.2.6 is historical context, not a supported target for the current Adapter.

## The Adapter is disabled with a generic SKSE load error

**Symptom:** SKSE reports `dovahlink_adapter_plugin.dll: disabled, fatal error occurred while loading plugin` without a more specific reason.

If the diagnostic reports `logger with name 'global' already exists`, the Adapter is colliding with
the logger that SKSE/CommonLib has already registered. DovahLink uses its own `DovahLinkAdapter`
logger name; this is a logging-initialization failure, not a Skyrim, SKSE, or Address Library version
mismatch.

The Adapter emits synchronous startup breadcrumbs through Windows debug output before and during
`SKSEPluginLoad`. Launch Skyrim through SKSE64 with a debugger attached, or use Microsoft's
DebugView, and filter for:

```text
DovahLink Adapter startup stage:
DovahLink Adapter startup failure at stage:
```

The last reported stage identifies the startup boundary that failed. If an exception escapes plugin
startup, the diagnostic also reports its message and the Adapter returns a normal SKSE load failure
instead of allowing the exception to collapse into SKSE's generic message. The regular logs remain
under `Documents\My Games\Skyrim Special Edition\SKSE`, including `skse64.log` and
`DovahLinkAdapter.log`.

If no DovahLink startup marker appears, the failure occurred before the Adapter reached its
instrumented entry point, or the launch was not observed by a debugger/debug-output viewer; inspect
the SKSE loader log and the deployed DLL's dependencies next.
