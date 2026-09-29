"""RCK's own additions to the CCU trait interface.

The faction additions extend CCU's own faction patterns, `Faction_<N>_<Grade>` and `<Group>_<Grade>`, so they keep
CCU-style IDs instead of the `RCK_` prefix. The other additions (recruiting, street innocence) use the `RCK_` prefix. `merge()` adds
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
                   "its default goal is WanderFar or a Random Teleport, or it owns nothing and started on unowned floor. "
                   "It is caught when another NPC sees it strike first: an attack on someone who didn't hit it first "
                   "and isn't fighting it. Property crimes don't catch it. Then its held-back rules apply for "
                   "the rest of the level, only ever making a relationship worse. `Drug_Dealer` holders and vanilla "
                   "Drug Dealers stay Guilty.")

NOTE = ("RCK additions, not part of CCU. Faction traits extend CCU's `Faction_<N>_<Grade>` and `<Group>_<Grade>` "
        "patterns; every other addition uses the `RCK_` prefix.")


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
        "path": "RCK/Systems/Social/FactionRecruit.cs",
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
        "path": "RCK/Systems/Social/StreetInnocence.cs",
        "bases": [],
        "display_en": INNOCENCE_MUTATOR_DISPLAY,
        "description_en": INNOCENCE_MUTATOR_DESCRIPTION,
        "in_head": True,
        "extension": True,
    }


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
    for tid in RECRUIT_TRAITS:
        out[tid] = _recruit_trait(tid)
    out[INNOCENCE_TRAIT] = _innocence_trait()
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
    _merge_section(spec["mutators"], mutators, "mutators")
    spec["extensions"] = {
        "note": NOTE,
        "street_innocence": {
            "rules": INNOCENCE_RULES,
            "trait": INNOCENCE_TRAIT,
            "mutator": INNOCENCE_MUTATOR,
        },
        "factions": {
            "numbered": NUMBERED_FACTIONS,
            "keys": faction_keys(),
            "named": NAMED_FACTIONS,
            "grades": GRADES,
            "territorial": TERRITORIAL,
            "member": MEMBER,
            "precedence": PRECEDENCE,
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
