# RCK.Quests features

RCK.Quests lets any NPC give the player jobs with story text: a scripted chain written into the NPC's Talk text, or a
generated ("radiant") odd job. Jobs are per level, need no chunk quest slot and aren't limited to the vanilla quest
givers, so a level can have as many as it needs. The machine-readable spec is `extensions.quests` in
[ccu-interface.json](../../../docs/ccu-interface.json).

| Name | Kind | What it does |
| --- | --- | --- |
| `rck-quest:::` | Talk text prefix | The NPC is a quest giver; the rest of the text is its script. |
| `RCK_Radiant_Quests` | Mutator | Up to 4 NPCs in each level offer one generated job. |
| `RCK_Radiant_Quest_Giver` | Designer trait | The NPC always offers a generated job, with or without the mutator. |
| `RCK_No_Quests` | Designer trait | The NPC is never picked to give a generated job. |
| `RCK_Quest_Target_A` to `_D` | Designer traits | Marks the NPC for a script's `Target: Label:A` (to D). |
| `Quest=id+id` | Level gate switch | True once every listed job is done and paid on this level. |

## Scripted jobs

Give an NPC a Talk text that starts with `rck-quest:::`. The rest is a script of one or more jobs ("stages"),
separated by lines holding just `---`. Each stage is a set of `Key: value` lines. A line that doesn't start with a key
carries on the value above it, so long texts can wrap.

```text
rck-quest:::
Title: Loose Lips
Type: Kill
Target: Agent:Big Tony
Reward: $150, Item:Revolver
Offer: {Target} has been talking to the cops. Make sure he stops.
Accept: Good. Don't come back until it's done.
Remind: {Target} is still breathing.
Done: The street's quieter already. Here.
---
Title: Special Delivery
After: loose-lips
Type: Deliver
Item: Briefcase
Target: Agent:Fence
Reward: XP, Standing
Report: Auto
Offer: Take this to the Fence across town. Don't open it.
Target text: About time. Tell {giver} we're square.
Idle: Nothing more for you today.
```

Keys ignore case, spaces, `_` and `-`:

| Key | Meaning |
| --- | --- |
| `Id` | The name used by `After` and the `Quest=` gate. Default: the title, lower-cased with `-` for spaces. |
| `Title` | Shown on the `Take the job` button. |
| `Type` | `Kill`, `Retrieve`, `Destroy`, `Deliver` or `Talk` (see below). |
| `Target` | Who or what the job is about (see below). |
| `Item`, `Count` | The item and how many (1 to 99) for Retrieve and Deliver; how many to take down for a Faction Kill. |
| `Reward` | A comma-separated list (see below). |
| `Report` | `Giver` (default): come back and press `Report back`. `Auto`: paid the moment the job is done. |
| `After` | Job ids joined with `+` or `,` that must be done on this level first. Chains can span several givers. |
| `Offer`, `Accept`, `Remind`, `Done`, `Fail` | What the giver says when offering, on accepting, while it runs, when paying, and when it fails. |
| `Target text` | What a Deliver or Talk recipient says. |
| `Wait` | What the giver says while `After` isn't met, or while the target can't be found yet. |
| `Idle` | Said once every stage is over (only needed once in a script). |

Job types:

| Type | Aliases | The job is done when |
| --- | --- | --- |
| `Kill` | `Neutralize`, `Hit` | The whole target set is down: killed, knocked out, arrested, ghosted or zombified all count. |
| `Retrieve` | `Fetch`, `Steal` | The player brings the giver `Count` of `Item`. With a `Target`, the item is put on the first living matching NPC (or into the first intact matching object) when the job is taken; without one, the player has to find it. Always reported to the giver. |
| `Destroy` | `Sabotage` | Every matching object is wrecked. Needs an `Object:` target. |
| `Deliver` |  | The player hands `Count` of `Item` (default `Briefcase`), given on accepting, to any living matching NPC with its `Hand over` button. A full inventory refuses the job. |
| `Talk` | `Message` | The player uses `Pass on the word` on any living matching NPC. |

Targets:

