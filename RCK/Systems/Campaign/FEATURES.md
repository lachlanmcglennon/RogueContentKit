# RCK.Campaign features

RCK.Campaign exposes compatibility goals, campaign mutators, level gates, conversion helpers, and follower spawning for custom campaigns.

## Goals and scene setters

Use these IDs as default goals or scene-setter goals on spawned agents.

| ID | Stored goal | What it does |
| --- | --- | --- |
| `Arrested` | `Arrested` | Spawned agent is immediately arrested/neutralized. |
| `Burned` | `Burned` | Spawned agent is killed as burned/on fire. |
| `Commit Arson` | `CommitArson` | Listed in editor; no custom arson brain behavior implemented. |
| `Dead` | `Dead` | Spawned agent is killed. |
| `Electrocuted` | `Electrocuted` | Applies long electrocution. |
| `Electrocuted (Permanent)` | `Electrocuted_Permanent` | Applies permanent relay effect. |
| `Flee Danger` | `FleeDanger` | Listed in the editor; it has no custom brain behavior on its own. |
| `Frozen` | `Frozen` | Applies long Frozen effect. |
| `Frozen (Fragile)` | `Frozen_Fragile` | Frozen agent shatters on damage. |
| `Frozen (Permanent)` | `Frozen_Permanent` | Applies permanent relay effect. |
| `Gibbed` | `Gibbed` | Immediately gibs spawned agent. |
| `Knocked Out` | `KnockedOut` | Immediately neutralizes as tranquilized. |
| `Panic` | `Panic` | Listed in the editor; it has no custom brain behavior on its own. |
| `Random Patrol (Chunk)` | `Random_Patrol_Chunk` | Walks between random spots in its start chunk, pausing 2 to 4.5 s at each. See "Patrol and wander goals" below. |
| `Random Patrol (Map)` | `Random_Patrol_Map` | Crosses the map between random public spots at least a chunk apart, pausing 1 to 2 s. Street-side for street innocence. |
| `Random Teleport` | `RandomTeleport` | Legacy name accepted from older content; converted where possible. |
| `Random Teleport (Duo)` | `Teleport_Duo` | Teleports like Public and spawns one follower of the leader's own kind. See "Followers" below. |
| `Random Teleport (Gang)` | `Teleport_Gang` | Teleports like Public and spawns three followers of the leader's own kind. |
| `Random Teleport (Private + Prison)` | `Teleport_Prison` | Listed in editor; private/prison tile picker not recreated. |
| `Random Teleport (Private)` | `Teleport_Private` | Listed in editor; private tile picker not recreated. |
| `Random Teleport (Public)` | `Teleport_Public` | Teleports to a random public tile at least 20 units (about 31 tiles) from the level start (the starting point, else the up elevator), as vanilla keeps its random events from the elevators. After 50 tries it takes the farthest spot found; with no spot it stays put. |
| `RobotClean` | `RobotClean` | Listed in editor; custom robot-clean brain not implemented. |
| `Wander Between Agents` | `WanderAgents` | Walks up to a random NPC within 2 chunks of its start, then another. |
| `Wander Between Agents (Non-Owner)` | `WanderAgentsNonOwners` | The same, among NPCs within 2 chunks outside its owner group. |
| `Wander Between Agents (Owner)` | `WanderAgentsOwners` | The same, among the NPCs of its owner group (same owner ID and start chunk), wherever they are. |
| `Wander Between Objects (Non-Owner)` | `WanderObjects` | Walks between objects within 2 chunks that its owner group doesn't own. |
| `Wander Between Objects (Owner)` | `WanderObjectsOwned` | Walks between the objects its owner group owns (same owner ID and chunk). |
| `Zombified` | `Zombified` | Spawns as zombified/dead zombie setup. |

## Mutators, gates, and quest helpers

These IDs are accepted in campaign or level mutator lists. Level-gate strings are written by hand in the campaign data.

| ID | What it does |
| --- | --- |
| `Big_Quest_Mandatory` | Accepted as a campaign mutator ID for compatibility. |
| `Client_Network` | Accepted as a campaign mutator ID for compatibility. |
| `Continue2` | Accepted as a campaign mutator ID for compatibility. |
| `Homesickness_Disabled` | Accepted as a campaign mutator ID for compatibility. |
| `Homesickness_Mandatory` | Accepted as a campaign mutator ID for compatibility. |
| `Insert_Nuggets` | Accepted as a campaign mutator ID for compatibility. |
| `Nap_Time` | Accepted as a campaign mutator ID for compatibility. |
| `No_Maps` | Accepted as a campaign mutator ID for compatibility. |
| `No_Open_Carry` | Accepted as a campaign mutator ID for compatibility. |
| `[CCU] Homesickness Mandatory` | Accepted from older content and converted when possible. |
| `[CCU]LevelGate::...` | Parses Entry gates at exit elevators: integer/A-D labels, Agent switches, AND/NAND/NOR/OR/XNOR/XOR, plus RCK's `Count=N`, conditions (`Routed`, `TurfTaken`, `Destroyed`, `Holding`, `Quest`) and an `Open=` message. `[RCK]LevelGate::...` is read too. The mutator list shows either as `[RCK] Level Gate`. See "Level-gate strings" below. |
| `LevelGateMenuHead` | Accepted as the level-gate configurator name; the configured data strings do the work. |
| `Agent_Switch_A` | Hidden switch trait for label 1. True when the agent is resolved (dead, KO, hired/following, dismissed, arrested, zombified, or exited). |
| `Agent_Switch_B` | Hidden switch trait for label 2. |
| `Agent_Switch_C` | Hidden switch trait for label 3. |
| `Agent_Switch_D` | Hidden switch trait for label 4. |
| `Agent_Switch_# / Switch_#` | Recognized if present on loaded agents, for integer labels. |
| `Quest_Giver` | This NPC is marked important for custom quest setups; full procedural quest generation is not active yet. |
| `TraitConversions` | Legacy trait names are normalized in custom character data before spawn. |
| `MutatorConversions` | Legacy mutator names are normalized in active/original challenge lists. |

