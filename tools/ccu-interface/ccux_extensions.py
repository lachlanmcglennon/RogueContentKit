"""RCK's own additions to the CCU trait interface.

The faction additions extend CCU's own faction patterns, `Faction_<N>_<Grade>` and `<Group>_<Grade>` (and the role
traits `<key>_Vengeful` and `<key>_Leader`), so they keep CCU-style IDs instead of the `RCK_` prefix. The other additions
(recruiting, street innocence, the defector) use the `RCK_` prefix. `merge()` adds
them to docs/ccu-interface.json with `"extension": true`, skips any faction ID CCU already defines, refuses any other
clash, and records the faction tables under `spec["extensions"]`.

harvest_ccu.py calls merge() on every harvest. To refresh an existing spec without re-harvesting:

    python tools/ccu-interface/ccux_extensions.py [--spec docs/ccu-interface.json]

Then run tools/ccu-codegen/gen_traits_cs.py and tools/ccu-interface/gen_interface_md.py.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SPEC = ROOT / "docs" / "ccu-interface.json"

NUMBERED_FACTIONS = 20

# Named factions: trait key -> who counts as a member besides agents holding <key>_Aligned.
NAMED_FACTIONS = {
    "Blahd": "vanilla GangbangerB (the red gang)",
    "Crepe": "vanilla Gangbanger (the blue gang)",
    "Cannibal": "vanilla Cannibal",
    "Gorilla": "vanilla Gorilla",
    "Soldier": "vanilla Soldier",
    "Firefighter": "vanilla Firefighter",
    "Scientist": "vanilla Scientist",
    "Mafia": "vanilla Mafia, and agents with the MafiaAligned trait",
    "Upper_Cruster": "vanilla UpperCruster, and agents with the UpperCrusty trait (the game gives it to the usual Uptown residents)",
    "Vampire": "vanilla Vampire",
    "Werewolf": "vanilla WerewolfB, in human or wolf form",
    "Common_Folk": "ordinary humans (no gang, not inhuman, not law) and agents with the Common_Folk trait",
    "Slavemaster": "vanilla Slavemaster",
    "Cop": "vanilla Cop, Cop2 (Supercop), CopBot, and agents with TheLaw",
    "Zombie": "vanilla Zombie and zombified agents",
    "Thief": "vanilla Thief",
    "Hacker": "vanilla Hacker",
}

# Trait suffix -> relationship it sets. Aligned also makes the holder a member of the faction. Territorial sets
# Annoyed and escalates on turf (TERRITORIAL).
GRADES = {
    "Aligned": "Aligned",
    "Hostile": "Hateful",
    "Annoyed": "Annoyed",
    "Friendly": "Friendly",
    "Neutral": "Neutral",
    "Territorial": "Annoyed",
}

TERRITORIAL = ("Territorial starts the pair Annoyed. Each side turns Hateful (hate 5, for the rest of the level) toward "
               "the other when it sees the other on its own turf: floor whose owner is its ownerID (any owned floor for "
               "owner 99) in its starting chunk, not a wall or behind glass, the same test vanilla uses for trespassing. "
               "Anywhere else the pair stays Annoyed, so rival gangs defend their buildings without brawling across the "
               "street. A side that owns no floor (owner 0, a street NPC) never escalates. It doesn't escalate toward "
               "agents that live there (same owner and chunk), dead agents, or holders of a PropertyDeed. Unlike vanilla "
               "trespassing, enforcers and firefighters aren't exempt.")

# Player-kind suffix: membership only, for playable characters that start inside a faction.
MEMBER = "Member"

# Faction roles: designer suffixes that give the holder a part in its faction's events. They aren't grades: they set no
# relationship at spawn, take no part in the precedence list, don't cancel the grades and don't make the holder a
# member.
ROLES = {
    "Vengeful": "Turns Hateful toward anyone who attacks or kills a member (see the rules below).",
    "Leader": "When the last one falls, the faction is routed (see the rules below).",
    "Calls_Backup": "When attacked, or when its owner group raises an alarm, the faction sends a squad (see "
                    "`extensions.backup`).",
    "Reinforcement": "The holder's character is a unit its faction's squads are made of: raids, backup, guards and "
                     "respawns spawn it (see `extensions.respawn`).",
}
VENGEFUL = ("A `<key>_Vengeful` holder avenges the faction. When someone attacks or kills a member of `<key>` (through "
            "`<key>_Aligned`, `<key>_Member` or vanilla membership, so players can be members too), every living holder "
            "turns Hateful (hate 5) toward the attacker for the rest of the level, wherever it is and whether or not it "
            "saw the attack. Holders that spawn later in the level bear the same grudge. Only the attacker is targeted, "
            "not its party. An attack is a hit the game reports as an assault (melee, bullets, thrown items, explosions), "
            "or a kill, knockout or arrest the game credits to the attacker. As in vanilla, duels, zombies and fights "
            "between two players don't count. There's no revenge on a member of `<key>`, on the holder's own "
            "party-mates, or on an agent the holder is Aligned, Loyal or Submissive to, and `Relationless` holders "
            "take none. Once the faction is routed (see `<key>_Leader`) it takes no more revenge. The role doesn't make "
            "the holder a member; add `<key>_Aligned` for that.")
LEADER = ("A `<key>_Leader` holder keeps the faction together. When the last living holder of `<key>_Leader` in the "
          "level dies, is knocked out or is arrested, `<key>` is routed for the rest of the level. Every living NPC "
          "member outside a player's party turns Neutral toward the players and their followers, with hate, strikes "
          "and turf standoffs cleared (Aligned, Loyal, Friendly and Submissive links stay), and from then on flees "
          "fights instead of joining them, unless it's `Fearless`. Members that spawn later are routed too. A routed "
          "member that is attacked again turns hostile as usual, but runs, and one that joins a player's party stops "
          "running. Other factions' feelings don't change. A leader a scene setter puts down at the start of the level "
          "(`Dead`, `Knocked Out`, `Arrested` and the like) never led, so its fall doesn't count. The "
          "role doesn't make the holder a member; add `<key>_Aligned` for that.")
DEFECTOR_TRAIT = "RCK_Faction_Defector"
DEFECTOR = (f"`{DEFECTOR_TRAIT}` marks a turncoat. When the holder joins a player's party (hired, recruited, rescued, "
            "or following the player into the next level), the factions it belonged to disown it, except any the "
            "player belongs to as well: the holder and every living NPC member of those factions outside the "
            "player's party turn Hateful (hate 5) toward each other, even over an Aligned link, on this level and on "
            "later ones while it stays in the party. Its factions are its `<key>_Aligned` traits and its vanilla "
            "membership; `Common_Folk` counts only through `Common_Folk_Aligned`. Street innocence doesn't hold this "
            "back. Joining an NPC's party (a Random Teleport squad) doesn't count.")

# Level/campaign mutator holding a relationship matrix between factions.
MATRIX_PREFIX = "[RCK]FactionRel::"
# Earlier spellings RCK still reads, so older campaigns keep working. Write MATRIX_PREFIX in new content.
MATRIX_LEGACY_PREFIXES = ["[CCU]FactionRel::"]
MATRIX_FORMAT = ("`[RCK]FactionRel::` followed by `;`-separated entries `A>B=Rel` (members of A toward members of B), "
                 "`A<>B=Rel` (both ways) or `A<B=Rel`. A and B are a number (`3` = `Faction_3`), a faction key, `Player` "
                 "or `*` (anyone outside the other side). Rel is a relationship from the precedence list; `Hostile` "
                 "means `Hateful`, and `Territorial` is the faction grade of that name, set one way.")

# Strongest first. When several faction rules match one pair, the strongest wins.
PRECEDENCE = ["Hateful", "Territorial", "Annoyed", "Friendly", "Aligned", "Neutral"]

# Faction recruiting. NPC trait ID -> (display suffix, policy). The traits cancel each other.
RECRUIT_TRAITS = {
    "RCK_Recruit_Free": ("Free", "Free"),
    "RCK_Recruit_Paid": ("Paid", "Paid"),
    "RCK_Not_Recruitable": ("Not Recruitable", "Off"),
}
# Campaign/level mutators that switch recruiting on for every faction. Both on means Free.
RECRUIT_MUTATORS = {
    "RCK_Faction_Recruit_Free": {
        "display_en": "[RCK+] Faction Recruiting - Free",
        "description_en": "Players can recruit NPCs from their own factions for free (\"Join me\").",
        "policy": "Free",
    },
    "RCK_Faction_Recruit_Paid": {
        "display_en": "[RCK+] Faction Recruiting - Paid",
        "description_en": "Players can hire NPCs from their own factions as protection, at the gang-hire price.",
        "policy": "Paid",
    },
}
RECRUIT_PREFIX = "[RCK]FactionRecruit::"
RECRUIT_POLICIES = ["Free", "Paid", "Off"]
RECRUIT_FORMAT = ("`[RCK]FactionRecruit::` followed by `;`-separated entries `A=Policy`. A is a faction number "
                  "(`3` = `Faction_3`), a faction key, a comma list of them, or `*` (any faction no entry names). Policy is "
                  "`Free`, `Paid` or `Off`. Where several entries match an NPC's factions, the most generous wins.")
RECRUIT_RULES = ("A player can recruit an NPC when both are members of a faction (the NPC through `<key>_Aligned` or "
                 "vanilla membership, the player through `<key>_Member`, `<key>_Aligned` or vanilla membership) and "
                 "that faction's policy allows it. The policy comes from the NPC's recruit trait, else the "
                 "`[RCK]FactionRecruit::` matrix, else the Faction Recruiting mutators (Free wins if both are on), "
                 "else Off. Free adds vanilla \"Join me\" at no cost; Paid adds \"Hire as protection\" at the vanilla "
                 "gang-hire price (and the Hiring Voucher offer). A player in a faction that the NPC's factions are "
                 "Friendly with (matrix, else faction traits) can hire it at the Paid price wherever its factions allow "
                 "recruiting. The NPC must be alive, free, not employed, not Relationless, and not Annoyed or Hostile "
                 "toward the player. No button is added where the game already offers \"Join me\", or a paid "
                 "\"Hire as protection\" for a Paid recruit. Joining uses the vanilla rules, so follower caps, "
                 "Unlikeable and low health still refuse, and Homesickless, Homesickly and Permanent_Hire apply.")

# Street innocence: an NPC trait and a mutator that hold back hostile faction rules and Guilty until the NPC is caught.
INNOCENCE_TRAIT = "RCK_Innocent_Until_Caught"
INNOCENCE_MUTATOR = "RCK_Street_Innocence"
INNOCENCE_MUTATOR_DISPLAY = "[RCK+] Street Innocence"
INNOCENCE_MUTATOR_DESCRIPTION = ("NPCs that start on the street are innocent until caught: their hostile faction rules "
                                 "and Guilty trait wait until another NPC sees them start a fight.")
INNOCENCE_RULES = ("An innocent NPC's Hostile, Territorial and Annoyed faction rules (traits or matrix), both ways, and "
                   "its `Guilty` trait (for `Hostile_to_Guilty`) don't apply: the pair keeps its vanilla relationship. "
                   "Friendly, Aligned and Neutral faction rules still apply. An NPC is innocent while it holds "
                   f"`{INNOCENCE_TRAIT}`, or while the `{INNOCENCE_MUTATOR}` mutator is on and it started street-side: "
                   "its default goal is WanderFar, Random Patrol (Map) or a Random Teleport, or it owns nothing and "
                   "started on unowned floor. "
                   "It is caught when another NPC sees it strike first: an attack on someone who didn't hit it first "
                   "and isn't fighting it. Property crimes don't catch it. Then its held-back rules apply for "
                   "the rest of the level, only ever making a relationship worse. `Drug_Dealer` holders and vanilla "
                   "Drug Dealers stay Guilty.")

NOTE = ("RCK additions, not part of CCU. Faction traits extend CCU's `Faction_<N>_<Grade>` and `<Group>_<Grade>` "
        "patterns; every other addition uses the `RCK_` prefix.")

# Always-on rules with no trait or mutator of their own.
PARTY_PEACE = ("Party-mates never fight each other. Two agents are party-mates when one employs the other or both have "
               "the same employer; the leader can be a player or an NPC, and Random Teleport squads count. Relationship "
               "rules skip party-mates: their own relationship traits, faction traits and the matrix, `Hostile_to_Guilty`, "
               "trait gates and street innocence. The pair keeps the Loyal, Aligned or Submissive link the game set when "
               "it joined, also when followers carry over to the next level. When an agent joins a party, any "
               "Territorial standoff, hate, and Annoyed or Hateful relationship between it and the leader or the other "
               "party-mates is cleared. `Relationless` still applies.")
PRIVATE_ACCESS = ("A player who joined a faction through `<key>_Member` or `<key>_Aligned` is welcome in the owned "
                  "rooms of an NPC that shares one of those factions (the NPC through `<key>_Aligned` or vanilla "
                  "membership), or whose factions are Friendly or Aligned toward the player's (matrix, else faction "
                  "traits), while the NPC is Neutral or Friendly toward the player. So `Blahd_Member` is welcome in a "
                  "vanilla Blahd base, but vanilla membership alone doesn't count for the player. The player's "
                  "followers come in with the player. The NPC doesn't treat a "
                  "welcome visitor as a trespasser: no \"get out\", and opening its doors isn't a crime. Theft, damage, "
                  "hacking, fire, bombs, opening prison cells and attacks still are, and once the NPC is Annoyed or "
                  "worse the visitor is a trespasser again. Security cameras, turrets and laser emitters, gang "
                  "muggings, Cop Bots, the Mayor's guards and police lockdowns keep the vanilla rules.")


def faction_keys() -> list[str]:
    return [f"Faction_{n}" for n in range(1, NUMBERED_FACTIONS + 1)] + list(NAMED_FACTIONS)


def _trait(key: str, grade: str) -> dict:
    tid = f"{key}_{grade}"
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Rel_Faction",
        "bases": ["T_Rel_Faction", "T_Relationships", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Relationships/Relationships - Faction/{tid}.cs",
        "display_en": f"[RCK+] Rel Faction - {key.replace('_', ' ')} {grade}",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [f"{key}_{g}" for g in GRADES if g != grade],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "faction": {"key": key, "grade": grade},
    }


def _member(key: str) -> dict:
    tid = f"{key}_{MEMBER}"
    return {
        "versions": ["RCK"],
        "kind": "player",
        "namespace": "RCK.Traits.Faction_Membership",
        "bases": ["T_PlayerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Player Traits/Faction Membership/{tid}.cs",
        "display_en": f"[RCK+] {key.replace('_', ' ')} {MEMBER}",
        "unlock_class": "TU_PlayerUnlock",
        "unlock": {
            "Cancellations": [],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": True,
            "UnlockCost": 0,
            "Unlock": {"cantLose": True, "cantSwap": True},
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "faction": {"key": key, "grade": MEMBER},
    }


def _role_trait(key: str, role: str) -> dict:
    tid = f"{key}_{role}"
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Rel_Faction",
        "bases": ["T_Rel_Faction", "T_Relationships", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Relationships/Relationships - Faction/{tid}.cs",
        "display_en": f"[RCK+] Rel Faction - {key.replace('_', ' ')} {role.replace('_', ' ')}",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "faction": {"key": key, "role": role},
    }


def _defector_trait() -> dict:
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Rel_Faction",
        "bases": ["T_Rel_Faction", "T_Relationships", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Relationships/Relationships - Faction/{DEFECTOR_TRAIT}.cs",
        "display_en": "[RCK+] Rel Faction - Defector",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "defector": True,
    }


def _recruit_trait(tid: str) -> dict:
    label, policy = RECRUIT_TRAITS[tid]
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Recruiting",
        "bases": ["T_Recruiting", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Recruiting/{tid}.cs",
        "display_en": f"[RCK+] Recruiting - {label}",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [t for t in RECRUIT_TRAITS if t != tid],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "recruit": {"policy": policy},
    }


def _recruit_mutator(name: str) -> dict:
    m = RECRUIT_MUTATORS[name]
    return {
        "versions": ["RCK"],
        "class": "MutatorUnlock",
        "path": "RCK/Systems/Social/Relations/FactionRecruit.cs",
        "bases": [],
        "display_en": m["display_en"],
        "description_en": m["description_en"],
        "in_head": True,
        "extension": True,
        "recruit": {"policy": m["policy"]},
    }


def _innocence_trait() -> dict:
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Rel_General",
        "bases": ["T_Rel_General", "T_Relationships", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Relationships/Relationships - General/{INNOCENCE_TRAIT}.cs",
        "display_en": "[RCK+] Rel General - Innocent Until Caught",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "street_innocence": True,
    }


def _innocence_mutator() -> dict:
    return {
        "versions": ["RCK"],
        "class": "MutatorUnlock",
        "path": "RCK/Systems/Social/Relations/StreetInnocence.cs",
        "bases": [],
        "display_en": INNOCENCE_MUTATOR_DISPLAY,
        "description_en": INNOCENCE_MUTATOR_DESCRIPTION,
        "in_head": True,
        "extension": True,
    }


DISGUISE_MUTATOR = "RCK_Faction_Disguises"
DISGUISE_PREFIX = "[RCK]Disguise::"
DISGUISE_DEFAULTS = {
    "CopHat": "Cop", "Cop2Hat": "Cop", "HatBlue": "Crepe", "HatRed": "Blahd", "SoldierHelmet": "Soldier",
    "ThiefHat": "Thief", "HackerGlasses": "Hacker", "FireHelmet": "Firefighter", "Fedora": "Mafia",
    "DoctorHeadLamp": "Scientist",
}
DISGUISE_DESCRIPTION = ("A player wearing a faction's headpiece passes for one of its members until they give "
                        "themselves away. Fallen members drop theirs.")
DISGUISE_RULES = (
    "On with the `RCK_Faction_Disguises` mutator or any `[RCK]Disguise::` map. A player wearing a mapped headpiece "
    "(worn, not the character's own starting look, and not for a faction the player already belongs to) counts as "
    "`<key>_Member` of that faction: welcome in its private rooms, exempt from its turf, recruitable-with, and its "
    "members who were Annoyed or Hateful toward the player turn Neutral (their old feelings come back when the "
    "headpiece comes off). A disguise is blown for the rest of the level, for every player, when a member of the "
    "faction turns on the wearer in play (caught stealing, trespassing, breaking in and so on), when the wearer hits a "
    "member, or when a member sees the wearer hit anyone. A member a Vengeful grudge already turned against the wearer "
    "isn't fooled. Cameras, turrets and other factions never are. A faction member wearing its own mapped headpiece as "
    "its look drops a copy when a player's side fells it, at most 3 per faction per level (vanilla never drops look "
    "headpieces)."
)
DISGUISE_FORMAT = ("`[RCK]Disguise::` followed by `;`-separated entries `Item=faction` (a faction number or key); "
                   "`Item=None` takes a default off. Later entries win. The defaults apply under either spelling.")
FACTION_NAME_PREFIX = "[RCK]FactionName::"
FACTION_NAME_FORMAT = ("`[RCK]FactionName::` followed by `;`-separated entries `faction=Name` (`1=The Contractor;"
                       "2=The Summit`). On-screen messages (disguises, quests, turf, raids, brokers) use the name; "
                       "otherwise a key reads as itself without underscores (`Faction 3`, `Upper Cruster`).")


def _disguise_mutator() -> dict:
    return {
        "versions": ["RCK"],
        "class": "MutatorUnlock",
        "path": "RCK/Systems/Social/Relations/Disguises.cs",
        "bases": [],
        "display_en": "[RCK+] Faction Disguises",
        "description_en": DISGUISE_DESCRIPTION,
        "in_head": True,
        "extension": True,
    }


QUEST_PREFIX = "rck-quest:::"
QUEST_MUTATOR = "RCK_Radiant_Quests"
QUEST_RADIANT_TRAIT = "RCK_Radiant_Quest_Giver"
QUEST_NO_TRAIT = "RCK_No_Quests"
QUEST_TARGET_PREFIX = "RCK_Quest_Target_"
QUEST_LABELS = ["A", "B", "C", "D"]
QUEST_TRAITS = {
    QUEST_RADIANT_TRAIT: ("Radiant Quest Giver", "Always offers a generated job for this level (its Talk text comes "
                          "back once the job is over), with or without the Radiant Quests mutator."),
    QUEST_NO_TRAIT: ("No Quests", "Never picked to give a generated job."),
    **{QUEST_TARGET_PREFIX + x: (f"Quest Target {x}", f"Marks the NPC for quest scripts' `Target: Label:{x}` "
                                                      f"(every holder in the level).") for x in QUEST_LABELS},
}
QUEST_MUTATOR_DESCRIPTION = ("A few NPCs offer generated odd jobs: faction members want rivals' leaders hit, their "
                             "numbers thinned, stolen goods recovered, gear wrecked or messages run; civilians want "
                             "pickpockets dealt with and parcels delivered.")
QUEST_KEYS = ["Id", "Title", "Type", "Target", "Item", "Count", "Reward", "Report", "After",
              "Offer", "Accept", "Remind", "Done", "Fail", "Target text", "Wait", "Idle"]
QUEST_TYPES = {
    "Kill": "Take down the target set (killed, knocked out, arrested, ghosted or zombified all count). Aliases "
            "Neutralize, Hit.",
    "Retrieve": "Bring the giver Count (default 1) of Item. With a Target, the item is put on the first living "
                "matching NPC (or into the first intact matching object) when the job is taken; without one, the "
                "player finds it. Always reported to the giver. Aliases Fetch, Steal.",
    "Destroy": "Wreck the matching objects (an Object target). Alias Sabotage.",
    "Deliver": "The giver hands the player Count (default 1) of Item (default Briefcase) on accepting; hand them to "
               "any living matching NPC. A full inventory refuses the job.",
    "Talk": "Talk to any living matching NPC (its `Pass on the word` button). Alias Message.",
}
QUEST_TARGETS = {
    "Label:A": "Every NPC with `RCK_Quest_Target_A` (A to D) in the level.",
    "Agent:Name": "NPCs whose name (agentRealName) or agent type is Name, case-insensitive.",
    "Agent:#id": "The NPC with that agent ID (radiant jobs use this).",
    "Leader:<faction>": "The faction's `<key>_Leader` holders. `Leader:Rival` picks the giver's bitterest rival "
                        "faction present with a leader standing.",
    "Faction:<faction>": "The faction's living members when the job is taken (a Kill needs Count, default 3, of "
                         "them). `Faction:Rival` picks the bitterest rival with members standing.",
    "Object:Name[@owner]": "Objects named Name; `@<faction>` only those owned by the faction's members (same owner "
                           "ID and chunk), `@Rival` the bitterest rival's, `@Giver` the giver's own.",
}
QUEST_REWARDS = {
    "$N": "Money (also `Money:N`), 1 to 100000.",
    "Item:Name": "An item (`Item:Banana x3` or `Item:Banana*3` for several). What doesn't fit drops at the "
                 "player's feet.",
    "XP": "Experience, as a vanilla completed mission.",
    "Recruit": "The giver joins the party (a leader sends its nearest non-leader member instead), within vanilla's "
               "follower cap.",
    "Standing": "Every member of the giver's faction turns Friendly toward the player.",
}
QUEST_PLACEHOLDERS = {
    "giver": "The giver's name (`the Bartender` for an unnamed NPC).",
    "faction": "The giver's faction as a group (`the Crepes`, or a `[RCK]FactionName::` name); `my people` without.",
    "rival": "The rival faction the job is about, as a group; `the competition` without.",
    "target": "The target set (`Big Tony`, `the Blahd leader`, `3 Blahds`, `the Generator`); for a Retrieve, who "
              "or what holds the item.",
    "item": "The item's display name.",
    "count": "How many are needed.",
    "reward": "The reward list (`$150, Revolver and experience`).",
    "objective": "What the job asks (`take down 3 of the Blahds`, `bring back the Briefcase (Big Tony has it)`).",
}
QUEST_RULES = (
    "An NPC whose Talk text starts with `rck-quest:::` is a quest giver: the text is a script of one or more jobs "
    "(stages), separated by `---` lines. Each stage is `Key: value` lines; a line that doesn't start with a key "
    "continues the previous value, so texts can span lines. Stages are offered one at a time in order. The giver shows "
    "`Hear them out` and `Take the job (Title)`; while the job runs, `About the job`; once it's done, `Report back` "
    "(the reward), unless `Report: Auto` pays the moment it's done (a Deliver or Talk pays at the recipient). A stage "
    "with `After: id+id` waits (showing its Wait text) until those jobs are done this level, which can chain givers. "
    "The job fails, and the chain stops, if the giver falls or turns hostile, or every Deliver/Talk recipient falls; "
    "the giver then says the Fail text. Jobs are marked on the map but never added to the quest list: a giver with an "
    "offer shows a `!` (faded while its job runs, a tick when it's time to report), and targets, recipients and item "
    "holders a yellow arrow (up to 8 per job); host only. A stage whose target can't be found "
    "yet shows its Wait text instead of an offer. Givers that are Hostile or Annoyed toward the player offer nothing. "
    "Paying turns the giver Friendly. Texts are cut to 600 characters with the job and reward lines. Invalid stages "
    "are dropped and logged. Jobs are per level and run on the host; in multiplayer the host takes them."
)
QUEST_FORMAT = (
    "`rck-quest:::` then stage lines. Keys: `Id` (for After and the gate; default: the title slugged), `Title`, "
    "`Type` (Kill, Retrieve, Destroy, Deliver, Talk), `Target` (see targets), `Item`, `Count` (1 to 99), `Reward` "
    "(comma-separated, see rewards), `Report` (Giver or Auto), `After` (ids joined with `+` or `,`), and the texts "
    "`Offer`, `Accept`, `Remind`, `Done`, `Fail`, `Target text` (what a Deliver/Talk recipient says), `Wait` (before "
    "After is met) and `Idle` (once, anywhere: what the giver says after the last stage). Keys ignore case, spaces, "
    "`_` and `-`. Texts may use the placeholders; a capitalised one (`{Target}`) capitalises the value."
)
QUEST_GATE = ("`Quest=id+id`: true once every listed job id is done (paid) this level. Radiant job ids are "
              "`radiant-<agentID>`.")
QUEST_RADIANT = (
    "With the `RCK_Radiant_Quests` mutator, up to 4 NPCs (6.4 units apart, faction members with a rival first) get "
    "one generated job at level start; `RCK_Radiant_Quest_Giver` holders always get one on top. Never picked: NPCs "
    "with Talk text (unless they hold the trait), quest givers, targets and prisoners, hired NPCs, `RCK_No_Quests` "
    "holders, NPCs hostile or annoyed toward a player, and Zombie, Gorilla, CopBot, ButlerBot, Robot, Alien, Ghost, "
    "WerewolfB, Assassin, ShapeShifter or Slave. A faction member with a rival present offers Hit (the rival's "
    "leader), Thin (3 of the rival), Recover (an item from a rival member), Sabotage (a rival-owned machine), or runs "
    "Courier/Word to a fellow member; anyone else offers Pickpocket/Pest (a Thief, Cannibal, Slavemaster or Vampire) "
    "or Parcel/CheckIn (another civilian). Pay is $60 + $20 per level times the job's multiplier, plus XP, plus "
    "Standing for rival jobs."
)


def _quest_trait(tid: str) -> dict:
    label, _ = QUEST_TRAITS[tid]
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Quests",
        "bases": ["T_Quests", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Quests/{tid}.cs",
        "display_en": f"[RCK+] Quests - {label}",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [QUEST_NO_TRAIT] if tid == QUEST_RADIANT_TRAIT
            else [QUEST_RADIANT_TRAIT] if tid == QUEST_NO_TRAIT else [],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "quests": True,
    }


def _quest_mutator() -> dict:
    return {
        "versions": ["RCK"],
        "class": "MutatorUnlock",
        "path": "RCK/Systems/Quests/QuestsModule.cs",
        "bases": [],
        "display_en": "[RCK+] Radiant Quests",
        "description_en": QUEST_MUTATOR_DESCRIPTION,
        "in_head": True,
        "extension": True,
    }


SQUAD_RULES = (
    "Raids, backup, turf guards that walk in and respawns are squads. A squad is made of the faction's designated "
    "units (see `extensions.respawn`) when it has any, else of copies of one of the faction's own NPC members (not a "
    "leader, backup caller, broker, quest giver or target, gate agent, Relationless or important NPC, nor one in a "
    "party). Raid and backup squads appear at the post of their faction's own turf nearest their target, out of the "
    "players' sight and clear of foes; with no such turf, or for other squads, next to a member out of the players' "
    "sight when there is one (respawns: see `extensions.respawn`). Every member is armed as the level loader arms a "
    "placed NPC: with the items its source was placed with, else its type's random weapon, items and money; a custom "
    "character still without a weapon then gets its character's starting items. A squad is never street-innocent. "
    "It's Hateful (hate 5) toward its targets, and targets that aren't street-innocent are Hateful back; innocent "
    "ones are only attacked, so they don't start fights of their own. Squad-mates keep their faction's own ties to "
    "each other. A raid or backup squad walks to its spot, fights, searches around three times, walks back to where "
    "it spawned and vanishes, like vanilla's alarm squads. At most 12 raid, backup and guard members are alive at "
    "once, and 24 respawned members on their own cap. Only the host spawns them."
)
SQUAD_LIMIT = 12

TURF_MUTATOR = "RCK_Turf_Capture"
TURF_DESCRIPTION = ("When every holder of a faction's turf falls, the turf changes hands: the faction that took it "
                    "moves in to guard it.")
TURF_RULES = (
    "A turf is the owned floor one crew holds: the NPCs with the same owner ID and start chunk that defend it (a "
    "`<key>_Territorial` trait or a `=Territorial` matrix entry) and belong to a faction. RCK lists the turfs once "
    "the level has loaded. A turf falls when every holder is dead, knocked out, arrested, zombified, gone, hired by a "
    "player or routed (see `<key>_Leader`). With the `RCK_Turf_Capture` mutator it then changes hands, to the "
    "faction of whoever took down the last holder: a player or a follower takes it for the player's faction "
    "(`<key>_Member`, `<key>_Aligned` or the class's own), an NPC for its own. A fall with no taker (an accident, "
    "infighting) just clears it. The new owner moves in: up to 3 of its NPC members that own nothing and stand "
    "nearby (the one that took it first) become the turf's guards, else 2 of its squad units (see `squads`) walk in (a "
    "squad). Guards take the old holders' posts and owner ID, so the ones with Territorial traits defend it as the old "
    "holders did, and a turf can change hands again. The players on the host see a short message (`Blahd turf taken "
    "by the Crepes`) and the BepInEx log gets a line. Without the mutator nothing changes hands, but the `TurfTaken` "
    "gate switch still counts falls."
)
TURF_GATE = ("`TurfTaken=Blahd` (`Blahd+Crepe`: all of them): the faction held at least one turf when the level "
             "loaded and holds none now.")

RAID_MUTATOR = "RCK_Faction_Raids"
RAID_PREFIX = "[RCK]FactionRaid::"
RAID_DESCRIPTION = ("Every few minutes a squad from one faction raids a rival it hates: they march over, fight, "
                    "search the place and go home.")
RAID_FORMAT = ("`[RCK]FactionRaid::` followed by `;`-separated entries `A>B@T` or `A>B@TxN`: T seconds (0 to 3600) "
               "after the level loads, a squad of N (default 3, 1 to 6) members of A raids B. A and B are a faction "
               "number or key. Each entry fires once per level load, and is skipped when either side is routed or "
               "A has no one to send or B no one to hit.")
RAID_RULES = (
    "A raid squad (see `squads`) of the raiding faction walks to one of the target's turfs (a holder's post; see "
    "`extensions.turf`), else to a random NPC member of the target, and raids it. The players on the host see `The "
    "Crepes are raiding the Blahds!`. With the `RCK_Faction_Raids` mutator raids also happen on their own: the first 90 to "
    "150 s after the level loads, then every 150 to 240 s, at most 3 per level, each between a random pair of "
    "factions present where A is Hateful or Territorial toward B (by the members' traits or the matrix) and neither "
    "is routed. Scheduled entries and the mutator work together."
)
RAID_DEFAULTS = {"size": 3, "max_size": 6, "first_seconds": [90, 150], "gap_seconds": [150, 240], "max_auto": 3}
RAID_EXAMPLES = ["[RCK]FactionRaid::Crepe>Blahd@120;", "[RCK]FactionRaid::1>Blahd@60x5;Blahd>1@240;"]

BACKUP_ROLE = "Calls_Backup"
BACKUP_RULES = (
    "A `<key>_Calls_Backup` holder calls for help. When someone outside `<key>` really attacks it (a hit the game "
    "reports as an assault), or when it or an NPC of its owner group presses an Alarm Button against a criminal, "
    "`<key>` sends a squad (see `extensions.raids`) of 3 to the holder or the alarm. The squad is Hateful toward the "
    "attacker, searches, walks back and vanishes. A faction calls at most 2 squads per level, at least 30 s apart, "
    "and none once it's routed. Squad members never call backup themselves. When the attacker is a player or a "
    "follower, the players on the host see `The Blahds called for backup!`. The role doesn't make the holder a "
    "member."
)
BACKUP_LIMITS = {"squad": 3, "calls_per_level": 2, "cooldown_seconds": 30}

RESPAWN_MUTATOR = "RCK_Faction_Respawn"
RESPAWN_PREFIX = "[RCK]FactionRespawn::"
RESPAWN_ROLE = "Reinforcement"
RESPAWN_DESCRIPTION = ("Factions that hold turf send fresh squads to it when their numbers run low, until they've lost "
                       "all their turf.")
RESPAWN_RULES = (
    "A faction that holds turf (see `extensions.turf`) keeps its numbers up. Its target is `Free + PerTurf x` the "
    "turfs it holds now, at most `Max`. Every 2 s the host counts its living NPC members (not zombies, ghosts or "
    "players' hires, but squads count). When that's under the target and its cooldown has passed (the first respawn "
    "comes one cooldown after the level loads), it respawns a squad (see `extensions.raids`) of up to `Size`, no more "
    "than it's missing. The squad appears at a post (where a holder stood at load) of one of its turfs that no player "
    "is within 13 units of, no holder is fighting at and no foe member is within 5 units of; turfs with empty posts "
    "come first. It first restaffs its faction's empty posts, the home turf's first, taking the post and the turf's "
    "owner ID like a holder. With `RCK_Turf_Capture` on, the rest march together on the nearest turf that's empty or "
    "held by a foe (a faction it's Hostile or Territorial toward by traits or matrix, not routed, without a truce). "
    "They fight their way in: when they take down the last holder the turf changes hands as usual, and an empty or "
    "undefended turf is theirs once one of them is within 2 units of a post (`Blahd turf taken by the Crepes`). "
    "Either way they hold it. Marchers pick a new target when theirs stops being a foe's or after 300 s, and hold "
    "their faction's nearest turf when there's none. Without `RCK_Turf_Capture` the rest become extra holders of the "
    "home turf. A faction's pool is unlimited (`inf`) or a number of members per level. A faction that holds no turf "
    "and has no one marching is finished for the level: no more respawns, and the players see `The Blahds have lost "
    "all their turf!`. A routed faction's turfs fall, so it's finished too. A faction without turf at load never "
    "respawns. At most 24 respawned members are alive at once, all factions together."
)
RESPAWN_FORMAT = (
    "The `RCK_Faction_Respawn` mutator turns respawn on for every faction that holds turf when the level loads, with "
    "the defaults and an unlimited pool. `[RCK]FactionRespawn::` followed by `;`-separated entries tunes it, and "
    "without the mutator turns it on for the factions it lists. `Faction=pool` or `Faction=pool:Unit+Unit` lists a "
    "faction (a number or key): the pool is `inf`, `off` (0: no respawns, though the units still apply) or 1 to 999 "
    "members per level, and the units are what its squads are made of (see `units`). `Setting=N` sets a default for "
    "the level and `Faction.Setting=N` one faction's own: `Free` (0 to 30), `PerTurf` (0 to 10), `Max` (1 to 30), "
    "`Cooldown` (10 to 600 s) and `Size` (1 to 6). Later entries win, and bad entries are skipped with a warning in "
    "the BepInEx log."
)
RESPAWN_UNITS = (
    "A faction's designated units are its placed NPCs holding `<key>_Reinforcement` (each counts once, so place two "
    "to double a unit's chance) plus the units its `[RCK]FactionRespawn::` entry names. A unit name is a custom "
    "character's name (one placed somewhere in the level, as its data lives there), a placed NPC's type or a vanilla "
    "agent type (`Gangbanger`, `Soldier`; any case). Leaders, gate agents, quest NPCs, brokers and Relationless NPCs "
    "are never units. Each unit is snapshotted when the level loads, so it keeps coming after the original dies, and "
    "the original stays a normal NPC. Every squad of the faction spawns from its units, one picked at random per "
    "member: raids, backup, guard copies and respawns. A unit that isn't a member of the faction (a vanilla type for a "
    "numbered faction) gets `<key>_Aligned`. A faction without designated units copies its members as before, and "
    "respawns from its plain members at load (see `squads`)."
)
RESPAWN_DEFAULTS = {"Free": 2, "PerTurf": 2, "Max": 12, "Cooldown": 60, "Size": 3, "pool": "inf"}
RESPAWN_RANGES = {"Free": [0, 30], "PerTurf": [0, 10], "Max": [1, 30], "Cooldown": [10, 600], "Size": [1, 6],
                  "pool": [0, 999]}
RESPAWN_LIMITS = {"max_respawned_alive": 24, "max_units_per_entry": 12, "tick_seconds": 2, "player_clear": 13,
                  "foe_clear": 5, "claim_radius": 2, "march_seconds": 300, "retry_seconds": 10}
RESPAWN_EXAMPLES = [
    "[RCK]FactionRespawn::Crepe=inf;Blahd=20;",
    "[RCK]FactionRespawn::Cooldown=45;Crepe=inf:Gangbanger+Crepe Heavy;Crepe.Size=4;",
    "[RCK]FactionRespawn::Faction_1=40:Contractor Grunt;Free=4;PerTurf=1;Max=10;",
]

BROKER_TRAIT = "RCK_Broker"
BROKER_RULES = (
    "An NPC with `RCK_Broker` deals in faction politics. When it isn't Hostile or Annoyed toward the player and at "
    "least two factions have NPC members here (living, not routed, not in a party; `Common_Folk` only through its "
    "traits), it offers `Broker a truce`: the two chosen factions turn Neutral toward each other, both ways, for the "
    "rest of the level, with hate and turf standoffs cleared (not offered while the pair already has one). It also "
    "offers `Frame a faction`: the second faction turns Hateful toward the first for the rest of the level. `Change "
    "first faction` and `Change second faction` "
    "cycle the choices, which the broker keeps for the level. Both deals are level relationship rules: they beat "
    "faction traits and the matrix, but not Vengeful grudges or routs, and a frame holds back from street innocents "
    "until they're caught. The holder needs no faction."
)
BROKER_PRICES = {"Broker a truce": "$100 + $25 per level", "Frame a faction": "$75 + $25 per level"}

NOINFIGHT_MUTATOR = "RCK_No_Infighting"
NOINFIGHT_TRAIT = "DontHitAligned"
NOINFIGHT_DESCRIPTION = ("Every NPC gets No In-Fighting: aligned NPCs, and a player's followers, stop hurting each "
                         "other in crossfire.")
NOINFIGHT_RULES = (
    "Every NPC gets the vanilla trait `DontHitAligned` (No In-Fighting) as it's set up: placed vanilla agents and "
    "customs, squads (raids, backup, guard copies, respawns) and anything spawned later (alarm cops, summons). NPCs "
    "that already have it are skipped, and a sweep every 2 s gives it back to an NPC whose traits were reset. Vanilla "
    "needs the trait on one side of a hit only: hits between two Aligned, Loyal or Submissive agents do no harm, nor "
    "do a follower's hits on its employer's allies. Players don't get it (on them it only adds co-op immunity, and "
    "it would show on the HUD and carry over to later levels); their followers are NPCs, so they're covered. The "
    "rule works both ways, so a player can't hurt an NPC that's Aligned, Loyal or Submissive to them either, until "
    "it turns hostile. Live quest kill targets can still be hit, as in vanilla. Runs on every peer."
)

TURF_OVERLAY_RULES = (
    "Whenever the level has turfs (see `rules`), the big map (the quest sheet's map) shows who holds them, with no "
    "mutator needed. Each turf's floor (the tiles of its owner ID and start chunk) is tinted in its holder's colour "
    "with a stronger outline; the players' own factions get a white outline. A cleared turf is grey, a turf shared by "
    "several factions is checkered in their colours, and a turf with no owned floor shows as a small disc around its "
    "posts. A contested turf, held while one of its holders is fighting, is striped. A legend in the map's top-left corner lists every "
    "faction that held turf at load (or holds some now) with its swatch, name (`[RCK]FactionName::` names apply), "
    "`(you)` for the players' factions and turfs held now; "
    "a routed faction or one holding none is greyed out. With rackets on (see `extensions.expansion`) each racket is "
    "dotted rather than solid: sand while it's free, its racketeers' colour while it's run, and gone once it's ruined; "
    "each legend row adds the rackets the faction runs, and a last row counts the free ones. It updates live while "
    "the map is open. The toggle key (F8 by default) hides and shows it. Host only: clients don't know the turfs. "
    "Not shown on streaming or home base levels."
)
TURF_OVERLAY_CONFIG = {
    "Map.TurfOverlay": "true (default) shows the overlay; false hides it until toggled.",
    "Map.TurfOverlayKey": ("The toggle key, a Unity KeyCode name (F8 by default; F7 is UnityExplorer's). The log warns "
                           "when the key is also bound in the player's SoR controls."),
}
TURF_OVERLAY_COLORS = {
    "Crepe": "blue",
    "Blahd": "red",
    "Mafia": "charcoal, gold outline",
    "Cop": "navy, light blue outline",
    "Thief": "purple",
    "Hacker": "green",
    "Faction_1 to Faction_20": "a fixed palette (orange, teal, pink, olive, cyan, magenta, brown, lime and so on)",
    "other named factions": "the same palette, by key",
    "cleared": "grey",
    "free racket": "sand, dotted",
    "run racket": "the racketeers' colour, dotted",
}

EXPANSION_MUTATOR = "RCK_Turf_Expansion"
EXPANSION_PREFIX = "[RCK]TurfWar::"
EXPANSION_DESCRIPTION = ("Factions that hold turf send small parties to take empty turf and run protection rackets on "
                         "the commoners' places near their own.")
EXPANSION_RULES = (
    "Every `Expand` seconds each faction that holds turf (see `extensions.turf`), isn't routed and isn't the one the "
    "player commands (see `extensions.command`) looks for the nearest empty turf, or free racket, within `Reach` "
    "tiles of one of its posts that none of its members is already marching on. It sends a party of up to `Party` "
    "members, nearest first: free roamers (NPC members that own nothing, are in no raid, backup or command squad and "
    "have no order) and spare holders (a second holder standing on the same post). The party marches over as a "
    "respawn squad does (see `extensions.respawn`): it's theirs once one of them is within 2 units of a post, and "
    "they hold it. Expansion is on with the `RCK_Turf_Expansion` mutator or any `[RCK]TurfWar::` entry, unless "
    "`Expand=0`, and it needs `RCK_Turf_Capture` or the commander for captures to stick."
)
RACKET_RULES = (
    "A racket is a place commoners own: a group of NPCs with the same owner ID and start chunk that belong to "
    "`Common_Folk` by their vanilla type and no other faction, where no faction member owns anything. RCK lists the "
    "rackets with the turfs; the commoners' spots are its posts, and the commoners never hold it. A free racket "
    "(sand on the map) is taken like empty turf: a faction walks in and guards it, by expansion or by command. While "
    "it's run, its commoners and the racketeers' NPC members are Aligned both ways, and the players see `The Crepes "
    "now run a protection racket`. The racket is broken (`Crepe racket broken`) when all its guards are resolved, and "
    "free again for anyone; it's ruined (`Crepe racket ruined`) when its commoners are all gone, and its guards go back "
    "to their faction's turf. A ruined racket gets a new, neutral owner 30 seconds later, once no player is within 8 "
    "tiles of its first post (or after 90 seconds regardless), up to `Reopen` times a level: the old owner's type if a "
    "Mobster can shake it down (Shopkeeper, Bartender, Clerk, DrugDealer or Athlete), else a Shopkeeper. The racket is "
    "free again, and the players see `A new owner took over a ruined racket`. Rackets never count as turf: not for "
    "respawn targets, raids, `TurfTaken`, finishing a "
    "faction or the legend's turf count. They pay control points (`PerCommon`) and weigh in the war panel's power. "
    "Rackets are on when expansion or the commander is, unless `Racket=0`."
)
EXPANSION_FORMAT = (
    "`[RCK]TurfWar::` followed by `;`-separated `Name=N` entries: `Expand` (seconds between a faction's moves, 0 for "
    "none or 15 to 600; under 15 counts as 15), `Racket` (1 lets factions run rackets, 0 stops it), `Reach` (tiles "
    "from a faction's own posts, 5 to 200), `Party` (members per move, 1 to 6) and `Reopen` (new owners a ruined "
    "racket gets per level, 0 to 9). Names are case-insensitive, later "
    "entries win, and bad entries are skipped with a warning in the BepInEx log."
)
EXPANSION_DEFAULTS = {"Expand": 45, "Racket": 1, "Reach": 30, "Party": 3, "Reopen": 3}
EXPANSION_RANGES = {"Expand": [0, 600], "Racket": [0, 1], "Reach": [5, 200], "Party": [1, 6], "Reopen": [0, 9]}
EXPANSION_LIMITS = {"min_expand_seconds": 15, "claim_radius": 2}
EXPANSION_EXAMPLES = ["[RCK]TurfWar::Expand=45;Racket=1;Reach=30;Party=3;", "[RCK]TurfWar::Expand=0;Racket=1;",
                      "[RCK]TurfWar::Racket=0;Party=2;"]

CP_PREFIX = "[RCK]ControlPoints::"
CP_RULES = (
    "Control points (CP) measure a faction's hold on the city and pay for the commander's recruits. Every `Interval` "
    "seconds each faction in the war (it held turf at load, holds or held some since, or is commanded) that isn't "
    "routed earns `Base + PerTurf x` its turfs `+ PerCommon x` its rackets, up to `Max`. A faction with no turf and no "
    "racket earns nothing, except the commanded one, which keeps `Base`. Taking a turf pays `Capture` (half for a "
    "racket). Taking down an NPC member of another faction pays `Kill` to the killer's side: a player's follower "
    "counts for the player, a player for the faction they command (else their own), and a squad member for its "
    "squad's faction. Every faction starts with `Start`. CP is on with any `[RCK]ControlPoints::` entry or the "
    "commander. Host only; the points belong to one level load."
)
CP_FORMAT = (
    "`[RCK]ControlPoints::` followed by `;`-separated `Name=N` entries: `Base`, `PerTurf` and `PerCommon` (0 to 50), "
    "`Capture` (0 to 500), `Kill` (0 to 100), `Max` (10 to 9999), `Interval` (5 to 120 s) and `Start` (0 to 9999, at "
    "most `Max`). Names are case-insensitive, later entries win, and bad entries are skipped with a warning."
)
CP_DEFAULTS = {"Base": 1, "PerTurf": 2, "PerCommon": 1, "Capture": 10, "Kill": 1, "Max": 200, "Interval": 10,
               "Start": 30}
CP_RANGES = {"Base": [0, 50], "PerTurf": [0, 50], "PerCommon": [0, 50], "Capture": [0, 500], "Kill": [0, 100],
             "Max": [10, 9999], "Interval": [5, 120], "Start": [0, 9999]}
CP_POWER = ("Each faction's power is its share, in percent, of the sum over all factions of `10 x turfs + 4 x "
            "rackets + living NPC members + CP / 5` (CP counts 0 while points are off).")
CP_EXAMPLES = ["[RCK]ControlPoints::Start=50;PerTurf=3;", "[RCK]ControlPoints::Interval=5;Kill=2;Capture=25;Max=400;"]

COMMAND_MUTATOR = "RCK_Commander"
COMMAND_PREFIX = "[RCK]Command::"
COMMAND_DESCRIPTION = ("You lead your faction in the turf war: spend control points on squads and send them to "
                       "capture and defend turf from the commander console (T).")
COMMAND_RULES = (
    "The host commands one faction for the level. It's the `Faction=` key, else the host's first faction that held "
    "turf at load, else the host's own faction (never `Common_Folk`); with none, the log says so and nothing changes. "
    "The commanded faction's automatic respawn, raids (scheduled ones too), backup calls and expansion stop; the other "
    "factions carry on as before. Turf capture (`RCK_Turf_Capture`), control points (see `extensions.control_points`) "
    "and rackets come on with it. The players see `You command the Crepes (T)` and the console opens. Recruiting a "
    "unit raises a squad of up to `Size`, fewer when the cap or the faction's CP runs short, for the unit's cost each. "
    "It appears at the post of the faction's own turf nearest the player that's clear of foes, else beside the "
    "player. Recruits are "
    "sworn to the commanded faction alone, whatever their type (a recruited Cop drops its ties to other cops), and "
    "first hold the nearest post of their faction's turf. Orders go to one squad or all of them: `Capture` (a foe's "
    "turf, an empty turf or a free racket; on the faction's own turf it's `Defend`), `Defend` (empty posts first), "
    "`Recall` (walk to the player and wait) and `Disband` (the members vanish and `Refund` percent of what they cost "
    "comes back). A turf held by a faction that isn't a foe needs a war declaration first: `Declare war` (two clicks "
    "within 4 s) makes the faction and the chosen one Hateful toward each other for the rest of the level, and the "
    "players see `War on the Blahds!`. A captured turf is held as usual. A manual order that hasn't finished in 600 s "
    "lapses and the squad holds its faction's nearest turf. A squad with nobody left is dropped (`Squad 2 wiped out`). "
    "The console is a window with a map of the whole level: walls and floors, every turf and racket in its holders' "
    "colours (striped while under attack, a white edge for the commanded faction's), the player, other factions' "
    "members as dots and the commanded squads as numbered discs with a line to where each is headed. Hovering shows "
    "what's under the mouse. Click a squad (on the map or its chip in the squad list), or `All squads`, then click a "
    "turf or racket to capture it (defend it if it's the faction's own), or open ground to `Move` there (walk and "
    "wait, holding nothing); right-click clears the pick. Beside the map are the recruit list, the squad list with "
    "`Recall` and `Disband` (two clicks within 3 s) for the pick, and `Declare war`. It uses the game's own font. "
    "The commander is on with the `RCK_Commander` mutator or any `[RCK]Command::` entry. Host only: only the host "
    "sees and uses the console."
)
COMMAND_FORMAT = (
    "`[RCK]Command::` followed by `;`-separated entries. `Faction=key` (a number or key) picks the faction. "
    "`Units=A+B+C` lists up to 12 recruitable units of any faction: a custom character's name (placed somewhere in the "
    "level), a placed NPC's type or a vanilla agent type, as for respawn units (see `extensions.respawn`); `Name:N` "
    "gives one its own cost (0 to 500), and a colon not followed by digits stays in the name. Names are at most 64 "
    "characters. Without `Units` the console offers the faction's respawn units, else a copy of one of its members. "
    "`Cost=N` (0 to 500 CP per recruit), `Size=N` (1 to 6 per squad), `Cap=N` (1 to 24 recruits alive at once) and "
    "`Refund=N` (0 to 100 percent) tune it. Names are case-insensitive, later entries win, and bad entries are "
    "skipped with a warning."
)
COMMAND_DEFAULTS = {"Faction": "the host's faction", "Cost": 10, "Size": 3, "Cap": 12, "Refund": 50}
COMMAND_RANGES = {"Cost": [0, 500], "Size": [1, 6], "Cap": [1, 24], "Refund": [0, 100]}
COMMAND_LIMITS = {"max_units": 12, "max_unit_name": 64, "declare_confirm_seconds": 4, "disband_confirm_seconds": 3,
                  "order_seconds": 600}
COMMAND_ORDERS = {
    "Recruit": "Raises a squad of a unit for its cost each.",
    "Capture": "Marches on a foe's turf, an empty turf or a free racket and holds it once taken.",
    "Defend": "Holds one of the faction's own turfs, empty posts first.",
    "Recall": "Walks to the player and waits there.",
    "Move": "Walks to a spot clicked on the map and waits there, holding nothing.",
    "Disband": "Two clicks within 3 s: sends the members home for `Refund` percent of their cost.",
    "Declare war": "Two clicks within 4 s: the commanded faction and the chosen one turn Hateful toward each other.",
}
COMMAND_CONFIG = {
    "Map.CommandConsoleKey": ("Hides and shows the console (T by default, free in SoR's default controls; None turns "
                              "the key off). A config from a build that used F9 moves to T once, and the console and "
                              "the log warn when the key is also bound in the player's SoR controls."),
}
COMMAND_EXAMPLES = [
    "[RCK]Command::Faction=Crepe;",
    "[RCK]Command::Faction=Crepe;Units=Gangbanger+Crepe Heavy:25;Cost=10;",
    "[RCK]Command::Units=Faction_1 Grunt+Cop:20;Size=4;Cap=16;Refund=75;",
]

WAR_PANEL_MUTATOR = "RCK_War_Panel"
WAR_PANEL_DESCRIPTION = "Shows the turf-war panel from the start: each faction's turf, men, control points and power."
WAR_PANEL_RULES = (
    "The war panel ranks every faction in the level's turf war, strongest first: its colour swatch, name "
    "(`[RCK]FactionName::` names apply; `(yours)` for the commanded faction, `(you)` for the players' own, `routed` "
    "once routed), the turfs it holds, rackets it runs, living members, CP (while points are on) and a bar of its "
    "power (see `extensions.control_points`). A faction with nothing left is greyed out, and the last line counts the "
    "free rackets. It refreshes every second. The commander console sits beside it. Both are windows that can be "
    "dragged by the title, scale with the screen height and hide behind the pause menu; clicks on them don't fire the "
    "player's weapon. The panel key (G by default) shows it in any level with a turf war; it opens by itself with "
    "the `RCK_War_Panel` mutator or the commander. Mouse only. Host only."
)
WAR_PANEL_CONFIG = {
    "Map.WarPanelKey": ("Hides and shows the war panel (G by default, free in SoR's default controls; None turns the "
                        "key off). A config from a build that used F6 moves to G once, and the panel and the log warn "
                        "when the key is also bound in the player's SoR controls."),
}

MEDIC_TRAIT = "RCK_Faction_Medic"
MEDIC_RULES = (
    "An NPC with `RCK_Faction_Medic` patches up its friends. Every 5 s it heals the hurt friend within 3.2 units (5 "
    "tiles) with the lowest share of their health, itself included: `max(8, 15%` of the patient's most health`)`, no "
    "more than the patient is missing, with the vanilla heal sound. Friends are its employer and fellow followers, "
    "its own followers, the players while the medic belongs to the faction they command, and anyone sharing one of "
    "its factions; never someone it's Hostile or Annoyed toward. A dead, arrested or zombified medic doesn't heal, "
    "nor does a player. Works on recruits, squads and respawn units like any other trait. Host only."
)
MEDIC_LIMITS = {"range_units": 3.2, "range_tiles": 5, "cooldown_seconds": 5, "heal_share": 0.15, "min_heal": 8}

RACKETEER_TRAIT = "RCK_Faction_Racketeer"
RACKETEER_RULES = (
    "A player with `RCK_Faction_Racketeer` can lean on a free racket: talking to one of its commoners shows `Shake "
    "down for <faction> (n%)`, where the faction is the one the player commands, else the player's own (its "
    "`<key>_Member`/`_Aligned` traits first, then vanilla membership; never `Common_Folk` or a routed faction). The "
    "chance is vanilla's shakedown chance (the commoner's fear of the player), 100% if the commoner is at 40% health "
    "or less, Aligned with the player, or the player's slave. On success the commoner gives in and the racket joins "
    "that faction, exactly as if its squad had taken it (the commoners side with the faction, the turf overlay and "
    "control points follow); no money changes hands, and nobody is posted to guard it. On failure the commoner turns "
    "Hateful and attacks, and nearby police hear it, as in vanilla. Only free rackets (nobody runs it, not ruined), "
    "so only while rackets are on (turf expansion or the commander, and `Racket` isn't 0; see turf). Players only: an "
    "NPC with the trait does nothing. Host only."
)


def _war_mutator(path: str, display: str, description: str) -> dict:
    return {
        "versions": ["RCK"],
        "class": "MutatorUnlock",
        "path": path,
        "bases": [],
        "display_en": display,
        "description_en": description,
        "in_head": True,
        "extension": True,
    }


def _broker_trait() -> dict:
    return {
        "versions": ["RCK"],
        "kind": "designer",
        "namespace": "RCK.Traits.Rel_Faction",
        "bases": ["T_Rel_Faction", "T_Relationships", "T_DesignerTrait", "CustomTrait"],
        "path": f"RCK/Systems/Designer Traits/Relationships/Relationships - Faction/{BROKER_TRAIT}.cs",
        "display_en": "[RCK+] Rel Faction - Broker",
        "unlock_class": "TU_DesignerUnlock",
        "unlock": {
            "Cancellations": [],
            "CharacterCreationCost": 0,
            "IsAvailable": False,
            "IsAvailableInCC": {"expr": "Core.designerEdition"},
            "UnlockCost": 0,
        },
        "rolls": None,
        "in_head": True,
        "extension": True,
        "broker": True,
    }


def _medic_trait() -> dict:
    trait = _broker_trait()
    del trait["broker"]
    trait["path"] = f"RCK/Systems/Designer Traits/Relationships/Relationships - Faction/{MEDIC_TRAIT}.cs"
    trait["display_en"] = "[RCK+] Rel Faction - Medic"
    trait["medic"] = True
    return trait


def _racketeer_trait() -> dict:
    trait = _broker_trait()
    del trait["broker"]
    trait["path"] = f"RCK/Systems/Designer Traits/Relationships/Relationships - Faction/{RACKETEER_TRAIT}.cs"
    trait["display_en"] = "[RCK+] Rel Faction - Racketeer"
    trait["racketeer"] = True
    return trait


GOAL_RULES = (
    "CCU lists these default goals in the editor; RCK makes them work. The game runs each as its own WanderFar, so "
    "the NPC fights, flees, investigates and reacts like any wanderer; RCK picks every stop. A stop at an NPC, an "
    "object or a patrol spot lasts 2 to 4.5 s. With nobody or nothing to visit, a Wander Between goal patrols its "
    "start chunk instead. Doors, windows, bars, security, hazards, plants and fixtures are never stops. Only the "
    "host picks stops, as with vanilla goals."
)
GOALS = {
    "Random Patrol (Chunk)": "Walks between random spots in its start chunk.",
    "Random Patrol (Map)": "Crosses the map: each stop is a random public spot at least a chunk away, with a 1 to "
                           "2 s pause. Street-side for street innocence, like WanderFar.",
    "Wander Between Agents": "Walks up to a random NPC within 2 chunks of its start (never a player, nor one it's "
                             "Hostile or Annoyed toward), then another.",
    "Wander Between Agents (Owner)": "The same, among the NPCs of its owner group (same owner ID and start chunk), "
                                     "wherever they are.",
    "Wander Between Agents (Non-Owner)": "The same, among the NPCs within 2 chunks outside its owner group.",
    "Wander Between Objects (Owner)": "Walks between the objects its owner group owns (same owner ID and chunk).",
    "Wander Between Objects (Non-Owner)": "Walks between the objects within 2 chunks that its owner group doesn't "
                                          "own.",
}

GATE_RULES = (
    "RCK reads more than CCU's Agent switches in a `[CCU]LevelGate::` or `[RCK]LevelGate::` string. `Label`, "
    "`Switch` and `Logic` work as in CCU, and labels are optional. `Count=N` replaces the logic: at least N of the "
    "label agents resolved (dead, knocked out, arrested, hired, zombified and so on). Every other `Name=value` pair "
    "is a condition, and the exit opens when the label part (if any) and every condition hold. A gate with an "
    "unknown condition stays shut, and the BepInEx log says so. When the exit refuses, the log lists what isn't met "
    "yet. The conditions are checked on the host."
)
GATE_CONDITIONS = {
    "Routed": "`Routed=Crepe` (or `Crepe+Blahd`, all of them): the faction's last `<key>_Leader` fell this level.",
    "Destroyed": "`Destroyed=Generator` (or `Generator+PowerBox`): at least one object of that name was destroyed "
                 "this level and none is left standing.",
    "Holding": "`Holding=Briefcase` (`Briefcase*2`, `Briefcase+Key`): the player at the exit carries them.",
    "Quest": "`Quest=id+id`: every listed RCK quest is done and paid this level.",
    "TurfTaken": TURF_GATE,
}
GATE_KEYS = {
    "Type": "`Entry` (the default); other types aren't gates.",
    "Label": "Agent switch labels `A` to `D` (or 1 to 4), comma-separated; also `Labels`.",
    "Switch": "`Agent`; also `Switches`. Labels are ignored unless it lists Agent.",
    "Logic": "AND (default), OR, NAND, NOR, XOR or XNOR over the label agents.",
    "Count": "1 to 99: at least that many label agents resolved; replaces Logic.",
    "Open": ("Text (up to 120 characters, no `;`) the host's players see once, in the turf-message style, the first "
             "time every part of the gate holds (checked every second). Not a condition."),
}
GATE_EXAMPLES = [
    "[CCU]LevelGate::Type=Entry;Label=A;Count=3;",
    "[RCK]LevelGate::Routed=Faction_2;Destroyed=Generator;",
    "[RCK]LevelGate::Label=A,B;Logic=OR;Holding=Briefcase;Quest=loose-lips;",
    "[RCK]LevelGate::TurfTaken=Blahd+Crepe;",
    "[RCK]LevelGate::Type=Entry;TurfTaken=Blahd+Mafia;Open=The Crepes run the city. The exit is open.;",
]


def build(harvested_ids: set[str]) -> dict[str, dict]:
    out = {}
    for key in faction_keys():
        for grade in GRADES:
            tid = f"{key}_{grade}"
            if tid not in harvested_ids:
                out[tid] = _trait(key, grade)
        tid = f"{key}_{MEMBER}"
        if tid not in harvested_ids:
            out[tid] = _member(key)
        for role in ROLES:
            tid = f"{key}_{role}"
            if tid in harvested_ids:
                raise SystemExit(f"faction role trait {tid} collides with a CCU name")
            out[tid] = _role_trait(key, role)
    out[DEFECTOR_TRAIT] = _defector_trait()
    for tid in RECRUIT_TRAITS:
        out[tid] = _recruit_trait(tid)
    out[INNOCENCE_TRAIT] = _innocence_trait()
    for tid in QUEST_TRAITS:
        out[tid] = _quest_trait(tid)
    out[BROKER_TRAIT] = _broker_trait()
    out[MEDIC_TRAIT] = _medic_trait()
    out[RACKETEER_TRAIT] = _racketeer_trait()
    return out


def _merge_section(section: dict, added: dict[str, dict], what: str) -> None:
    for name in [n for n, e in section.items() if e.get("extension")]:
        del section[name]
    clash = set(section) & set(added)
    if clash:
        raise SystemExit(f"extension {what} collide with CCU names: {sorted(clash)}")
    for name in sorted(added):
        section[name] = added[name]


def merge(spec: dict) -> dict:
    traits = spec["traits"]
    harvested = {t for t, e in traits.items() if not e.get("extension")}
    _merge_section(traits, build(harvested), "trait IDs")
    mutators = {name: _recruit_mutator(name) for name in RECRUIT_MUTATORS}
    mutators[INNOCENCE_MUTATOR] = _innocence_mutator()
    mutators[DISGUISE_MUTATOR] = _disguise_mutator()
    mutators[QUEST_MUTATOR] = _quest_mutator()
    mutators[TURF_MUTATOR] = _war_mutator("RCK/Systems/Social/Turf/TurfWar.cs", "[RCK+] Turf Capture", TURF_DESCRIPTION)
    mutators[RAID_MUTATOR] = _war_mutator("RCK/Systems/Social/War/FactionRaids.cs", "[RCK+] Faction Raids", RAID_DESCRIPTION)
    mutators[RESPAWN_MUTATOR] = _war_mutator("RCK/Systems/Social/War/FactionRespawn.cs", "[RCK+] Faction Respawn",
                                             RESPAWN_DESCRIPTION)
    mutators[NOINFIGHT_MUTATOR] = _war_mutator("RCK/Systems/Social/Relations/NoInfighting.cs", "[RCK+] No In-Fighting",
                                               NOINFIGHT_DESCRIPTION)
    mutators[EXPANSION_MUTATOR] = _war_mutator("RCK/Systems/Social/Turf/TurfExpansion.cs", "[RCK+] Turf Expansion",
                                               EXPANSION_DESCRIPTION)
    mutators[COMMAND_MUTATOR] = _war_mutator("RCK/Systems/Social/Commander/Command.cs", "[RCK+] Commander", COMMAND_DESCRIPTION)
    mutators[WAR_PANEL_MUTATOR] = _war_mutator("RCK/Systems/Social/Commander/WarPanel.cs", "[RCK+] War Panel",
                                               WAR_PANEL_DESCRIPTION)
    _merge_section(spec["mutators"], mutators, "mutators")
    spec["extensions"] = {
        "note": NOTE,
        "party_peace": PARTY_PEACE,
        "street_innocence": {
            "rules": INNOCENCE_RULES,
            "trait": INNOCENCE_TRAIT,
            "mutator": INNOCENCE_MUTATOR,
        },
        "disguises": {
            "rules": DISGUISE_RULES,
            "mutator": DISGUISE_MUTATOR,
            "prefix": DISGUISE_PREFIX,
            "defaults": DISGUISE_DEFAULTS,
            "format": DISGUISE_FORMAT,
        },
        "faction_names": {"prefix": FACTION_NAME_PREFIX, "format": FACTION_NAME_FORMAT},
        "quests": {
            "rules": QUEST_RULES,
            "prefix": QUEST_PREFIX,
            "format": QUEST_FORMAT,
            "keys": QUEST_KEYS,
            "types": QUEST_TYPES,
            "targets": QUEST_TARGETS,
            "rewards": QUEST_REWARDS,
            "placeholders": QUEST_PLACEHOLDERS,
            "max_text": 600,
            "gate_switch": {"name": "Quest", "rules": QUEST_GATE},
            "traits": {tid: rules for tid, (_, rules) in QUEST_TRAITS.items()},
            "target_labels": QUEST_LABELS,
            "radiant": {"mutator": QUEST_MUTATOR, "trait": QUEST_RADIANT_TRAIT, "rules": QUEST_RADIANT},
        },
        "goals": {"rules": GOAL_RULES, "goals": GOALS},
        "gates": {
            "rules": GATE_RULES,
            "prefixes": ["[CCU]LevelGate::", "[RCK]LevelGate::"],
            "keys": GATE_KEYS,
            "conditions": GATE_CONDITIONS,
            "examples": GATE_EXAMPLES,
        },
        "turf": {
            "rules": TURF_RULES,
            "mutator": TURF_MUTATOR,
            "gate_switch": {"name": "TurfTaken", "rules": TURF_GATE},
            "overlay": {"rules": TURF_OVERLAY_RULES, "config": TURF_OVERLAY_CONFIG, "colors": TURF_OVERLAY_COLORS,
                        "host_only": True},
        },
        "no_infighting": {
            "rules": NOINFIGHT_RULES,
            "mutator": NOINFIGHT_MUTATOR,
            "trait": NOINFIGHT_TRAIT,
        },
        "raids": {
            "rules": RAID_RULES,
            "format": RAID_FORMAT,
            "mutator": RAID_MUTATOR,
            "prefix": RAID_PREFIX,
            "squads": SQUAD_RULES,
            "max_squad_agents": SQUAD_LIMIT,
            "defaults": RAID_DEFAULTS,
            "examples": RAID_EXAMPLES,
        },
        "backup": {
            "rules": BACKUP_RULES,
            "role": BACKUP_ROLE,
            "limits": BACKUP_LIMITS,
        },
        "respawn": {
            "rules": RESPAWN_RULES,
            "format": RESPAWN_FORMAT,
            "mutator": RESPAWN_MUTATOR,
            "prefix": RESPAWN_PREFIX,
            "role": RESPAWN_ROLE,
            "units": RESPAWN_UNITS,
            "defaults": RESPAWN_DEFAULTS,
            "ranges": RESPAWN_RANGES,
            "limits": RESPAWN_LIMITS,
            "examples": RESPAWN_EXAMPLES,
        },
        "broker": {
            "rules": BROKER_RULES,
            "trait": BROKER_TRAIT,
            "buttons": ["Broker a truce", "Frame a faction", "Change first faction", "Change second faction"],
            "prices": BROKER_PRICES,
        },
        "expansion": {
            "rules": EXPANSION_RULES,
            "format": EXPANSION_FORMAT,
            "mutator": EXPANSION_MUTATOR,
            "prefix": EXPANSION_PREFIX,
            "defaults": EXPANSION_DEFAULTS,
            "ranges": EXPANSION_RANGES,
            "limits": EXPANSION_LIMITS,
            "rackets": RACKET_RULES,
            "examples": EXPANSION_EXAMPLES,
        },
        "control_points": {
            "rules": CP_RULES,
            "format": CP_FORMAT,
            "prefix": CP_PREFIX,
            "defaults": CP_DEFAULTS,
            "ranges": CP_RANGES,
            "power": CP_POWER,
            "examples": CP_EXAMPLES,
        },
        "command": {
            "rules": COMMAND_RULES,
            "format": COMMAND_FORMAT,
            "mutator": COMMAND_MUTATOR,
            "prefix": COMMAND_PREFIX,
            "orders": COMMAND_ORDERS,
            "defaults": COMMAND_DEFAULTS,
            "ranges": COMMAND_RANGES,
            "limits": COMMAND_LIMITS,
            "config": COMMAND_CONFIG,
            "host_only": True,
            "examples": COMMAND_EXAMPLES,
        },
        "war_panel": {
            "rules": WAR_PANEL_RULES,
            "mutator": WAR_PANEL_MUTATOR,
            "config": WAR_PANEL_CONFIG,
            "host_only": True,
        },
        "medic": {
            "rules": MEDIC_RULES,
            "trait": MEDIC_TRAIT,
            "limits": MEDIC_LIMITS,
        },
        "racketeer": {
            "rules": RACKETEER_RULES,
            "trait": RACKETEER_TRAIT,
            "host_only": True,
        },
        "factions": {
            "numbered": NUMBERED_FACTIONS,
            "keys": faction_keys(),
            "named": NAMED_FACTIONS,
            "grades": GRADES,
            "territorial": TERRITORIAL,
            "member": MEMBER,
            "roles": ROLES,
            "vengeful": VENGEFUL,
            "leader": LEADER,
            "defector": {"trait": DEFECTOR_TRAIT, "rules": DEFECTOR},
            "precedence": PRECEDENCE,
            "private_access": PRIVATE_ACCESS,
            "matrix": {"prefix": MATRIX_PREFIX, "legacy_prefixes": MATRIX_LEGACY_PREFIXES, "format": MATRIX_FORMAT},
            "recruit": {
                "rules": RECRUIT_RULES,
                "policies": RECRUIT_POLICIES,
                "traits": {tid: policy for tid, (_, policy) in RECRUIT_TRAITS.items()},
                "mutators": {name: m["policy"] for name, m in RECRUIT_MUTATORS.items()},
                "matrix": {"prefix": RECRUIT_PREFIX, "format": RECRUIT_FORMAT},
            },
        },
    }
    return spec


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", type=Path, default=DEFAULT_SPEC)
    a = ap.parse_args(argv)
    spec = json.loads(a.spec.read_text(encoding="utf-8"))
    merge(spec)
    a.spec.write_text(json.dumps(spec, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    ext = sum(1 for e in spec["traits"].values() if e.get("extension"))
    ext_mutators = sum(1 for e in spec["mutators"].values() if e.get("extension"))
    print(f"wrote {a.spec}: {ext} extension traits, {ext_mutators} extension mutators, "
          f"{len(spec['extensions']['factions']['keys'])} faction keys")


if __name__ == "__main__":
    main()
