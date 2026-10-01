# Changelog

All notable changes to the RCK Pack are listed here, newest first. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Version numbers follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html); [docs/versioning.md](docs/versioning.md) says what a
change in each number means for players and campaign makers. The pack's version is RCK's version; the release notes of
each version list the versions of the components inside (RogueLibsPlus, RogueLibs and BepInEx).

## [Unreleased]

## [1.1.0] - 2026-10-01

### Added

- Faction members are welcome in the private areas of friendly factions. A player who joined a faction through a
  faction trait (`<key>_Member` or `<key>_Aligned`) can walk into, open the doors of and talk in the rooms of NPCs who
  share that faction or whose factions are Friendly or Aligned toward it, without being told to get out. Stealing,
  breaking things, hacking, fires, bombs, opening prison cells and attacks are still crimes. See
  [Social features](RCK/Systems/Social/FEATURES.md#private-areas-of-friendly-factions).
- Faction roles. `<key>_Vengeful` NPCs turn on anyone who attacks or kills a member of their faction, for the rest of
  the level. When the last `<key>_Leader` of a faction falls, the faction is routed: its members make peace with the
  players and flee from fights. An `RCK_Faction_Defector` NPC that joins a player's party is disowned by its old
  factions, and both sides fight. Every faction key has a Vengeful and a Leader trait. See
  [Social features](RCK/Systems/Social/FEATURES.md#faction-roles-vengeful-leader-and-defector).
- Faction disguises. With the `RCK_Faction_Disguises` mutator, a player wearing a faction's headpiece (a blue hat for
  the Crepes, a red one for the Blahds, a cop hat for the law, and so on) passes for one of its members: welcome in its
  rooms and on its turf, and its members calm down. Get caught doing a crime or hit someone in front of them and the
  disguise is blown for the level. Fallen members drop their hats. `[RCK]Disguise::` changes which headpiece is which,
  and `[RCK]FactionName::` names factions in on-screen messages. See
  [Social features](RCK/Systems/Social/FEATURES.md#faction-disguises).
- Quests with story text from any NPC. An NPC whose Talk text starts with `rck-quest:::` gives a chain of jobs written
  into it: take down a target, fetch or steal an item, wreck an object, deliver a parcel or pass on a message, for money,
  items, experience, a recruit or the faction's friendship, with the giver's own lines for every step. Jobs can wait on
  other jobs, even another giver's. The `RCK_Radiant_Quests` mutator has a few NPCs offer generated odd jobs:
  faction members want a rival's leader hit, its numbers thinned, goods recovered or gear wrecked. Jobs are marked on
  the map with vanilla's quest icons (a `!` on givers with work, a tick to report back, yellow arrows on targets and
  recipients) but stay out of the quest list; in multiplayer only the host sees them. `Quest=` is a level
  gate switch. See [Quests features](RCK/Systems/Quests/FEATURES.md).
- Cut items that the game ships with art but never made work now do something. The BFG fires a rocket with a huge
  explosion. The Grappling Hook pulls you to walls or pulls people to you. The Tripmine is a thrown trap that goes off
  near enemies. The Force Field makes you invincible for 8 s. The Magic Lamp grants a random wish. The Voodoo Doll
  takes over a person for 30 s. The Augmentation Canister gives a completely random trait at an Augmentation Booth.
  The Five-Leaf and Six-Leaf Clovers are stronger Four-Leaf Clovers. The Mood Ring warns about people who secretly
  mean you harm. A Rag and a Beer or Whiskey make a Molotov Cocktail. See [Item features](RCK/Systems/Items/FEATURES.md).
- Patrol and wander goals. The default goals CCU listed but never ran now work: `Random Patrol (Chunk)` and
  `Random Patrol (Map)` walk between random spots, and `Wander Between Agents` and `Wander Between Objects` (plain,
  Owner and Non-Owner) visit nearby NPCs or furniture in turn. Patrolling NPCs still fight, flee and investigate like
  any wanderer. See [Campaign features](RCK/Systems/Campaign/FEATURES.md#patrol-and-wander-goals).
- Smarter level gates. A level gate can now need `Count=3` of its label agents (3 of 4 bosses), a routed faction
  (`Routed=`), destroyed objects (`Destroyed=Generator`), items in the exiting player's pockets (`Holding=Briefcase`)
  or finished quests (`Quest=`), and labels are optional. When the exit refuses, the log says what's still missing. See
  [Campaign features](RCK/Systems/Campaign/FEATURES.md#level-gate-strings).
- Faction war. Wars now move on their own. With `RCK_Turf_Capture`, a crew's turf falls when its last holder goes down
  and the faction that took it moves in to guard it (`TurfTaken=` is a level gate condition). `[RCK]FactionRaid::`
  schedules raids (`Crepe>Blahd@120`), and the `RCK_Faction_Raids` mutator has factions that hate each other raid on
  their own every few minutes. A `<key>_Calls_Backup` NPC that is attacked or raises an alarm brings a squad of its
  faction after the attacker. An `RCK_Broker` NPC sells truces between two factions and frames one for hitting
  another. Raiders and backup are copies of the faction's own members, never street-innocent, and vanish when done.
  They set out from their faction's turf nearest the target, out of your sight, and carry their weapons.
  See [Social features](RCK/Systems/Social/FEATURES.md#faction-war).
- Faction respawn. With `RCK_Faction_Respawn`, a faction that holds turf sends fresh squads to it when its numbers run
  low (by default 2 + 2 per turf held, at most 12, every 60 s), restaffs its empty posts and, with `RCK_Turf_Capture`,
  marches on empty or rival turf to take it. A faction is only finished once all its turf is gone.
  `[RCK]FactionRespawn::` turns it on per faction and tunes it: an unlimited or counted pool, the target, cooldown and
  squad size (`Crepe=inf;Blahd=20;Crepe.Size=4;`). Squads can be made of chosen characters, custom ones included:
  give a placed NPC `<key>_Reinforcement`, or name them (`Crepe=inf:Gangbanger+Crepe Heavy`). Raids, backup and turf
  guards use them too. See [Social features](RCK/Systems/Social/FEATURES.md#respawn).
- Turf map. Whenever a level has turfs, the big map shows who holds each one: its floor is tinted in the holder's
  colour, cleared turf is grey, shared turf is checkered and turf under attack is striped. A legend lists each
  faction's turf count, marks your factions and greys out routed ones. F8 hides and shows it (`[Map]` in
  `streetsofrogue.roguecontentkit.cfg`). Host only. See [Social features](RCK/Systems/Social/FEATURES.md#turf-map).
- No in-fighting. The `RCK_No_Infighting` mutator gives every NPC the vanilla "No In-Fighting" trait, including
  vanilla NPCs, squads and anything spawned later, so aligned NPCs stop hurting each other in crossfire. Players don't
  get it; their followers do. See [Social features](RCK/Systems/Social/FEATURES.md#no-in-fighting).
- Level gate messages. `Open=text` in a level gate shows its text once, the first time the gate's conditions all hold
  (`Open=The Crepes run the city. The exit is open.`). See
  [Campaign features](RCK/Systems/Campaign/FEATURES.md#level-gate-strings).
- Command your faction. With `RCK_Commander` (or `[RCK]Command::`), you lead one faction in the turf war: its own
  respawn, raids, backup and expansion stop, and from the commander console (T) you spend control points to recruit
  squads of chosen units, custom ones included. The console has a map of the whole level with every turf, racket and
  squad on it: click a squad, then a turf to attack it (or defend it if it's yours), or open ground to move there.
  Hovering shows what a click would do before you click, a lone or newly raised squad takes orders without picking,
  and the title shows your CP income. A squad sent to defend elsewhere releases its old turf, and a recruit hired by a
  player leaves its squad and the cap.
  Squads can also regroup on you or disband. Declare war on a faction to take its turf. The other factions fight on
  by themselves. The console and the war panel use the game's own font. Their keys, T and G, are free in SoR's
  default controls; if your own controls use one, the console or panel says so and names the setting to change (test
  builds' F9 and F6 move to T and G once). See
  [Social features](RCK/Systems/Social/FEATURES.md#commander).
- Control points and the war panel. Factions earn control points from their turf and rackets, captures and kills
  (`[RCK]ControlPoints::` tunes it), and the war panel (G, or the `RCK_War_Panel` mutator) ranks every faction by
  power with its turf, rackets, members and points. See
  [Social features](RCK/Systems/Social/FEATURES.md#control-points).
- Turf expansion and rackets. With `RCK_Turf_Expansion` (or `[RCK]TurfWar::`), factions send small parties to take
  empty turf near their own, and to run protection rackets on the commoners' places: the commoners side with their
  racketeers until the guards are beaten or the commoners are gone. A ruined racket gets a new owner a little later
  (`Reopen=`, 3 times a level by default). Rackets show on the turf map. See
  [Social features](RCK/Systems/Social/FEATURES.md#expansion).
- Racketeers. A player with `RCK_Faction_Racketeer` can shake down a free racket's commoner, with vanilla's odds, to
  make the racket join their faction. A Mobster's own shakedown does the same. See
  [Social features](RCK/Systems/Social/FEATURES.md#racketeer).
- Faction medics. An `RCK_Faction_Medic` NPC heals the most hurt friend nearby every few seconds. See
  [Social features](RCK/Systems/Social/FEATURES.md#faction-medic).

### Changed

- The main menu and the BepInEx log show exactly which build you have. A release shows its version (`RCK v1.0.1`); a
  test build between releases adds the commits since the last release and the commit it was built from
  (`RCK v1.0.0+3.gabc1234`). RogueLibsPlus does the same (`RL+ v1.0.0+3.gabc1234`).
- New [docs/versioning.md](docs/versioning.md): what the version numbers mean, which versions get fixes, and
  pre-releases.
- `LICENSE` is now the plain MIT licence text, so GitHub shows the repository as MIT. What it covers moved to the
  README's "Credits and licence" section and to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- RCK merchant types stock the newly working items (for example the Rag at `Fire_Sale`, the Grappling Hook at
  `Hardware_Store`, the Augmentation Canister at `Tech_Mart`) and no longer sell items that still do nothing in the
  game (the Blowtorch, Matches, Map, Saw, Power Drill, Jackhammer, Sticky Mine, Laser Blazer, Zombie Reanimator, Rope,
  Ballet Shoes and Vision Detector).

### Fixed

- NPCs that RCK spawns during a level are armed. The game only arms NPCs placed in the level, so the
  `Random Teleport (Duo)` and `(Gang)` followers came with bare fists. Now each gets the weapons and items its leader
  was placed with, else its type's usual random weapon; a custom character gets its character's items.
- The Sniper Rifle fires. The game has the item but never fired it: it only played the shoot animation, for players
  and NPCs. Now each shot is one heavy round (45 base damage, against the Revolver's 16) with about 1.5 s between
  shots, the Revolver's bang and recoil, and aim one Accuracy level better. Silencers and gun mods work on it, and it
  has a description. See [Combat features](RCK/Systems/Combat/FEATURES.md#sniper-rifle).
- Members of one party no longer fight each other. Followers of the same leader (a player or an NPC, including RCK
  squads) keep their Loyal, Aligned or Submissive links whatever their faction, Guilty or relationship traits say, also
  after the elevator. For example, a hired Blahd and a recruited Crepe of the same player, or a Guilty hire and a
  recruited cop of The Rookie, stay at peace.
- `MapMarker_Pilot` NPCs show on the map. The trait made a marker but left it invisible, as vanilla does for anyone
  but shopkeepers and the like; now, once you've seen the NPC, it shows as a blue arrow with its name.
- Furniture holding a hat or armour as its container item (`CopHat`, `HatRed`, `Fedora`, `BulletproofVest` and the
  like) can be searched for it. RCK took these real items for unknown ones and left the furniture unsearchable.
- Appearance randomisation rolls each look on its own. The skin colour, hairstyle and "not the skin colour" picks
  reused the next roll instead of moving it on, so one NPC's skin, hairstyle and shirt or trouser colour could come
  from the same roll and some mixes never came up. Randomised NPCs look different from before, still the same on
  every machine.
- Squads you order somewhere walk there straight away. A member already holding a post stood still until something
  disturbed it, such as you pushing it, because the game only sends a guard to its post when it starts guarding. Far
  from you, its brain then switched off. This hit attacks, moves, defends, recalls and squads holding a turf they took.

## [1.0.0] - 2026-09-29

The first public release: RCK (Rogue Content Kit), a custom-content mod for Streets of Rogue campaigns, packed with
everything it needs in one zip. Campaigns and characters made for Custom Content Utilities (CCU) work unchanged.

### Added

- The RCK Pack zip: BepInEx 5.4.23.5, RogueLibs 4.0.0-rc.3 (by Chasmical, released by Dzhake, unmodified),
  RogueLibsPlus 1.0.0 and RCK 1.0.0. Extract it into the game folder; extracting a newer zip over it updates it.
- RCK: designer traits for NPCs, NPC goals, mutators, factions, investigate text, container items and more, under the
  names CCU campaigns already use.
- Designer mode, a button at the top of the character creator's Traits list that shows the designer traits.
- Menu lines for RogueLibs, RogueLibsPlus and RCK, and a red line that says what to fix when the install is incomplete
  or CCU is still installed.
- Signs tagged `[RCK REQUIRED]` in a campaign are removed when RCK is installed, so a campaign can warn players who
  don't have it.

[Unreleased]: https://github.com/lachlanmcglennon/RogueContentKit/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/lachlanmcglennon/RogueContentKit/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/lachlanmcglennon/RogueContentKit/releases/tag/v1.0.0