### Level-gate strings

RCK reads both `[CCU]LevelGate::...` and `[RCK]LevelGate::...`: `;`-separated `Key=value` pairs. Entry gates can use integer labels or labels A-D, Agent switches, and AND, NAND, NOR, OR, XNOR, or XOR logic. A switch is true when its agent is resolved: dead, knocked out, hired or following, dismissed, arrested, zombified, or exited.

Example: `[CCU]LevelGate::Type=Entry;Label=1;Switch=Agent;Logic=AND;` with an `Agent_Switch_A` NPC blocks the exit until that NPC is resolved.

RCK adds these to CCU's grammar. Labels are optional once there's a condition.

| Key | Example | The exit opens when |
| --- | --- | --- |
| `Count` | `Label=A;Count=3;` | At least 3 of the label agents are resolved (3 of the 4 Summit bosses). Replaces `Logic`; 1 to 99. |
| `Routed` | `Routed=Faction_2` or `Routed=Crepe+Blahd` | Every listed faction's last `<key>_Leader` fell this level (RCK.Social). |
| `TurfTaken` | `TurfTaken=Blahd` or `TurfTaken=Blahd+Crepe` | Every listed faction held turf when the level loaded and holds none now (RCK.Social; see [turf capture](../Social/FEATURES.md#turf-capture)). |
| `Destroyed` | `Destroyed=Generator` or `Destroyed=Generator+PowerBox` | At least one object of each name was destroyed this level and none of that name is left standing. |
| `Holding` | `Holding=Briefcase`, `Holding=Briefcase*2+Key` | The player at the exit carries the items (stacks count). |
| `Quest` | `Quest=loose-lips+the-drop` | Every listed RCK quest is done (RCK.Quests). |

The label part (if any) and every condition must hold. A gate with an unknown condition stays shut; the BepInEx log warns once, the first time a player tries the exit. When the exit refuses, the log lists what isn't met yet (at most every 3 s). Gates are checked on the host: in multiplayer, the host's state decides.

Example: `[RCK]LevelGate::Label=A,B,C,D;Count=3;Destroyed=Generator;` opens once 3 of the 4 label agents are resolved and every Generator is destroyed.

`Open=text` shows a message the first time every condition of the gate holds, once per level, to the host's players, in the same style as the turf messages. The text is at most 120 characters (longer text is cut, with a log warning) and can't contain `;`, which ends the key. `=` is fine. A gate with only `Open=` and no conditions never opens, so it never shows.

Example: `[RCK]LevelGate::Type=Entry;TurfTaken=Blahd+Mafia+Cop+Thief+Hacker;Open=The Crepes run the city. The exit is open.;`.

## Patrol and wander goals

The game runs each of these goals as its own `WanderFar`, so the NPC fights, flees, investigates and reacts like any wanderer; RCK picks every stop.

- A stop is a spot 0.3 to 1.3 units from the target NPC or object; the NPC pauses 2 to 4.5 s there (1 to 2 s for Random Patrol (Map)). It never picks the same target twice in a row.
- Targets: NPCs are living non-players that aren't ghosts or object agents, nor Hostile or Annoyed toward the walker. Objects skip doors, windows, bars, wire, lockdown walls, security, hazards, bushes, trees, plants, pipes and elevators.
- Owner groups use the walker's owner ID (above 0) and start chunk. Non-owner and plain targets must be within 20 units (2 chunks) of the walker's start.
- With no NPC or object to visit, a Wander Between goal patrols its start chunk instead.
- Random Patrol (Map) stops are random public tiles, at least 10 units away when possible.
- Home is where the NPC spawned. The host picks stops; clients follow the synced movement.

## Items and effects

| ID | What it does |
| --- | --- |
| `ClassAWare` | Accepted as an item ID for older content; it has no use effect on its own. |
| `Debugulizer` | Accepted as an item ID for older content; it has no use effect on its own. |
| `RubberBulletsMod` | Registered as a gun-mod category item; bespoke use effects are not recreated. |
| `Electrocuted_Permanent` | Maintains vanilla Electrocuted permanently. |
| `Frozen_Fragile` | Keeps the agent frozen and makes it shatter on damage. |
| `Frozen_Permanent` | Maintains vanilla Frozen permanently. |

## Followers

`Random Teleport (Duo)` and `Random Teleport (Gang)` spawn followers beside the leader after it teleports.

- A follower uses the leader's own agent type. Custom leaders copy their name, look, traits, and items; faction traits such as `Crepe_Aligned` copy too. A custom leader with no character data falls back to `Gangbanger`.
- Followers get the `Follow` goal and the leader as employer.
- Followers are loyal to the leader, the leader is aligned with each follower, and followers of the same leader are aligned with each other.
- The link lasts for the level where it was made. If a follower is hired away or the pooled agent is reused later, the link is dropped.

## Legacy names

Legacy names such as `Random Teleport`, `[CCU] Homesickness Mandatory`, `[CCU]LevelGate::...`, `TraitConversions`, and `MutatorConversions` are accepted from older content and converted where possible.
