# RCK.Social features

RCK.Social gives campaign makers relationship, faction, hire, and recruiting traits for custom NPCs and players.

## Faction traits

There are 37 faction keys: `Faction_1` to `Faction_20`, plus `Blahd`, `Crepe`, `Cannibal`, `Gorilla`, `Soldier`, `Firefighter`, `Scientist`, `Mafia`, `Upper_Cruster`, `Vampire`, `Werewolf`, `Common_Folk`, `Slavemaster`, `Cop`, `Zombie`, `Thief`, and `Hacker`. Each key supports these traits:

| Trait | What it does |
| --- | --- |
| `<key>_Aligned` | The holder is a member of the faction, and members start Aligned with each other. |
| `<key>_Hostile` | The holder and every member start Hateful (hate 5) to each other. |
| `<key>_Annoyed` | The holder and every member start Annoyed: a standoff that one provocation turns into a fight. |
| `<key>_Territorial` | The holder and every member start Annoyed, and each side turns Hateful toward the other on its own turf (below). |
| `<key>_Friendly` | The holder and every member start Friendly: no attacks, but they don't join each other's fights. |
| `<key>_Neutral` | The holder and every member start Neutral, even where vanilla would make them enemies (truces). |

CCU content that already uses `Faction_1..4_Aligned/_Hostile` and named pairs keeps the same IDs. The other keys are RCK additions shown in the character creator as `[RCK+] Rel Faction - ...`. The six grades for one key cancel each other.

Named factions also count matching vanilla agents as members:

- `Blahd` is `GangbangerB`; `Crepe` is `Gangbanger`.
- `Cannibal`, `Gorilla`, `Soldier`, `Firefighter`, `Scientist`, `Vampire`, `Slavemaster`, `Thief`, and `Hacker` match the same-named agents.
- `Mafia` is `Mafia` or `MafiaAligned`. `Upper_Cruster` is `UpperCruster` or `UpperCrusty`.
- `Werewolf` is `WerewolfB` or a transformed `Werewolf`. `Zombie` is `Zombie` or any zombified agent.
- `Cop` is `Cop`, `Cop2`, `CopBot`, any enforcer, or a `TheLaw` holder.
- `Common_Folk` is the `Common_Folk` trait, or a human NPC with no gang who is not law, inhuman, zombified, electronic, or one of the named groups above.

Custom characters are always `Custom` agents, so they join named factions through `<key>_Aligned`. `<key>_Member` is a player trait that only makes the player a member. Other characters' faction traits then decide the relationship: `Faction_3_Aligned` NPCs align with a `Faction_3_Member` player, and `Faction_3_Hostile` NPCs hate that player.

Rules are mutual. The strongest matching faction rule wins: Hateful, then Territorial, Annoyed, Friendly, Aligned, Neutral. For example, `Faction_3_Hostile` plus `Blahd_Aligned` on the same pair ends Hateful, and `Crepe_Territorial` beats `Crepe_Annoyed`.

### Territorial relationships

`<key>_Territorial` and matrix `=Territorial` make factions defend their own turf without starting map-wide fights.

- A territorial pair starts Annoyed. It turns Hateful when one side sees the other standing on its own turf.
- Turf is owned floor in the holder's starting chunk. A street NPC with owner 0 has no turf and stays Annoyed.
- Residents with the same owner and starting chunk, dead agents, object agents, empty mechs, and PropertyDeed holders are not treated as trespassers.
- Hateful is sticky for the rest of the level.
- The matrix form is directed: `Crepe>*=Territorial` makes Crepes territorial toward everyone else, while `1<>2=Territorial` makes both sides territorial.

### Faction roles: Vengeful, Leader and Defector

Four role traits per key, and one defector trait, act on events during play. They don't set relationships when NPCs meet, and they don't make the holder a member: add `<key>_Aligned` for that. They're shown as `[RCK+] Rel Faction - ...` and don't cancel the grades.

