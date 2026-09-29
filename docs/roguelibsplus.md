# RogueLibsPlus

The RCK Pack ships Dzhake's RogueLibs v4.0.0-rc.3 release unmodified. RogueLibsPlus (RL+) is a separate BepInEx
plugin that fixes bugs in RogueLibs and the game that affect content mods, and adds a few small APIs that RCK uses.

- Plugin GUID `streetsofrogue.roguelibsplus`, namespace `RogueLibsPlus`, MIT licence (`RogueLibsPlus/LICENSE`).
- It needs RogueLibs (`abbysssal.streetsofrogue.roguelibscore`). RCK needs RogueLibsPlus.

## What gets installed

| File | What it is |
| --- | --- |
| `BepInEx\plugins\RogueLibsCore.dll` | Dzhake's v4.0.0-rc.3 release, SHA256 `357B53E477A63FFE282AFA631B1D2BF13EEB4389B1D544FFF8117FE714259E1C` |
| `BepInEx\patchers\RogueLibsPatcher.Gen2.dll` | The preloader embedded in that DLL, SHA256 `9135EF2EE2538816CF2BEFAAB5F84B275196AB8D59D0CC8F3C611C131CEF0E78` |
| `BepInEx\plugins\RogueLibsPlus\RogueLibsPlus.dll` | This add-on |
| `BepInEx\plugins\RCK\RCK*.dll` | RCK |

RogueLibs still needs its preloader: it adds the fields that RogueLibs keeps its hooks in. Without it, RogueLibs
falls back to slower lookups, writes the preloader itself and asks for a restart. The pack installs the same copy
that RogueLibs would write, under the same file name.

## Version guard

- Each fix is applied on its own. If a fix can't find what it needs, it is skipped with one warning
  (`Fix '<name>' skipped: <reason>`) and the other fixes still apply.
- Fixes to the game and the public APIs work with any RogueLibs 4.0. Fixes to RogueLibs itself only apply on
  RogueLibs 4.0.0-rc.3; on any other version they are skipped with one warning.
- The log line reads `RogueLibsPlus 1.0.0 on RogueLibs 4.0.0-rc.3: N fixes applied, M skipped.` With rc.3, all 9 fixes
  apply. The main menu shows `RL+ v1.0.0`; if a fix is skipped, the line turns red and gives the count.

## Fixes

### Save and continue

Continuing a saved run keeps custom items, traits and status effects working. Before, the game restored them without
the RogueLibs hooks that make them work.

### A broken button no longer breaks the whole interaction menu

- A mod's interaction or button that throws an error is skipped, and the other buttons still show.
- When a pressed button throws, the menu still closes cleanly, so the object can be used again.
- The error in `BepInEx\LogOutput.log` names the button, the object and the mod that owns the code
  (`Assembly: Type.Method`). Errors while the menu is being built are logged once per source; button presses are
  logged every time.
- Turntables: hacking them again while the bad music is still playing no longer throws; it plays the "can't do"
  sound, as the game does.

### Menus rebuilt after an interaction ended

Investigating an object, e.g. reading a Computer's text, could close the window at once and stutter, because the menu
was rebuilt a frame after the interaction ended. The menu is now only rebuilt while someone is still using the object.

### Unlocks work in every menu

- The Twitch *Rewards* and *Level Feelings* menus and the arcade *Character Select* and *Achievements* menus work again
  with RogueLibs installed.
- Unlock settings (such as whether a trait is in the character creator, or the Augmentation Booth prices) can be read
  outside their own menu without an error. The booth prices fall back to the open booth, then to the game's defaults.
- In co-op, each player's menu acts on its own buttons (e.g. both players picking a level-up trait at once).
- Twitch vote numbers on trait buttons match the button, and the disaster menu checks the right vote setting.
- The level-up trait menu updates its buttons, and toggling the *Super Special Abilities* mutator refreshes the
  character select slots.

### Custom items in the level editor and in chunks

- Every custom item from a loaded mod is listed in the editor's item lists (*Items* on the ground, and the contents of
  chests, safes and agents), sorted by name, before *Money*. Abilities are never listed. A mod can leave an item out
  with `[HideInLevelEditor]`.
- Placing a custom item on the editor's *Items* layer no longer throws, and the tile shows.
- A chunk, chest or agent that names an item from a mod that isn't loaded skips it and logs
  `Skipped unknown item '<name>'` once per name, instead of spawning a broken item.
- A `Nugget` in a chest's or agent's contents is skipped with one warning; before, the level hung at
  "100% - SETUPMORE4". Nuggets on the floor are unchanged.
- Limit: the editor tile uses the sprite named after the item. An item whose sprite has a different name shows a
  placeholder sprite in the editor, but spawns correctly in the game.

## Public API

All in the `RogueLibsPlus` namespace:

- `EditorItems.GetCustomItems()`, `IsKnownItem(name)`, `RegisterNonItemPrefix(prefix)` and `IsNonItemName(name)`.
  `RegisterNonItemPrefix` marks names that start with `prefix` as data, not items: they are never spawned or logged,
  and a chest or agent gets an empty placeholder item instead. RCK registers its investigate-text prefix here.
- `CustomItemFactory` extension methods: `Items()`, `Contains(name)` and `TryGetMetadata(name, out metadata)`, plus
  `ItemFactoryExtensions.CanListItems`.
- `CustomItemMetadata.IsHiddenInLevelEditor()` (an extension method) and `[HideInLevelEditor]` for `CustomItem` classes.
- `MenuLines.Set(id, text)`, `SetProblem(id, text)`, `Get(id)` and `Refresh()`: lines at the bottom of the main menu,
  next to the game's version. RL+ and RCK show their versions and any install problems here.

## Known differences from earlier builds

- In a scrolling menu that isn't RogueLibs' own, pressing a trait unlock in the level-up or Augmentation Booth swap
  menu, or an unlocked item in the rewards menu, runs the game's own code rather than the RogueLibs unlock's.
- Mods built against an older RogueLibs fork's `RogueLibsCore.EditorItems` or `CustomItemFactory.Items` need
  recompiling against RogueLibsPlus. RCK is the only one we know of.
