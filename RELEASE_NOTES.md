# Legs Mod v1.0.0

First public v1.0 release.

- Adds client-side arm and hand visuals with configurable offsets.
- Adds current and legacy Photon presence markers with retry and diagnostic logging.
- Applies remote visuals locally when a compatible marker is detected.
- Restores the original pose near the computer keyboard and owned-cosmetics wardrobe equip controls.
- Leaves movement colliders unchanged.
- Includes `PurpleMoonieee` company metadata and the display name `Legs Mod`.
- Downloads as `LegsMod.dll`, ready to copy into the BepInEx plugins folder.

## Known limitation

Cross-client visibility is not confirmed. The latest user report says other players still do not see the modded appearance. Both clients need compatible builds; use the Photon diagnostics in the README and test with a second client before treating mod-sided visibility as working.
