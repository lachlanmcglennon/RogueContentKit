# RCK.Objects features

RCK.Objects lets campaign makers add investigate text and one-time searchable container items to supported vanilla objects.

## Object support

| Object ID | Feature | What it does |
| --- | --- | --- |
| `Altar` | Investigate text | Text starting with `investigateable-message:::` opens an **Investigate** reader; the editor shows a long text entry. |
| `ArcadeGame` | Investigate text | Same Investigate reader and editor support. |
| `Boulder` | Investigate text | Same Investigate reader and editor support. |
| `Computer` | Investigate text | Same Investigate reader and editor support. |
| `Gravestone` | Investigate text | Same Investigate reader and editor support. |
| `Jukebox` | Investigate text | Same Investigate reader and editor support. |
| `Podium` | Investigate text | Same Investigate reader and editor support. |
| `Speaker` | Investigate text | Same Investigate reader and editor support. |
| `Television` | Investigate text | Same Investigate reader and editor support. |
| `Window` | Investigate text | Same Investigate reader and editor support. |
| `Barbecue` | Container item, fire particle | **Search** gives the configured item once; the editor shows an item picker. Burning objects block search unless the player resists fire. |
| `Bathtub` | Container item | **Search** gives the configured item once; the editor shows an item picker. |
| `Bed` | Container item | Same container support. |
| `Desk` | Container item | Same container support. |
| `Fireplace` | Container item, fire particle | Same container support, with burning-object gating. |
| `FlamingBarrel` | Container item, fire particle | Same container support, with burning-object gating. |
| `GasVent` | Container item | Same container support. |
| `Plant` | Container item | Same container support. |
| `PoolTable` | Container item | Same container support. |
| `Refrigerator` | Container item | Same container support. |
| `Shelf` | Container item | Same container support. |
| `Stove` | Container item | Same container support. |
| `Toilet` | Container item | Same container support. |
| `TrashCan` | Container item | Same container support. |
| `Tube` | Container item | Same container support; live tubes are not searchable. |
| `VendorCart` | Container item | Item search works; special vendor-cart anger and noise are not recreated. |
| `WaterPump` | Container item | Same container support. |
| `Well` | Container item | Same container support. |
| `Vanilla chests/safes/hidden bombs` | Coexistence | Existing `Open`/`Search` containers are left to vanilla. |

## Container search rules

- **Search** gives the item as a vanilla chest would: a loaded gun, full durability or charges, or a normal stack. A `Money` entry gives 11-25 cash, adjusted for co-op player count. A `Nugget` entry adds 1 nugget and does not need inventory space.
- The player sees "Found: <item>".
- If the inventory has no room, the item stays in the container for later.
- Unknown item names are skipped and the object stays non-interactable. Hats and armour (`CopHat`, `Fedora`,
  `BulletproofVest` and the like) are real items and work.
- `Randomized` and `None` entries are ignored.
- Containers remain searchable on later levels when pooled objects are reused.
