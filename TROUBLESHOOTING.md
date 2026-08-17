# Troubleshooting

## Skyrim pauses when I click away to use DovahLink

**Symptom:** When you click away from Skyrim to use DovahLink on another monitor, phone, or tablet, Skyrim pauses. When you click back into Skyrim, the game resumes.

**Root cause:** Skyrim Special Edition 1.6.1170 pauses the game when it loses focus. This behavior cannot be disabled through configuration files.

**Solution:** Install [Skyrim Always Active](https://www.nexusmods.com/skyrimspecialedition/mods/56432) from Nexus Mods. This SKSE plugin keeps Skyrim running even when the game window is not active.

1. Install the mod through Vortex or manually into your Skyrim folder
2. Restart Skyrim
3. The pause-on-focus-loss behavior should be gone

**Affected versions:** Skyrim Special Edition 1.6.1170 with SKSE64 2.2.6
