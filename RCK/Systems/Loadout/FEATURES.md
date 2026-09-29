# RCK.Loadout features

RCK.Loadout lets campaign makers control custom NPC starting items, money, and chunk-specific keys with compatibility traits.

With a Loader trait, the custom character's Items page becomes a pool for NPCs. The NPC receives only the rolled subset, and the same NPC keeps the same roll on reload. Players are not changed by the loader. A `Nugget` on the Items page is ignored by the loader so NPC spawning does not alter the player's nugget count. For example, an Items pool can include `Banana`, `Rock`, `Shovel`, `BaseballBat`, and `Pistol`, and slot traits decide which subset is granted.

## Traits

| Trait ID | Category | What it does |
| --- | --- | --- |
| `Chunk_Key` | Loadout/Chunk Items | This NPC starts with a key for a locked door in its chunk or sector and is marked as a key holder. |
| `Chunk_Mayor_Badge` | Loadout/Chunk Items | This NPC starts with a Mayor Badge and is marked as the badge holder. |
| `Chunk_Safe_Combo` | Loadout/Chunk Items | This NPC starts with a safe combination for a safe in its chunk or sector. |
| `Chunk_Stash_Hint` | Loadout/Chunk Items | Accepted for compatibility; it does not create a stash-hint item. |
| `Accuracy_Mod` | Loadout/Gun Nut | Legacy compatibility trait kept so older characters load; it has no active effect. |
| `Ammo_Stock` | Loadout/Gun Nut | Legacy compatibility trait kept so older characters load; it has no active effect. |
| `Rate_of_Fire_Mod` | Loadout/Gun Nut | Legacy compatibility trait kept so older characters load; it has no active effect. |
| `Rubber_Bullets` | Loadout/Gun Nut | Legacy compatibility trait kept so older characters load; it has no active effect. |
| `Silencer` | Loadout/Gun Nut | Legacy compatibility trait kept so older characters load; it has no active effect. |
| `Flat_Distribution` | Loadout/Loader | This NPC rolls each Items-page slot evenly, with a possible no-item result unless forced. |
| `Scaled_Distribution` | Loadout/Loader | This NPC rolls Items-page loadouts with cheaper items more likely. |
| `Upscaled_Distribution` | Loadout/Loader | This NPC rolls Items-page loadouts with expensive items more likely. |
| `Bankrupt_25` | Loadout/Money | This NPC has a 25% chance to lose all starting money. |
| `Bankrupt_50` | Loadout/Money | This NPC has a 50% chance to lose all starting money. |
| `Bankrupt_75` | Loadout/Money | This NPC has a 75% chance to lose all starting money. |
| `Broke` | Loadout/Money | This NPC's starting money becomes 1 to 6 cash, unless a bankrupt roll removes it. |
| `Desperate` | Loadout/Money | This NPC's starting money becomes 6 to 11 cash, unless a bankrupt roll removes it. |
| `Poor` | Loadout/Money | This NPC's starting money becomes 11 to 26 cash, unless a bankrupt roll removes it. |
| `Prosperous` | Loadout/Money | This NPC's starting money becomes 26 to 41 cash, unless a bankrupt roll removes it. |
| `Rich` | Loadout/Money | This NPC's starting money becomes 41 to 61 cash, unless a bankrupt roll removes it. |
| `Wealthy` | Loadout/Money | This NPC's starting money becomes 81 to 100 cash, unless a bankrupt roll removes it. |
| `Zillionaire` | Loadout/Money | This NPC's starting money becomes 1000 cash, unless a bankrupt roll removes it. |
| `FunnyPack` | Loadout/Pockets | This NPC can roll up to 2 pocket items from its Items-page pool. |
| `FunnyPack_Extreme` | Loadout/Pockets | This NPC can roll up to 3 pocket items from its Items-page pool. |
| `FunnyPack_Pro` | Loadout/Pockets | This NPC can roll enough pocket items to empty most Items-page pools. |
| `Have` | Loadout/Pockets | This NPC's pocket roll cannot choose no item when the pool has something to give. |
| `Have_Mostly` | Loadout/Pockets | This NPC's pocket roll has only a 25% no-item chance. |
| `Have_Not` | Loadout/Pockets | This NPC's pocket roll has a 50% no-item chance. |
| `Equipment_Chad` | Loadout/Slots | This NPC's equipment slots cannot choose no item when their pools have something to give. |
| `Equipment_Enjoyer` | Loadout/Slots | This NPC's equipment slots have only a 25% no-item chance. |
| `Equipment_Virgin` | Loadout/Slots | This NPC's equipment slots have a 50% no-item chance. |
| `Sidearmed` | Loadout/Slots | This NPC can roll up to 2 items in each equipment slot. |
| `Sidearmed_but_on_Both_Sides` | Loadout/Slots | This NPC can roll up to 3 items in each equipment slot. |
| `Sidearmed_to_the_Teeth` | Loadout/Slots | This NPC can roll enough equipment items to empty most slot pools. |

## Legacy names

Accepted from older content and recorded without a loadout effect on their own: `Accuracy_Mod`, `Ammo_Stock`, `Rate_of_Fire_Mod`, `Rubber_Bullets`, `Silencer`.