| Trait | What it does |
| --- | --- |
| `<key>_Vengeful` | When someone attacks or kills a member of the faction, the holder turns Hateful (hate 5) toward the attacker for the rest of the level. |
| `<key>_Leader` | When the last living leader of the faction falls, the faction is routed for the rest of the level. |
| `<key>_Calls_Backup` | When an outsider attacks the holder, or it raises an alarm, the faction sends a squad after the attacker. See [Backup](#backup). |
| `<key>_Reinforcement` | The holder's character is a unit the faction's squads are made of. See [Squad units](#squad-units). |
| `RCK_Faction_Defector` | When a player takes the holder into the party, its old factions disown it and both sides turn Hateful. |

Vengeful:

- Every living holder takes revenge, wherever it is and whether or not it saw the attack. Holders that spawn later in the level bear the same grudge. Only the attacker is targeted, not its party.
- An attack is a hit the game reports as an assault (melee, bullets, thrown items, explosions), or a kill, knockout or arrest the game credits to the attacker. As in vanilla, duels, zombies and fights between two players don't count.
- Players can be the members avenged (`<key>_Member`), so `Cop_Vengeful` NPCs protect a `Cop_Member` player.
- There's no revenge on a member of the faction, on the holder's own party-mates, or on an agent the holder is Aligned, Loyal or Submissive to. `Relationless` holders take none, and a routed faction takes no more.
- Self-defence counts: a member that strikes first and is hit back is still avenged.

Leader:

- A leader falls when it dies, is knocked out or is arrested; one that will get back up (a resurrection) doesn't count. While another leader of the same faction lives, nothing happens. A leader a scene setter puts down at the start of the level (`Dead`, `Knocked Out`, `Arrested` and the like) never led, so its fall doesn't count.
- When the faction is routed, every living NPC member outside a player's party turns Neutral toward the players and their followers, with hate, strikes and turf standoffs cleared. Aligned, Loyal, Friendly and Submissive links stay. From then on it flees fights instead of joining them, unless it's `Fearless`. Members that spawn later are routed too.
- A routed member that is attacked again turns hostile as usual, but runs. One that joins a player's party stops running. Other factions' feelings don't change.

Defector:

- The old factions are the holder's `<key>_Aligned` traits and its vanilla membership (`Common_Folk` only through `Common_Folk_Aligned`), minus any faction the player belongs to as well.
- The holder and every living NPC member of those factions outside the player's party turn Hateful toward each other, even over an Aligned link, on this level and on later ones while it stays in the party. Street innocence doesn't hold this back.
- Joining an NPC's party (a Random Teleport squad) doesn't count. A routed member leaves the defector alone.

Only the host applies these rules in multiplayer. The BepInEx log has one line per grudge, rout and defection.

### Faction relationship matrix (`[RCK]FactionRel::`)

The older `[CCU]FactionRel::` spelling is also read. Put the string in a campaign's `mutatorList` or a level's `levelMutators`.

- Entries are separated by semicolons: `A>B=Rel` sets how members of A treat members of B; `A<>B=Rel` sets both ways; `A<B=Rel` is the same as `B>A=Rel`.
- A side can be a faction number (`3` means `Faction_3`), a key (`Blahd`, `Upper_Cruster`; case and underscores do not matter, so `CommonFolk` works), a comma list (`8,9`), `Player`, or `*`.
- `*` means anyone who is not on the other side. `*>*` is ignored.
- Rel can be `Hateful`, `Hostile`, `Territorial`, `Annoyed`, `Friendly`, `Aligned`, or `Neutral`.
- Entries naming both sides beat `*` entries. Within each kind, the strongest relationship wins.
- A matrix direction beats faction traits for that direction. Other general and player relationship traits can still win first.
- Example: `[RCK]FactionRel::1<>2=Hateful;3>Player=Annoyed;Crepe>Blahd=Hateful;7>*=Friendly;`.

## Examples

- Two custom NPCs with `Faction_12_Aligned` start aligned, while `Faction_12_Hostile` starts hateful toward that faction.
- `Faction_1_Aligned` and `Faction_2_Aligned` NPCs can be made hostile with `[RCK]FactionRel::1<>2=Hateful;`.
- `Faction_5_Annoyed`, `Cop_Aligned`, `Zombie_Neutral`, `Blahd_Territorial`, and `Cop_Territorial` are valid examples of the same naming rules.
- `Cannibal_Hostile` and `Soldier_Hostile` are the current forms for older hostile-to-group ideas.
- `Drug_Dealer` and `Guilty` still matter for street innocence rules.

## Legacy faction names

| Legacy name | Use instead |
| --- | --- |
| `Faction_Blahd_Aligned` | Accepted from older content; use `Blahd_Aligned` |
| `Faction_Cannibal_Aligned` | Accepted from older content; use `Cannibal_Aligned` |
| `Faction_Crepe_Aligned` | Accepted from older content; use `Crepe_Aligned` |
| `Faction_Firefighter_Aligned` | Accepted from older content; use `Firefighter_Aligned` |
| `Faction_Gorilla_Aligned` | Accepted from older content; use `Gorilla_Aligned` |
| `Faction_Soldier_Aligned` | Accepted from older content; use `Soldier_Aligned` |
| `Hostile_to_Vampire` | Accepted from older content; use `Vampire_Hostile` |
| `Hostile_to_Werewolf` | Accepted from older content; use `Werewolf_Hostile` |

## General and player relationship traits

| Trait | What it does |
| --- | --- |
| `Aligned_to_Innocent` | This NPC starts aligned with innocent targets. |
| `Hostile_to_Guilty` | This NPC starts hateful toward guilty targets, unless street innocence is hiding that guilt. |
| `Hostile_to_Scumbag` | This NPC starts hateful toward Scumbag targets. |
| `Relationless` | This NPC forces relationships involving it back to neutral. |
| `Player_Aligned` | This NPC starts aligned with players. |
| `Player_Annoyed` | This NPC starts annoyed at players. |
| `Player_Friendly` | This NPC starts friendly toward players. |
| `Player_Hostile` | This NPC starts hateful toward players. |
| `Player_Loyal` | This NPC starts loyal to players. |
| `Player_Neutral` | This NPC starts neutral toward players. |
| `Player_Secret_Hate` | This NPC secretly hates players without changing the visible relationship. |
| `Player_Submissive` | This NPC starts submissive toward players. |
| `Hostile_To_Cannibal` | Legacy compatibility trait; on load it converts to Cannibal Hostile. |
| `Hostile_To_Soldier` | Legacy compatibility trait; on load it converts to Soldier Hostile. |
| `Hostile_To_Vampire` | Legacy compatibility trait; on load it converts to Vampire Hostile. |
| `Hostile_To_Werewolf` | Legacy compatibility trait; on load it converts to Werewolf Hostile. |
| `RCK_Innocent_Until_Caught` | This NPC's hostile faction rules and Guilty flag stay hidden until another NPC sees it strike first. |

## Street innocence

Street innocence lets street-side gangs avoid immediate map-wide brawls. An innocent NPC's hostile, territorial, annoyed, or guilty results are held back until it is seen starting a fight.

| ID | Kind | What it does |
| --- | --- | --- |
| `RCK_Innocent_Until_Caught` | NPC trait | The holder is innocent until caught. The editor shows `[RCK+] Rel General - Innocent Until Caught`. |
| `RCK_Street_Innocence` | mutator | Every street-side NPC is innocent until caught, vanilla agents too. The mutator list shows `[RCK+] Street Innocence`. |

Street-side means the default goal is `WanderFar`, `Random Patrol (Map)` or a `Random Teleport` goal, or the NPC has no owner and starts on unowned floor. Drug dealers stay guilty. Players are never made innocent.

An NPC is caught when another NPC sees it strike first with melee, bullets, thrown items, explosions, or biting. Self-defence, duels, invisible attackers, aligned or loyal witnesses, dead witnesses, sleeping witnesses, ghosts, and zombified witnesses do not count. Once caught, held-back relationships are applied in both directions and only make relationships worse.

## Trait gates

| Trait | What it does |
| --- | --- |
| `Common_Folk` | This NPC becomes loyal to players with the common-folk friend trait. |
| `Cool_Cannibal` | This NPC is neutral toward players who are Cool with Cannibals. |
| `Family_Friend` | This NPC is aligned with Mafia, Mobsters and Friend of the Family players. |
| `Scumbag` | This NPC is hateful toward Scumbag Slaughterer players. |
| `Slayable` | This NPC is hateful toward Scientist Slaughterer players. |
| `Specistist` | This NPC is hateful toward Specist or gorilla-hating players. |
| `Suspecter` | This NPC starts annoyed at Suspicious players. |

Legacy gate names accepted from older content and having no effect on their own: `Bashable`, `Crushable`.

## Hiring

| Trait | What it does |
| --- | --- |
| `Cyber_Intruder` | This NPC can be hired for expert hacking-style tasks and also has a tech merchant pool. |
| `Decoy` | This NPC offers expert helper work that draws attention and starts trouble. |
| `Intruder` | This NPC can be hired for break-in and lockpicking-style tasks. |
| `Muscle` | This NPC can be hired as protection and also has a muscle merchant pool. |
| `Pickpocket` | This NPC can be hired for thief-style tasks using the closest vanilla helper flow. |
| `Poisoner` | This NPC can be hired for ruckus-style tasks using the closest vanilla helper flow. |
| `Saboteur` | This NPC can be hired for hacking-style sabotage tasks. |
| `Safecracker` | This NPC can be hired for lockpicking-style tasks. |
| `Trapper` | This NPC can be hired for lockpicking-style trap work. |
| `Homesickless` | This hired NPC travels to future floors with the player. |
| `Homesickly` | This hired NPC stays behind when the floor changes. |
| `Permanent_Hire` | This NPC can be hired permanently at 800% price and can repeat helper tasks. |
| `Permanent_Hire_Only` | This NPC only offers permanent 800% expert hire and can repeat helper tasks. |

Hire prices follow the NPC's currency trait from RCK.Merchants. For example, a `Junk_Dealer` Muscle can charge "1 Item" instead of cash.

Legacy hire names accepted from older content: `Hacker`, `HirePermanent`, `HirePermanentOnly`, `Join_on_Release`, `Join_on_Sight`.

## Faction recruiting

Faction recruiting lets a player recruit NPCs from their own factions, like vanilla gang recruiting.

| ID | Kind | What it does |
| --- | --- | --- |
| `RCK_Recruit_Free` | NPC trait | Shared-faction players get "Join me" at no cost. |
| `RCK_Recruit_Paid` | NPC trait | Shared-faction players get "Hire as protection" at the vanilla gang-hire price. |
| `RCK_Not_Recruitable` | NPC trait | Faction recruiting never offers this NPC. Its hire traits (Muscle and so on) still work. |
| `RCK_Faction_Recruit_Free` | mutator | Every faction recruits free, unless a trait or the matrix says otherwise. |
| `RCK_Faction_Recruit_Paid` | mutator | Every faction recruits paid, unless a trait or the matrix says otherwise. |
| `[RCK]FactionRecruit::` | mutator | Per-faction policies, below. |

- Membership is the same as for faction traits: NPCs through `<key>_Aligned` or vanilla membership, players through `<key>_Member`, `<key>_Aligned`, or vanilla membership. A recruit trait alone does not make anyone a member.
- The policy comes from the NPC's recruit trait, then the recruiting matrix, then the mutators. Free wins over Paid when both apply.
- `[RCK]FactionRecruit::` takes semicolon-separated `A=Policy` entries. A is a faction number, key, comma list, or `*`. Policy is `Free`, `Paid`, or `Off`. The most generous matching entry wins.
- Example: `[RCK]FactionRecruit::Cop=Free;Crepe,Blahd=Paid;*=Off;`.
- A player in a faction friendly with the NPC's factions can hire it at the Paid price when recruiting allows it.
- Recruiting is not offered for dead, arrested, imprisoned, mind-controlled, enslaved, already employed, rescue-quest, `Relationless`, Annoyed, or Hostile NPCs, nor in the home base or tutorial.
- Existing vanilla "Join me" and paid "Hire as protection" buttons are not duplicated. Free recruiting does not remove an existing paid offer.
- Vanilla follower caps, Unlikeable, No Followers, low-health refusal, `Homesickless`, `Homesickly`, `Permanent_Hire`, Hiring Vouchers, and merchant currency traits still apply.

## Party peace

Party-mates never fight each other. This is always on and has no trait or mutator.

- Two agents are party-mates when one employs the other or both have the same employer. The leader can be a player or an NPC, and RCK Random Teleport squads count.
- Relationship rules skip party-mates: the pair's own relationship traits, faction traits and the matrix, `Hostile_to_Guilty`, trait gates and street innocence. The pair keeps the Loyal, Aligned or Submissive link the game gave it when it joined, including when followers carry over to the next level.
- When someone joins a party, any territorial standoff, hate and Annoyed or Hateful relationship between the newcomer and the leader or the other party-mates is cleared.
- `Relationless` still applies.

## No in-fighting

The `RCK_No_Infighting` mutator ("[RCK+] No In-Fighting") gives every NPC the vanilla trait `DontHitAligned` ("No
In-Fighting"). That covers placed vanilla and custom NPCs, RCK squads (raids, backup, guards and respawns), and
anything spawned later, such as alarm cops and summoned followers. NPCs that already have it are skipped.

- The game blocks a hit between two agents that are Aligned, Loyal or Submissive toward each other when either one has
  the trait. So aligned NPCs stop hurting each other in crossfire, for example two vanilla Gangbangers.
- The rule works both ways: a player can't hurt an NPC that is Aligned toward them either.
- Players don't get the trait. On a player it would only add co-op immunity, show on the HUD and carry over to the next
  level. A player's followers are NPCs, so they get it.
- A sweep every 2 seconds gives it back to NPCs that lost it. Every peer runs it, as NPC traits aren't networked. The
  BepInEx log says how many NPCs got it on each level.

## Faction disguises

Turn this on with the `RCK_Faction_Disguises` mutator, or with any `[RCK]Disguise::` map in a campaign's `mutatorList` or a level's `levelMutators`.

A player wearing a faction's headpiece passes for a member of that faction.

| Headpiece | Passes for |
| --- | --- |
| `CopHat`, `Cop2Hat` | `Cop` |
| `HatBlue` | `Crepe` |
| `HatRed` | `Blahd` |
| `SoldierHelmet` | `Soldier` |
| `ThiefHat` | `Thief` |
| `HackerGlasses` | `Hacker` |
| `FireHelmet` | `Firefighter` |
| `Fedora` | `Mafia` |
| `DoctorHeadLamp` | `Scientist` |

What a disguise does:

- The wearer counts as `<key>_Member` of that faction. It is welcome in the faction's private rooms, exempt from its turf, and can recruit with the faction's recruiting policy.
- Members of the faction who were Annoyed or Hateful toward the wearer turn Neutral. They show a "Disguised as ..." status text. Their old feelings come back when the headpiece comes off.
- A headpiece the character started with is its look, not a disguise. So is one for a faction the player already belongs to.
- Security cameras, turrets and other factions aren't fooled. Neither is a member of a `<key>_Vengeful` faction that already holds a grudge against the wearer.

When a disguise is blown:

- The disguise for that faction is blown for the rest of the level, for every player, when:
  - a member of the faction turns on the wearer in play (caught stealing, trespassing, breaking in and so on);
  - the wearer hits a member;
  - a member sees the wearer hit anyone.
- The calmed members go back to how they felt, and a "... disguise blown!" status text shows.

Getting headpieces:

- Vanilla never drops the hats NPCs wear as their look. Here, a faction member wearing its own faction's mapped headpiece drops a copy when a player or a player's follower kills, knocks out or arrests it.
- There are at most 3 drops per faction per level. Headpieces from shops, chests and loadouts work too.

Map format:

- `[RCK]Disguise::` takes `;`-separated `Item=faction` entries. The faction is a number or a key.
- `Item=None` takes a default off. Later entries win.
- Example: `[RCK]Disguise::Fedora=None;HatBlue=3;Beret=Faction_1;`. The defaults apply under either spelling.

Only the host acts in multiplayer. The BepInEx log has one line per disguise put on and per disguise blown.

## Faction names

`[RCK]FactionName::1=The Contractor;2=The Summit;` names factions in on-screen messages (disguises, and later features). The mutator list shows it as "[RCK+] Faction Names".

- Entries are `;`-separated `faction=Name`. The faction is a number or a key.
- Without a name, a key reads as itself without underscores (`Faction 3`, `Upper Cruster`).

## Faction war

These make a faction war move on its own: turf changes hands, crews raid their rivals, attacked factions call for
backup, and factions that hold turf respawn their losses and push out to take more turf and commoners' rackets. A
broker sells truces and frames. Control points measure each faction's power, the war panel ranks them, and the
commander lets the player lead one faction by hand.
Only the host acts in multiplayer, and only players on the host see the on-screen messages. The BepInEx log has a line
for each turf that falls or is claimed, each raid, each backup call and each respawn.

### Squads

Raids, backup, turf guards that walk in and respawns are squads.

- A squad is made of the faction's [squad units](#squad-units) when it has any. Otherwise it's copies of one of the
  faction's own NPC members. Leaders, backup callers, brokers, quest givers and targets, gate agents, `Relationless`
  and important NPCs, and NPCs in a party are never copied.
- Raid and backup squads appear at the post of their faction's own turf nearest where they're headed, out of the
  players' sight (13 units) and with no foe within 5 units. A faction with no such turf, and other squads, spawn next
  to a member that is at least 8 units from every player when there is one. [Respawns](#respawn) pick their own post.
- Every member is armed as the level loader arms a placed NPC: with the weapons and items its source was placed with,
  else its type's random weapon, items and money (a vanilla NPC left on "Randomized" rolls its own). A custom
  character that still has no weapon gets its character's starting items.
- Squad members are never street-innocent. Each is Hateful (hate 5) toward its targets. Targets that aren't
  street-innocent hate it back; innocent ones are only attacked, so they don't start fights of their own. Squad-mates
  keep their faction's own ties to each other.
- A raid or backup squad walks to its spot, fights what it finds, searches around three times, walks back to where it
  spawned and vanishes, as vanilla's alarm enforcers do.
- At most 12 raid, backup and guard members are alive at once. Respawned members have their own cap of 24.

### Squad units

A faction's squads can be made of chosen characters, custom ones included, instead of copies of its members:

- Give a placed NPC `<key>_Reinforcement` to make its character one of `<key>`'s units. Each holder counts once, so
  place two to double a unit's chance. The holder itself stays a normal NPC.
- Or name units in the faction's `[RCK]FactionRespawn::` entry (see [Respawn](#respawn)): `Crepe=inf:Gangbanger+Crepe
  Heavy`. A name is a custom character's name, a placed NPC's type or a vanilla agent type (`Gangbanger`, `Soldier`,
  any case). A custom character must be placed somewhere in the level, since its data lives there.
- Each member of a squad is a unit picked at random: raids, backup, guard copies and respawns all use them. A unit
  that isn't a member of the faction (a vanilla type for a numbered faction) gets `<key>_Aligned`.
- Units are snapshotted when the level loads, so they keep coming after the original dies. Leaders, gate agents, quest
  NPCs, brokers and `Relationless` NPCs are never units; the log says why one was skipped.

### Turf capture

A turf is the owned floor one crew holds: the NPCs with the same owner ID and start chunk that defend it (a
`<key>_Territorial` trait or a `=Territorial` matrix entry) and belong to a faction. RCK lists the turfs once the level
has loaded.

- A turf falls when every holder is dead, knocked out, arrested, zombified, gone, hired by a player, or routed (see
  `<key>_Leader`).
- With the `RCK_Turf_Capture` mutator, the turf then changes hands to the faction of whoever took down the last holder.
  A player or a follower takes it for the player's faction (`<key>_Member`, `<key>_Aligned` or the class's own); an NPC
  takes it for its own. A fall with no taker (an accident, or infighting) just clears it.
- The new owner moves in. Up to 3 of its NPC members that own nothing and stand near the turf's posts become its guards,
  starting with the one that took it. With none nearby, 2 of its squad units walk in. Guards take the old
  holders' posts and owner ID, so the ones with Territorial traits defend it as the old holders did. A taken turf can
  change hands again.
- A short message shows: `Blahd turf taken by the Crepes`, or `Blahd turf cleared` when nobody takes it.
- Without the mutator nothing changes hands, but the `TurfTaken=` level gate condition still counts falls:
  `[RCK]LevelGate::TurfTaken=Blahd+Crepe;` opens once both factions held turf when the level loaded and hold none now.

### Raids

A raid squad of one faction walks to one of its target's turfs (a holder's post), or else to a random NPC member of the
target, and raids it. A message shows: `The Crepes are raiding the Blahds!`.

- `[RCK]FactionRaid::` schedules raids. Put it in a campaign's `mutatorList` or a level's `levelMutators`. Entries are
  `;`-separated `A>B@T` or `A>B@TxN`: T seconds (0 to 3600) after the level loads, a squad of N (default 3, 1 to 6)
  members of A raids B. A and B are a faction number or key. The mutator list shows it as
  `[RCK+] Faction Raids (scheduled)`.
- Example: `[RCK]FactionRaid::1>Blahd@60x5;Blahd>1@240;`.
- The `RCK_Faction_Raids` mutator also starts raids on its own: the first 90 to 150 s after the level loads, then every
  150 to 240 s, at most 3 per level. Each is between a random pair of factions present where the first is Hateful or
  Territorial toward the second (by its members' traits or the matrix).
- A raid is skipped when either side is routed, the two have a truce, the raider has no one to send or the target no one
  to hit, or 12 squad members are already out. Scheduled entries fire once per level load. Bad entries are skipped with
  a log warning.

### Backup

A `<key>_Calls_Backup` holder calls for help:

- when someone outside `<key>` really attacks it (a hit the game reports as an assault), or
- when it, or an NPC of its owner group, presses an Alarm Button against a criminal and turns the alarm on.

`<key>` then sends a squad of 3 to the holder or the alarm. The squad is Hateful toward the attacker and tracks it.

- A faction calls at most 2 squads per level, at least 30 s apart, and none once it's routed. Squad members never call
  backup themselves.
- When the attacker is a player or a follower, `The Blahds called for backup!` shows.
- The role doesn't make the holder a member. Vanilla's own alarm enforcers still come as usual.

### Respawn

A faction that holds turf keeps its numbers up, and a faction is only finished once all its turf is gone.

- The `RCK_Faction_Respawn` mutator turns it on for every faction that holds turf when the level loads.
  `[RCK]FactionRespawn::` turns it on for the factions it lists and tunes it; put it in a campaign's `mutatorList` or
  a level's `levelMutators`.
- A faction's target is `Free + PerTurf ×` the turfs it holds now, at most `Max` (by default 2 + 2 per turf, at most
  12). Every 2 s the host counts its living NPC members (not zombies, ghosts or players' hires; squads count). When
  that's under the target and its cooldown (default 60 s) has passed, it respawns a squad of up to `Size` (default 3),
  never more than it's missing. The first respawn comes one cooldown after the level loads.
- The squad appears at a post of one of the faction's turfs where no player is within 13 units, no holder is fighting
  and no foe is within 5 units. Turfs with empty posts come first. When no turf is safe it tries again 10 s later.
- It first restaffs the faction's empty posts, the home turf's first. With `RCK_Turf_Capture` the rest march together
  on the nearest turf that's empty or held by a foe (a faction it's Hostile or Territorial toward, by traits or the
  matrix, not routed and without a truce). They fight their way in: taking down the last holder captures it as usual,
  and an empty or undefended turf is theirs once one of them is within 2 units of a post
  (`Blahd turf taken by the Crepes`, or `The Blahds took back their turf`). Either way they hold it. Marchers pick a
  new target when theirs stops being a foe's or after 300 s, and hold their faction's nearest turf when there's none.
  Without `RCK_Turf_Capture` the rest become extra holders of the home turf.
- A faction's pool is unlimited (`inf`, the default) or a number of members per level.
- A faction that holds no turf and has no one marching is finished for the level: no more respawns, and
  `The Blahds have lost all their turf!` shows. A routed faction's turfs fall, so it's finished too. A faction without
  turf at load never respawns.
- At most 24 respawned members are alive at once, all factions together.

`[RCK]FactionRespawn::` takes `;`-separated entries. Later entries win, and bad entries are skipped with a log warning.

| Entry | Meaning |
| --- | --- |
| `Crepe=inf` | Respawn Crepes with no limit. A faction is a number (`1`) or a key (`Faction_1`, `Crepe`). |
| `Blahd=20` | Respawn at most 20 Blahds this level (0 to 999). |
| `Cop=off` | Never respawn Cops, even with the mutator on. Units named after `:` still apply to its other squads. |
| `Crepe=inf:Gangbanger+Crepe Heavy` | Also sets the faction's [squad units](#squad-units) (at most 12 names). |
| `Cooldown=45` | A default for the level: `Free` (0 to 30), `PerTurf` (0 to 10), `Max` (1 to 30), `Cooldown` (10 to 600 s) or `Size` (1 to 6). |
| `Crepe.Size=4` | One faction's own setting, over the level's defaults. |

Example: `[RCK]FactionRespawn::Cooldown=45;Crepe=inf:Gangbanger+Crepe Heavy;Crepe.Size=4;Blahd=20;`.

### Broker

An NPC with `RCK_Broker` sells faction deals. It needs no faction.

- It offers them when it isn't Hostile or Annoyed toward the player, and at least two factions have NPC members on the
  level (living, not routed, not in a party).
- `Broker a truce` ($100 + $25 per level): the two chosen factions turn Neutral toward each other, both ways, for the
  rest of the level, with hate and turf standoffs cleared. It isn't offered while the pair already has a truce.
- `Frame a faction` ($75 + $25 per level): the second faction turns Hateful toward the first for the rest of the level.
- `Change first faction` and `Change second faction` cycle the choices. The broker remembers them for the level.
- Both deals are level relationship rules: they beat faction traits and the matrix, but not Vengeful grudges or routs.
  A frame holds back from street innocents until they're caught.

### Turf map

Whenever the level has turfs, the big map (the quest sheet's map) shows who holds them. It needs no mutator.

- Each turf's floor (the tiles of its owner ID and start chunk) is tinted in its holder's colour, with a stronger
  outline. The players' own factions get a white outline.
- A cleared turf is grey. A turf shared by several factions is checkered in their colours. A turf with no owned floor
  shows as a small disc around its posts.
- A contested turf is striped. It's contested while it's held and one of its holders is fighting.
- A legend in the map's top-left corner lists every faction that held turf at load or holds some now. Each row has the
  faction's swatch, its name (`[RCK]FactionName::` names apply), `(you)` for the players' factions, and the number of
  turfs it holds now. A routed faction, or one holding none, is greyed out.
- It updates live while the map is open.
- F8 hides and shows it. When the map is closed, a "Turf map on" or "Turf map off" status text shows. Set the key with
  `TurfOverlayKey` in the `[Map]` section of `BepInEx\config\streetsofrogue.roguecontentkit.cfg`, and the starting
  state with `TurfOverlay`.
- Host only, as clients don't know the turfs. It isn't shown on streaming, home base or tutorial levels.

| Faction | Colour |
| --- | --- |
| `Crepe` | blue |
| `Blahd` | red |
| `Mafia` | charcoal, gold outline |
| `Cop` | navy, light blue outline |
| `Thief` | purple |
| `Hacker` | green |
| Others | a fixed palette of 12 colours, by key |

The BepInEx log has one line with the map's layout the first time the map is opened, and one line each time the turf
shapes are rebuilt. Free rackets show as sand and run ones in the racketeer's colour (see [Rackets](#rackets)).

### Expansion

With the `RCK_Turf_Expansion` mutator, or any `[RCK]TurfWar::` entry, factions push out on their own.

- Every `Expand` seconds (default 45) each faction that holds turf, isn't routed and isn't the one the player
  commands picks the nearest empty turf or free racket within `Reach` tiles (default 30) of one of its posts that none
  of its members is already marching on.
- It sends a party of up to `Party` members (default 3), nearest first: free roamers (NPC members that own nothing,
  aren't in a raid, backup or command squad and have no order) and spare holders (a second holder on the same post).
- The party marches like a respawn squad. The spot is theirs once one of them is within 2 units of a post, and they
  hold it.
- Captures only stick with `RCK_Turf_Capture` or the commander.

`[RCK]TurfWar::` takes `;`-separated `Name=N` entries. Names are case-insensitive, later entries win, and bad entries
are skipped with a log warning.

| Entry | Meaning |
| --- | --- |
| `Expand=45` | Seconds between a faction's moves: 0 for none, else 15 to 600 (under 15 counts as 15). |
| `Racket=1` | 1 lets factions run rackets, 0 stops it. |
| `Reach=30` | How far from its own posts a faction looks, in tiles (5 to 200). |
| `Party=3` | Members per move (1 to 6). |
| `Reopen=3` | New owners a ruined racket gets per level (0 to 9). See [Rackets](#rackets). |

Example: `[RCK]TurfWar::Expand=60;Party=2;`.

### Rackets

A racket is a place commoners own: NPCs with the same owner ID and start chunk that are `Common_Folk` by their vanilla
type and in no other faction, where no faction member owns anything. RCK lists rackets with the turfs; the commoners'
spots are its posts, but the commoners never hold it.

- A free racket is taken like empty turf: a faction walks in and guards it, through expansion or the commander.
- While it's run, its commoners and the racketeers' NPC members are Aligned both ways, and
  `The Crepes now run a protection racket` shows.
- It's broken (`Crepe racket broken`) when all its guards are down, and free again for anyone. It's ruined
  (`Crepe racket ruined`) when its commoners are all gone, and its guards go back to their faction's turf.
- A ruined racket gets a new, neutral owner 30 s later, once no player is within 8 tiles of it (or after 90 s
  regardless), up to `Reopen` times a level (default 3). The owner is the old owner's type if a Mobster can shake it
  down (Shopkeeper, Bartender, Clerk, Drug Dealer or Athlete), else a Shopkeeper, armed as usual. The racket is free
  again.
- A player with `RCK_Faction_Racketeer` can take a free racket alone: see [Racketeer](#racketeer).
- Rackets never count as turf: not for respawn targets, raids, `TurfTaken`, finishing a faction or the turf map's
  turf count. They pay control points and add to a faction's power.
- They're on whenever expansion or the commander is, unless `Racket=0`.

### Control points

Control points (CP) measure a faction's hold on the city and pay for the commander's recruits. They're on with any
`[RCK]ControlPoints::` entry or the commander, host only, and last for one level load.

- Every `Interval` seconds each faction in the war (it held turf at load, holds or held some since, or is commanded)
  that isn't routed earns `Base + PerTurf ×` its turfs `+ PerCommon ×` its rackets, up to `Max`. A faction with no
  turf and no racket earns nothing, except the commanded one, which keeps its `Base`.
- Taking a turf pays `Capture` (half for a racket).
- Taking down an NPC member of another faction pays `Kill` to the killer's side: a follower counts for its player, a
  player for the faction they command (else their own), and a squad member for its squad's faction.
- Every faction starts with `Start`.
- A faction's power is its share, in percent, of the sum over all factions of
  `10 × turfs + 4 × rackets + living NPC members + CP / 5` (CP counts 0 while points are off).

`[RCK]ControlPoints::` takes `;`-separated `Name=N` entries, with the same rules as `[RCK]TurfWar::`.

| Entry | Default | Range |
| --- | --- | --- |
| `Base` | 1 | 0 to 50 |
| `PerTurf` | 2 | 0 to 50 |
| `PerCommon` | 1 | 0 to 50 |
| `Capture` | 10 | 0 to 500 |
| `Kill` | 1 | 0 to 100 |
| `Max` | 200 | 10 to 9999 |
| `Interval` | 10 s | 5 to 120 |
| `Start` | 30 | 0 to 9999, at most `Max` |

Example: `[RCK]ControlPoints::Start=50;PerTurf=3;`.

### Commander

With the `RCK_Commander` mutator, or any `[RCK]Command::` entry, the host leads one faction for the level.

- The faction is the `Faction=` key, else the host's first faction that held turf at load, else the host's own faction
  (never `Common_Folk`). With none, the log says so and nothing changes.
- That faction's automatic respawn, raids (scheduled ones too), backup calls and expansion stop. Every other faction
  carries on as before.
- Turf capture, control points and rackets come on with it. `You command the Crepes (T)` shows and the console opens.
- **Recruit** raises a squad of a unit: up to `Size` members, fewer when the cap or the faction's CP runs short, for
  the unit's cost each. It appears at the post of the faction's own turf nearest the player that has no foe within
  5 units, else beside the player.
  Recruits are sworn to the commanded faction alone, whatever their type (a recruited Cop drops its ties to other
  cops), and first hold the nearest post of their faction's turf.
- Orders go to one squad or all of them:
  - **Capture** a foe's turf, an empty turf or a free racket, and hold it once taken. On the faction's own turf it's
    Defend.
  - **Defend** one of the faction's own turfs, empty posts first.
  - **Recall**: walk to the player and wait.
  - **Move**: walk to a spot clicked on the map and wait there, holding nothing.
  - **Disband** (two clicks within 3 s): the members vanish and `Refund` percent of what they cost comes back.
- A turf held by a faction that isn't a foe needs a war first. **Declare war** (two clicks within 4 s) makes the two
  factions Hateful toward each other for the rest of the level, and `War on the Blahds!` shows.
- A manual order that hasn't finished in 600 s lapses, and the squad holds its faction's nearest turf. A squad with
  nobody left is dropped (`Squad 2 wiped out`).
- A squad sent to defend another turf stops holding the one it was on, so that turf can fall or be claimed. A recruit
  that a player hires, or that is raised as a zombie or ghost, leaves its squad and stops counting toward the cap.
- A turf the commanded faction takes in a fight is held by whichever of its men are free nearby. It gets no free guard
  copies, as other factions' turfs do: one with nobody near is held but undefended until a squad is sent to it. A
  squad marching on another turf keeps its order when a turf falls as it passes.
- The console is a window with a map of the level and the controls beside it (see [Command map](#command-map)). T
  hides and shows it (`CommandConsoleKey` in `[Map]`). Mouse only, host only. See [Keys](#war-keys) for the keys.

#### Command map

The console's map shows the whole level: walls and floors, and every turf and free racket in its holders' colours, as
on the [turf map](#turf-map) (dotted for rackets, striped while under attack, a white edge for your faction's). Each
turf has a numbered badge (`$` and a number for a racket), you're a white dot marked `You`, other factions' members are dots in their colour
(commoners in sand), and your squads are numbered discs in your faction's colour with a dotted line to where each is
headed: red to attack, green to hold, yellow to move. The picked squad has a pulsing yellow ring. Hovering shows
what's under the mouse: a squad's units and what it's doing, a turf's place and holders, and what a click would do.

- **Pick a squad:** click its disc on the map or its chip in the squad list. `All squads` picks every squad. Click the
  squad again, or right-click the map, to clear the pick. With just one squad there's nothing to pick: orders go to
  it. A newly recruited squad is picked by itself when nothing else is, so the next map click sends it.
- **Order it:** with a squad picked, click a foe's turf, an empty turf or a free racket to capture it, one of your own
  turfs to defend it, or open ground to move there. Walls and holes refuse (`They can't go there.`).
- **Before the click:** hovering a turf rings its edge, pulsing, in the colour of what a click would do (red to
  attack or take, green to defend, grey when it would be refused) and says why (`Click: attack Blahds`,
  `At peace with Mafia: declare war first`, `Ruined: its owners are gone.`). A faint dotted line runs from the
  squads that would go. Open ground says `Click: move here` with a yellow line. A squad says `Click: pick it` or
  `Click: let go`.
- **After the click:** a ring flashes where the order went, in the same colour, and the outcome shows beside the map.
- While nothing is picked, a hint along the map's bottom says what to do next (recruit, click the map, or pick a
  squad).
- Beside the map: your faction, men alive and the cap; the last order's outcome (red when refused); the recruit list
  with each unit's squad size and cost (`at the cap` or `need 25 CP` when it can't be raised); the squad list with
  `Recall` and `Disband` for the pick (the first `Disband` click says how much CP comes back); and `Declare war`.
  The title shows the faction's CP and its income (`+5 every 10 s`, or `(full)` at the most it can hold).
- It uses the game's own font, scales with the screen height and can be dragged by its title. The map redraws its
  floors every 20 s and the turf colours twice a second. Closed with its key, it stays closed in later levels until
  the key opens it again (until the game quits).

`[RCK]Command::` takes `;`-separated entries. Names are case-insensitive, later entries win, and bad entries are
skipped with a log warning.

| Entry | Meaning |
| --- | --- |
| `Faction=Crepe` | The faction to command, a number or key. |
| `Units=Gangbanger+Crepe Heavy:25` | Up to 12 recruitable units, as for [squad units](#squad-units). `Name:N` gives one its own cost (0 to 500); a colon not followed by digits stays in the name. At most 64 characters a name. Without `Units` the console offers the faction's squad units, else a copy of one of its members. |
| `Cost=10` | CP per recruit (0 to 500). |
| `Size=3` | Members per squad (1 to 6). |
| `Cap=12` | Recruits alive at once (1 to 24). |
| `Refund=50` | Percent of the cost a disband gives back (0 to 100). |

Example: `[RCK]Command::Faction=Crepe;Units=Gangbanger+Crepe Heavy:25;Cost=10;`.

### War panel

The war panel ranks every faction in the level's turf war, strongest first.

- Each row has the faction's swatch and name (`[RCK]FactionName::` names apply; `(yours)` for the commanded faction,
  `(you)` for the players' own, `routed` once routed), its turfs, rackets, living members, CP (while points are on)
  and a bar of its power (turf, rackets, men and CP; the footer says so). A faction with nothing left is greyed out.
  The last line counts free rackets.
- It refreshes every second, can be dragged by its title, scales with the screen height and hides behind the pause
  menu. Clicks on it don't fire the player's weapon.
- G shows it in any level with a turf war (`WarPanelKey` in `[Map]`). It opens by itself with the `RCK_War_Panel`
  mutator or the commander, unless G closed it in an earlier level. Mouse only, host only.

#### War keys

The war screens' keys are in the `[Map]` section of `BepInEx\config\streetsofrogue.roguecontentkit.cfg`, as Unity
KeyCode names. `None` turns a key off.

| Setting | Default | Does |
| --- | --- | --- |
| `CommandConsoleKey` | T | Hides and shows the commander console. |
| `WarPanelKey` | G | Hides and shows the war panel. |
| `TurfOverlayKey` | F8 | Hides and shows the [turf map](#turf-map). |

- T and G sit next to WASD and do nothing in SoR's default controls. G is only the game's debug-build pause key.
- RCK reads the player's own SoR keyboard controls every 2 s. If one of these keys is also bound to a game action,
  both happen on a press: the console or panel shows a red line naming the action and the setting to change, and the
  log warns once. Playing with a gamepad turns the keyboard controls off, so nothing clashes.
- A config written by a test build that used F9 for the console and F6 for the panel moves to T and G once
  (`KeyDefaults` records it). Keys the player chose themselves stay.

### Faction medic

An NPC with `RCK_Faction_Medic` patches up its friends. It needs no faction.

- Every 5 s it heals the hurt friend within 3.2 units (5 tiles) with the lowest share of their health, itself
  included, by `max(8, 15%` of the patient's most health`)`, never more than they're missing, with the vanilla heal
  sound.
- Friends are its employer and fellow followers, its own followers, the players while the medic belongs to the faction
  they command, and anyone sharing one of its factions. Never someone it's Hostile or Annoyed toward.
- A dead, arrested or zombified medic doesn't heal, nor does a player. It works on recruits, squads and respawn units
  like any other trait. Host only.

### Racketeer

A player with `RCK_Faction_Racketeer` can take a free [racket](#rackets) for their faction without a squad.

- Talking to one of a free racket's commoners shows `Shake down for <faction> (n%)`. The faction is the one the player
  commands, else their own (`<key>_Member` or `<key>_Aligned` first, then the class's own; never `Common_Folk` or a
  routed faction).
- The chance is vanilla's shakedown chance, and 100% when the commoner is at 40% health or less, Aligned with the
  player, or the player's slave.
- On success the racket joins the faction as if its squad had taken it: the commoners side with the faction, and the
  maps and control points follow. No money changes hands and nobody is posted there. On failure the commoner attacks
  and nearby police hear it, as in vanilla.
- A Mobster's own vanilla shakedown of such a commoner claims the racket too, and the racketeer button doesn't show
  for a Mobster.
- Only free rackets, so only while rackets are on. Players only: an NPC with the trait does nothing. Host only.

## Private areas of friendly factions

Faction members are welcome in the private areas of factions friendly to theirs. This is always on and has no trait or mutator.

- A player who joined a faction through a faction trait (`<key>_Member` or `<key>_Aligned`) is welcome in the owned rooms of an NPC that shares one of those factions, or whose factions are Friendly or Aligned toward them (matrix first, then faction traits). The NPC can be a member either way, so a `Blahd_Member` player is welcome in a vanilla Blahd base and a `Cop_Member` player in a police station. Vanilla membership alone doesn't count for the player, so a vanilla Scientist player isn't welcome in every lab and ordinary runs are unchanged.
- The NPC must still be Neutral or Friendly toward the player. The player's followers come in with the player.
- A welcome player can walk in, open doors and talk without being told to get out.
- Stealing, breaking things, hacking, starting fires, planting bombs, opening prison cells and hurting anyone are still crimes. Once the owner is Annoyed or worse, the player is a trespasser again.
- Security cameras, turrets and laser emitters keep the vanilla ownership rules, as they do for Friendly visitors in vanilla. Gang muggings, Cop Bot checks, the Mayor's guards and police lockdowns are unchanged.
