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

Street-side means the default goal is `WanderFar` or a `Random Teleport` goal, or the NPC has no owner and starts on unowned floor. Drug dealers stay guilty. Players are never made innocent.

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
