# Legacy CCU compatibility vocabulary

RCK reads campaigns, chunks, characters and saves made for Custom Content Utilities (CCU) unchanged. To do that it
keeps the names that content stores. These names are data formats, not RCK's name, and are never renamed.
Everything RCK names itself uses `RCK`: the plugin, DLLs, log source, display tags and button keys.

The full list of stored names is in [ccu-interface.md](ccu-interface.md).

## Stored names kept as they are

| What | Where content stores it |
|---|---|
| Trait IDs: all 744 CCU traits, e.g. `Drug_Dealer`, `Plot_Critical`, `Faction_1_Aligned` | character files, saves |
| Renamed-trait conversions (25): older trait names are converted to the current ones | older characters |
| Mutator names, e.g. `Homesickness_Disabled`, `No_Maps`, and the old `[CCU] Homesickness Disabled` / `[CCU] Homesickness Mandatory` | campaign and level mutator lists |
| Default-goal names (28), e.g. `Dead`, `Frozen (Fragile)`, `Random Teleport (Public)` | chunk agent spawners |
| Item and status-effect names: `ClassAWare`, `Debugulizer`, `RubberBulletsMod`, `Electrocuted_Permanent`, `Frozen_Fragile`, `Frozen_Permanent` | inventories, saves |
| Investigate text: `investigateable-message:::<text>` | extraVarString of Altar, ArcadeGame, Boulder, Computer, Gravestone, Jukebox, Podium, Speaker, Television, Window |
| Container item: the item name as the whole extraVarString | Barbecue, Bathtub, Bed, Desk, Fireplace, FlamingBarrel, GasVent, Plant, PoolTable, Refrigerator, Shelf, Stove, Toilet, TrashCan, Tube, VendorCart, WaterPump, Well |
| Level Gate: `[CCU]LevelGate::Type=Entry;Label=1;Switch=Agent;Logic=AND;` | level mutators |
| Level Gate switch traits `Agent_Switch_A` to `Agent_Switch_D` | character files |
| Level Gate menu heading `LevelGateMenuHead` | mutator unlock name |

## RCK names, with an older spelling still read

| Older spelling | Write this in new content |
|---|---|
| `[CCU]FactionRel::` | `[RCK]FactionRel::` |
| `[CCU]LevelGate::` (the stored CCU format above) | either; `[RCK]LevelGate::` is read too |

## Renamed from CCU

Nothing in campaigns stores these, so RCK uses its own names.

| CCU | RCK |
|---|---|
| Plugin `CCU` in `BepInEx\plugins\CCU` (`CCU.dll`, `CCU.<Module>.dll`) | Plugin `RCK` (GUID `streetsofrogue.roguecontentkit`) in `BepInEx\plugins\RCK` (`RCK.dll`, `RCK.<Module>.dll`). RCK won't load while the CCU plugin is installed, and the main menu says so. |
| Display tags `[CCU]`, `[CCU+]` | `[RCK]`, `[RCK+]` |
| Buttons `CCU_OfferMotivation`, `CCU_LearnEnglish`, `CCU_Learn_<language>` | `RCK_OfferMotivation`, `RCK_LearnEnglish`, `RCK_Learn_<language>` |
| Dialogue `NA_CCU_Thanks`, `NA_CCU_NeedItem` | `NA_RCK_Thanks`, `NA_RCK_NeedItem` |
| Spawn tag `CCUTeleport` | `RCKTeleport` |

RCK's own additions use the `RCK_` trait prefix, the `[RCK+]` display tag and the `rck-` extra-var prefix.
