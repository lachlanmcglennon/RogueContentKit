# Third-party notices

The RCK Pack ships the components below. Each one is unmodified and keeps its own licence; the full licence texts are
in `licenses/` in this repository and in `RCK-licenses\` in the RCK Pack zip.

RCK itself (`RCK/`) and RogueLibsPlus (`RogueLibsPlus/`) are MIT, copyright (c) 2026 RCK contributors and RogueLibsPlus
contributors: see `RCK/LICENSE`, `RogueLibsPlus/LICENSE` and the root `LICENSE`.

| Component | Version | Files in the pack | Licence | Licence text |
|---|---|---|---|---|
| [RogueLibs](https://github.com/Chasmical/RogueLibs) by Chasmical (Abbysssal); release by Dzhake | [v4.0.0-rc.3](https://github.com/Dzhake/RogueLibs/releases/tag/v4.0.0-rc.3) | `BepInEx\plugins\RogueLibsCore.dll` | MIT | `RogueLibs.LICENSE.txt` |
| RogueLibsPatcher (part of RogueLibs, the copy embedded in the rc.3 DLL) | Gen2, from v4.0.0-rc.3 | `BepInEx\patchers\RogueLibsPatcher.Gen2.dll` | MIT | `RogueLibs.LICENSE.txt` |
| [BepInEx](https://github.com/BepInEx/BepInEx) by the BepInEx team | [5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) | `BepInEx\core\BepInEx*.dll`, `HarmonyXInterop.dll`, `0Harmony20.dll` | MIT | `BepInEx.LICENSE.txt` |
| [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) by NeighTools | [4.5.0](https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0) | `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` | LGPL-2.1 | `UnityDoorstop.LICENSE.txt` |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) by the BepInEx team | 2.9.0 | `BepInEx\core\0Harmony.dll` | MIT | `HarmonyX.LICENSE.txt` |
| [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike (the library HarmonyX is based on) | 2.0 compatibility | `BepInEx\core\0Harmony20.dll` | MIT | `Harmony.LICENSE.txt` |
| [MonoMod](https://github.com/MonoMod/MonoMod) by 0x0ade | 22.01.29.01 | `BepInEx\core\MonoMod.RuntimeDetour.dll`, `MonoMod.Utils.dll` | MIT | `MonoMod.LICENSE.txt` |
| [Mono.Cecil](https://github.com/jbevain/cecil) by Jb Evain | 0.10.4 | `BepInEx\core\Mono.Cecil*.dll` | MIT | `Mono.Cecil.LICENSE.txt` |

BepInEx, UnityDoorstop, HarmonyX, Harmony, MonoMod and Mono.Cecil come from the official
`BepInEx_win_x64_5.4.23.5.zip` release, byte for byte, with their `.xml` documentation files. UnityDoorstop's source
code is at the link above. BepInEx 5 is MIT-licensed (the LGPL-2.1 licence on BepInEx's main branch belongs to
BepInEx 6, which the pack doesn't use).

## Custom Content Utilities

RCK is inspired by, and compatible with, [Custom Content Utilities](https://github.com/Freiling87/CCU) (CCU) by
Freiling87 (Ted Bunny), the original custom-content mod for Streets of Rogue. RCK is written from scratch and contains
no CCU code, text or art. It reuses only the names that campaigns and characters store (trait, mutator, goal and
option names), so that content made for CCU keeps working. Its trait descriptions are its own.

## Streets of Rogue

Streets of Rogue is made by Matt Dabrowski and published by tinyBuild. The RCK Pack contains no game files.
