# Horizontal Directional Drilling

Starter Captain of Industry mod under `Ligvigfuis mods`.

## Included

- Loadable `IMod` entry point
- Validated JSON configuration and startup logging
- Manifest and safe initial save metadata
- Stable prototype IDs for planned buildings, products and research
- Localization-ready strings
- Original SVG icon asset
- Changelog, release metadata and Visual Studio solution
- Packaging compatible with `Build-Mod.ps1`

## Build and install

From `Ligvigfuis mods`:

```powershell
.\Build-Mod.ps1 HDD
```

The script builds the mod, verifies its manifest and primary DLL, backs up an
existing installation, and installs the resulting files into the game Mods
directory.

## Current scope

Version 0.1.0 is a deliberately safe foundation release. It loads in the game
and performs startup/configuration diagnostics, but it does not yet register
prototypes or alter save data or gameplay.

## Implementation plan

1. Define the drilling-pad and receiving-pad entity layouts.
2. Add consumable drill pipe and drilling fluid products and recipes.
3. Prototype a route-planning dependency and curved underground path model.
4. Add terrain, obstacle, length, depth and connection validation.
5. Connect a construction simulation to materials, power and maintenance.
6. Register research and unlock progression.
7. Persist route state with explicit save-version migration.
8. Add Unity assets, inspector controls, previews and localization files.

## Save compatibility

This release registers no save-persistent types and declares itself removable.
When route or entity state is added, update `can_remove_from_saved_game` and
implement version-gated serialization before publishing that feature.