| Target | Matches |
| --- | --- |
| `Label:A` (to `D`) | Every NPC in the level with `RCK_Quest_Target_A` (to D). |
| `Agent:Name` | NPCs whose name or agent type is `Name`, ignoring case. |
| `Agent:#id` | The NPC with that agent ID. |
| `Leader:<faction>` | The faction's `<key>_Leader` holders. `Leader:Rival` picks the giver's bitterest rival present with a leader standing. |
| `Faction:<faction>` | The faction's living members when the job is taken. A Kill needs `Count` of them (default 3). `Faction:Rival` picks the bitterest rival with members standing. |
| `Object:Name[@owner]` | Objects called `Name`. `@<faction>` keeps only those owned by the faction's members (same owner ID and chunk), `@Rival` the bitterest rival's, `@Giver` the giver's own. |

A faction is a number or a key (`Crepe`, `Faction_1`).

Rewards:

| Reward | Gives |
| --- | --- |
| `$N` or `Money:N` | Money (1 to 100000). |
| `Item:Name`, `Item:Name x3`, `Item:Name*3` | Items. What doesn't fit drops at the player's feet. |
| `XP` | Experience, as for a vanilla completed mission. |
| `Recruit` | The giver joins the party. A faction leader sends its nearest non-leader member instead. Vanilla's follower cap applies. |
| `Standing` | Every member of the giver's faction turns Friendly toward the player. |

Texts can use `{giver}`, `{faction}`, `{rival}`, `{target}`, `{item}`, `{count}`, `{reward}` and `{objective}`.
Factions read as groups (`the Crepes`, or a `[RCK]FactionName::` name). A capitalised placeholder (`{Target}`)
capitalises its value. Texts are cut to 600 characters, including the job and reward lines RCK adds.

How it plays:

- Stages are offered one at a time, in order. The giver shows `Hear them out` and `Take the job (Title)`; while the job
  runs, `About the job`; once it's done, `Report back`. After the last stage, `Talk` says the `Idle` text.
- Jobs are marked on the map, never in the quest list. A giver with a job on offer gets vanilla's `!` quest marker
  (faint on the map until the player has seen them), a faded `!` while its job runs, and the tick once there's
  something to report. Targets, recipients and whoever holds a wanted item get a yellow arrow (at most 8 per job).
  Hovering a map icon names the NPC or object and the job (`The Bartender - job: Loose Lips`). Vanilla's own quest
  markers win: an NPC or object already in a vanilla quest keeps its marker. In multiplayer only the host sees them.
- A job fails, and the rest of the chain with it, if the giver falls or turns Hostile, or if every Deliver or Talk
  recipient falls. The giver says the `Fail` text.
- Givers that are Hostile or Annoyed toward the player offer nothing. Paying a reward makes the giver Friendly.
- A stage with a mistake is dropped, and the BepInEx log says why. The rest of the script still works.
- Quest givers lose their vanilla Talk button, since the script replaces the Talk text.

## Radiant jobs

With the `RCK_Radiant_Quests` mutator, up to 4 NPCs per level (at least 6.4 units apart, faction members with a rival
present first) offer one generated job each. `RCK_Radiant_Quest_Giver` holders always offer one on top, and their own
Talk text comes back once the job is over.

- A faction member whose faction has a rival in the level wants the rival's leader hit, 3 of the rival thinned out, an
  item recovered from a rival member, a rival-owned machine sabotaged, or a parcel or a word taken to a fellow member.
- Anyone else wants a Thief, Cannibal, Slavemaster or Vampire dealt with, or a parcel or a message taken to another
  civilian.
- Pay is $60 + $20 per level, scaled per job, plus experience. Jobs against a rival also pay Standing.
- Never picked: NPCs with Talk text (unless they hold the trait), quest givers, targets, prisoners, hired NPCs,
  `RCK_No_Quests` holders, NPCs Hostile or Annoyed toward a player, and Zombies, Gorillas, robots, Aliens, Ghosts,
  werewolves, Assassins, Shapeshifters and Slaves.
- Radiant job ids are `radiant-<agentID>`.

## Level gates

`Quest=id+id` is a level gate switch: it holds once every listed job is done and paid on this level. See
[Campaign features](../Campaign/FEATURES.md) for level gates.

## Limits

- Jobs are per level. Nothing carries over the elevator.
- Only the host runs jobs. In multiplayer, only the host player gets the quest buttons; on a client, a quest giver
  has no Talk button.
- The BepInEx log has one line per job offered, taken, done, paid and failed (at most 40 per level).
