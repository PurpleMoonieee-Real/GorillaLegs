# Legs Mod

A client-side Gorilla Tag BepInEx plugin that gives the rendered arms and hands a leg-like pose.

- Plugin ID: `purplemoonieee.leg.mod`
- In-game name: `Legs Mod`
- Version: `1.0.0`
- Windows file metadata company: `PurpleMoonieee`

## Current status

The local visual pose is implemented. The mod advertises a Photon player-property marker so compatible modded clients can recognize one another and apply the visual locally. The latest report says other players still do not see the modded appearance, and cross-client rendering has not been confirmed. Treat mod-sided visibility as experimental in this release; the marker logs can help diagnose it, but a solo test cannot verify that another client rendered your rig.

Both clients need a compatible build for mod-sided visuals. The mod does not transmit arm poses or change movement colliders. Each client renders the visual locally after detecting the marker. Clients without the mod see the normal Gorilla Tag rig.

## Install

1. Install BepInEx 5 for the PC version of Gorilla Tag.
2. Download `LegsMod.dll` from the [v1.0.0 release](https://github.com/PurpleMoonieee-Real/GorillaLegs/releases/tag/v1.0.0).
3. Copy `LegsMod.dll` into `Gorilla Tag/BepInEx/plugins/`.
4. Restart the game.

The first launch creates `BepInEx/config/purplemoonieee.leg.mod.cfg`. Defaults are a 0.35 m shoulder drop, 0.029 m hand raise, and 0.08 m movement toward the torso. The original hands and shoulders are restored near the computer keyboard and owned-cosmetics wardrobe equip controls. Interaction colliders remain unchanged.

## Multiplayer diagnostics

Check `BepInEx/LogOutput.log` after joining a room:

- `Published current and legacy cosmetic presence markers.` means the mod attempted to publish its marker.
- `Photon presence update: ... local=True, markerPresent=True` means the local client observed the marker in its Photon properties.
- `Detected a compatible modded peer (actor ...)` means this client detected another compatible modded player.

Only a second compatible client can confirm end-to-end visibility. A mirror or solo log check cannot establish that another player rendered your appearance.

## Build from source

Requirements: .NET SDK, the .NET Framework 4.7.2 targeting pack, BepInEx 5, and a PC Gorilla Tag installation with its managed assemblies.

```powershell
dotnet build .\LegsMod.sln -c Release -p:GorillaTagDir="C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag"
```

Set `GorillaTagDir` to your game folder if it is installed elsewhere. The project references game and BepInEx assemblies from that installation; those dependencies are not bundled.

## License

No license is included. All rights remain with the author unless a license is added.
