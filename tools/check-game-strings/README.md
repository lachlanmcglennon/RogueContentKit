# check-game-strings

Role-aware static checker for RCK strings passed to Streets of Rogue APIs. It harvests vanilla names from a C# decompile of the game's `Assembly-CSharp.dll` (by default `.ref\decomp`, or `$env:SOR_DECOMP`) and `sorcampaigns` harvested vocab (by default the `sorcampaigns` checkout next to this repository), extracts RCK literals by call pattern, and fails on unknown game-string uses that are not explicitly allowlisted with a reason.

Run from the repository root:

```powershell
python tools\check-game-strings\check_game_strings.py
```

Optional paths:

```powershell
python tools\check-game-strings\check_game_strings.py `
  --decomp <Assembly-CSharp decompile folder> `
  --data <sorcampaigns checkout>\data
```

Traits and status effects are checked as separate kinds. A vanilla trait name passed to `AddStatusEffect`, `hasStatusEffect` or a status-effect table fails with a "wrong kind" hint, and a status-effect name passed to `AddTrait`, `EnsureTrait` or a trait table fails the same way.

- Status effects come from the cases in `GetStatusEffectTime`, `GetStatusEffectHate`, `isPositiveStatusEffect`, `IsAntidoteEffect` and `AddStatusEffect`, from every `AddStatusEffect("X"` literal in the decompile, and from the armor contents in `InvItem.cs` (`ItemFunctions.EquipArmor` applies each one as a status effect).
- Traits come from `new Unlock("X", "Trait"`, the cases in `AddTrait` and `RemoveTrait`, and every `AddTrait(`/`hasTrait(` literal in the decompile.
- In CombatModule, the `DrugStatuses` and `PermanentStatuses` values are checked as status effects and the `PermanentTraits` values as traits.

Use `allowlist.tsv` only for deliberate non-vanilla strings, or for a verified name used as a different kind on purpose. Each non-comment row is tab-separated:

```text
role    value    one-line reason
```

`tools\verify-ccu.ps1` runs this check on full runs and fails the build on any unallowlisted unknown, so `tools\freeze.ps1` builds include it.

Dialogue keys registered by RCK with `RogueLibs.CreateCustomName("NA_<key>", NameTypes.Dialogue, ...)` count as known dialogue names.

The checker runs read-only and does not require Streets of Rogue or BepInEx to be running.
