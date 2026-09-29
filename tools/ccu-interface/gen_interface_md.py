"""Render docs/ccu-interface.md from the harvested CCU interface JSON.

The generated document records identifiers and storage formats only. It does not copy
CCU implementation logic or prose. By default it is the public version: no corpus
usage counts, commit hashes or harvest notes. Pass --internal (and optionally
--usage) to add those, and write that output under docs/internal.
"""
from __future__ import annotations

import argparse
import collections
import json
import re
from pathlib import Path, PurePosixPath

from harvest_ccu import balanced, const_table, read_tree, split_top, strip_comments, unquote

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SPEC = ROOT / "docs" / "ccu-interface.json"
DEFAULT_USAGE = ROOT / "docs" / "internal" / "ccu-corpus-usage.json"
DEFAULT_OUTPUT = ROOT / "docs" / "ccu-interface.md"
DEFAULT_UPSTREAM = ROOT / "upstream" / "CCU"
DEFAULT_ROGUELIBS = ROOT / "upstream" / "RogueLibs" / "RogueLibsCore"

EXTRA_FORMAT_COUNT = 3
VANILLA_EDITOR_GOALS = [
    "None", "Idle", "Guard", "Patrol", "Dance", "IceSkate", "Swim", "ListenToJokeNPC", "Joke", "Sit",
    "Sleep", "CuriousObject", "Wander", "WanderInOwnedProperty", "WanderFar",
]
LANGUAGES = ["Binary", "Chthonic", "English", "ErSdtAdt", "Foreign", "Goryllian", "Undercant", "Werewelsh"]
SWITCH_GATES = ["A", "B", "C", "D"]


def load_json(path: Path, default=None):
    if not path.exists():
        return default
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def read_cs_files(root: Path) -> dict[str, str]:
    if not root.exists():
        return {}
    files = {}
    for path in root.rglob("*.cs"):
        rel = path.relative_to(root).as_posix()
        files[rel] = path.read_text(encoding="utf-8-sig", errors="replace")
    return files


def md(s) -> str:
    if s is None:
        return ""
    text = str(s).replace("\r\n", " ").replace("\n", " ")
    return text.replace("|", "\\|")


def code(s) -> str:
    if s is None:
        return ""
    return "`" + str(s).replace("`", "\\`") + "`"


def yn(value: bool) -> str:
    return "yes" if value else "no"


def version_span(entry: dict) -> tuple[str, str]:
    versions = entry.get("versions") or []
    return (versions[0] if versions else "", versions[-1] if versions else "")


def usage_count(usage: dict | None, kind: str, name: str) -> str:
    if not usage:
        return ""
    row = (usage.get(kind) or {}).get(name)
    if not row:
        return ""
    return str(row.get("count", ""))


def simple_value(value) -> str:
    if isinstance(value, dict):
        if "expr" in value:
            return value["expr"]
        return json.dumps(value, ensure_ascii=False, sort_keys=True)
    if isinstance(value, list):
        return ", ".join(map(str, value))
    if value is None:
        return ""
    return str(value)


def trait_costs(entry: dict) -> str:
    if entry.get("kind") != "player":
        return ""
    unlock = entry.get("unlock") or {}
    cc = simple_value(unlock.get("CharacterCreationCost"))
    uc = simple_value(unlock.get("UnlockCost"))
    parts = []
    if cc != "":
        parts.append(f"CC {cc}")
    if uc != "":
        parts.append(f"unlock {uc}")
    return "; ".join(parts)


def trait_cancellations(entry: dict) -> str:
    unlock = entry.get("unlock") or {}
    values: list[str] = []
    for item in unlock.get("Cancellations") or []:
        values.append(simple_value(item))
    nested = unlock.get("Unlock") or {}
    for item in nested.get("cancellations") or []:
        values.append(simple_value(item))
    return ", ".join(dict.fromkeys(values))


def trait_group(entry: dict) -> str:
    path = entry.get("path") or ""
    parts = list(PurePosixPath(path).parts)
    if "Systems" in parts:
        i = parts.index("Systems") + 1
        group = parts[i:-1]
    elif len(parts) > 2:
        group = parts[1:-1]
    else:
        group = parts[:-1]
    return " / ".join(group) if group else "Other"


def reverse_trait_conversions(spec: dict) -> dict[str, list[str]]:
    reverse: dict[str, list[str]] = collections.defaultdict(list)
    for old, targets in spec.get("legacy", {}).get("TraitConversions", {}).items():
        for target in targets:
            reverse[target].append(old)
    return reverse


def table(headers: list[str], rows: list[list[str]]) -> list[str]:
    out = ["| " + " | ".join(headers) + " |", "| " + " | ".join("---" for _ in headers) + " |"]
    out.extend("| " + " | ".join(md(cell) for cell in row) + " |" for row in rows)
    return out


def resolve_expr(expr: str, consts: dict[str, str]) -> str | None:
    expr = expr.strip()
    literal = unquote(expr)
    if literal is not None:
        return literal
    if expr in consts:
        return consts[expr]
    if expr.startswith("NameTypes."):
        return expr.split(".", 1)[1]
    if expr.startswith("VanillaTraits.") and expr in consts:
        return consts[expr]
    if re.fullmatch(r"[A-Za-z_]\w*", expr):
        matches = [value for key, value in consts.items() if key.endswith("." + expr)]
        if len(set(matches)) == 1:
            return matches[0]
    if "." in expr:
        suffix = expr.split(".")[-1]
        matches = [value for key, value in consts.items() if key.endswith("." + suffix)]
        if len(set(matches)) == 1:
            return matches[0]
    return None


def resolve_type(expr: str, prefix: str, consts: dict[str, str]) -> str:
    expr = expr.strip()
    if expr == "t":
        assignments = list(re.finditer(r"(?:string\s+)?t\s*=\s*(NameTypes\.\w+|\"[^\"]+\")", prefix))
        if assignments:
            return resolve_type(assignments[-1].group(1), prefix[:assignments[-1].start()], consts)
    value = resolve_expr(expr, consts)
    return value if value is not None else expr


def expand_name_key(expr: str, path: str, consts: dict[str, str]) -> list[str]:
    expr = expr.strip()
    if expr == '$"Learn_{language}"':
        return [f"Learn_{language}" for language in LANGUAGES]
    if expr == "traitName" and path.endswith("T_Switch.cs"):
        return [f"Agent_Switch_{gate}" for gate in SWITCH_GATES]
    value = resolve_expr(expr, consts)
    return [value if value is not None else expr]


def collect_custom_names(upstream: Path, roguelibs: Path) -> list[dict[str, str]]:
    try:
        ccu_files = read_tree(upstream, "HEAD")
    except Exception:
        ccu_files = read_cs_files(upstream / "CCU")
    consts = {}
    consts.update(const_table(read_cs_files(roguelibs)))
    consts.update(const_table(ccu_files))

    rows = []
    for path, src in ccu_files.items():
        src = strip_comments(src)
        for match in re.finditer(r"RogueLibs\.CreateCustomName\s*\(", src):
            open_idx = src.find("(", match.start())
            close_idx = balanced(src, open_idx, "(", ")")
            args = split_top(src[open_idx + 1:close_idx])
            if len(args) < 2:
                continue
            keys = expand_name_key(args[0], path, consts)
            name_type = resolve_type(args[1], src[:match.start()], consts)
            for key in keys:
                rows.append({"key": key, "type": name_type, "path": path})
    unique = {(row["key"], row["type"]): row for row in rows}
    return sorted(unique.values(), key=lambda r: (r["type"], r["key"]))


def harvested(section: dict) -> dict:
    """Entries harvested from CCU, without the RCK additions merged in by ccux_extensions.py."""
    return {name: entry for name, entry in section.items() if not entry.get("extension")}


def collision_report(spec: dict, custom_names: list[dict[str, str]]) -> tuple[bool, list[str]]:
    strings: list[str] = []
    for section in ("traits", "mutators", "items", "effects", "goals"):
        strings.extend(harvested(spec.get(section, {})).keys())
    for section in ("traits", "items", "effects", "mutators"):
        for entry in harvested(spec.get(section, {})).values():
            display = entry.get("display_en")
            if display:
                strings.append(display)
    for mapping in spec.get("legacy", {}).values():
        strings.extend(mapping.keys())
        for value in mapping.values():
            if isinstance(value, list):
                strings.extend(value)
            else:
                strings.append(str(value))
    for object_list in spec.get("object_lists", {}).values():
        strings.extend(object_list.get("objects", []))
    strings.extend(row["key"] for row in custom_names)

    collisions = sorted({s for s in strings if s.startswith("RCK_") or s.startswith("rck-") or "[RCK" in s})
    return (not collisions, collisions)


def render(spec: dict, usage: dict | None, custom_names: list[dict[str, str]], internal: bool = False) -> str:
    extension_traits = {name: entry for name, entry in spec["traits"].items() if entry.get("extension")}
    extension_mutators = {name: entry for name, entry in spec["mutators"].items() if entry.get("extension")}
    traits = harvested(spec["traits"])
    mutators = harvested(spec["mutators"])
    head_traits = sum(1 for entry in traits.values() if entry.get("in_head"))
    trait_kinds = collections.Counter(entry.get("kind") for entry in traits.values())
    dynamic_mutators = sum(1 for key in mutators if key.startswith("<dynamic:"))
    legacy_counts = {key: len(value) for key, value in spec["legacy"].items()}
    legacy_total = sum(legacy_counts.values())
    ok, collisions = collision_report(spec, custom_names)

    lines: list[str] = []
    add = lines.append
    add("# CCU external interface contract")
    add("")
    add("Generated by `python tools\\ccu-interface\\gen_interface_md.py` from `docs\\ccu-interface.json`. It records the saved-data interface of CCU content, which RCK (Rogue Content Kit) reads for compatibility.")
    add("")
    add("## Summary")
    add("")
    for line in table(["Item", "Count"], [
        ["Trait IDs (union)", str(len(traits))],
        ["Trait IDs in HEAD", str(head_traits)],
        ["Designer traits", str(trait_kinds.get("designer", 0))],
        ["Player traits", str(trait_kinds.get("player", 0))],
        ["Goal strings", str(len(spec["goals"]))],
        ["Extra-var formats", str(EXTRA_FORMAT_COUNT)],
        ["Mutators", f"{len(mutators)} ({dynamic_mutators} dynamic entries)"],
        ["Items", str(len(spec["items"]))],
        ["Effects", str(len(spec["effects"]))],
        ["Legacy conversions", f"{legacy_total} ({', '.join(f'{k}={v}' for k, v in legacy_counts.items())})"],
        ["RCK extension traits (not counted above)", str(len(extension_traits))],
        ["RCK extension mutators (not counted above)", str(len(extension_mutators))],
    ]):
        add(line)
    add("")
    add("## Purpose and licensing note")
    add("")
    add("CCU's source repository has no licence file, and CCU's releases are licensed CC BY-NC-ND 4.0. This document therefore records only external interface facts needed for compatibility: saved-data identifiers, display-name formats, extra-var formats, mutator names, item/effect names, goal strings, legacy rename maps and registration keys. It contains no CCU code, behavioural logic, descriptions, translations or prose, and none may be copied into RCK.")
    add("")
    add("## Plugin identity")
    add("")
    refs = spec.get("source", {}).get("refs", {})
    add(f"- Plugin GUID: {code(spec['plugin']['guid'])}.")
    add("- Original plugin names: `CCU [D]` for the designer edition and `CCU [P]` for the player edition.")
    if internal:
        add(f"- Harvested refs: {', '.join(f'{ref} ({sha})' for ref, sha in refs.items())}. HEAD reports plugin version `{spec['plugin'].get('head_version')}a` in the version banner.")
    else:
        add(f"- CCU versions covered: {', '.join(ref for ref in refs if ref != 'HEAD')} and the latest source (plugin version `{spec['plugin'].get('head_version')}a`), shown as `HEAD` in the tables below.")
    add("- BepIn dependency: `[BepInDependency(RogueLibs.GUID, RogueLibs.CompiledVersion)]`, with RogueLibs GUID `abbysssal.streetsofrogue.roguelibscore`.")
    if internal:
        add("- BunnyLibs/SORCE dependency check: grep found no dependency on `Freiling87.streetsofrogue.CCU`; BunnyLibs only has a commented CCU logo reference and SORCE has no CCU GUID match.")
        add("- Config keys: none found (`Config.Bind` does not appear in the harvested CCU sources).")
    add("- RCK has its own GUID, `streetsofrogue.roguecontentkit`, and declares `[BepInIncompatibility]` on the GUID above, so the two never load together. RCK displays CCU's `[CCU]` tag as `[RCK]`; stored names are unchanged.")
    add("")
    add("## Storage locations in Streets of Rogue data")
    add("")
    add("- Trait IDs are C# class names stored in `customCharacterData*.traits` and in chunk `customCharacterList[].traits`.")
    add("- Goal strings are stored in agent spawner `defaultGoal`, on spawners with `spawnerType == \"Agent\"`.")
    add("- Extra-var formats use object spawner `extraVarString` through `extraVarString4`; CCU investigate text and container item data use `extraVarString`.")
    add("- Mutators are stored in campaign `mutatorList` and level `levelMutators`.")
    add("- Item names appear in custom-character item lists and object item slots such as container-like `extraVarString` values.")
    add("- Effect names are RogueLibs status-effect identifiers and may appear wherever a custom status-effect name is serialized or referenced by CCU data.")
    add("")
    add("## Naming formats")
    add("")
    add("- Designer trait display names: `[CCU] {Group} - {Name}`. The group is the last namespace segment with underscores replaced by spaces; the name is either an explicit custom name or the trait class name with underscores replaced by spaces.")
    add("- Player trait display names: class name with underscores replaced by spaces and `2` rendered as `+`.")
    add("- Mutator designer display names: `[CCU] {Group} - {Name}`, where the group is namespace segment 2 with underscores replaced by spaces.")
    add("- Legacy v0.1 trait `DisplayName` used `[CCU] {namespace segment 2 with underscores as spaces} - {custom or class name with underscores as spaces}`.")
    add("- Level Gate data mutators are semicolon-delimited key/value strings beginning with `[CCU]LevelGate::`, e.g. `[CCU]LevelGate::Type=Entry;Label=A,B;Switches=Agent,Object;Logic=AND;`. The third key is written `Switch` or `Switches`; RCK reads both.")
    add("- Configured mutator menu display names are numbered as `[CCU] Level Gate Mutator (<color=yellow>NNN</color>)`; the configurator unlock display uses CCU's designer-mutator naming with `Level Gate(<color=lime>Configurator</color>)`.")
    add("- Campaign-branching switch traits generated at runtime use `Agent_Switch_A` through `Agent_Switch_D` as status-effect/description name keys.")
    add("")
    add("## Trait IDs")
    add("")
    reverse_legacy = reverse_trait_conversions(spec)
    grouped: dict[str, list[tuple[str, dict]]] = collections.defaultdict(list)
    for name, entry in sorted(traits.items()):
        grouped[trait_group(entry)].append((name, entry))
    for group in sorted(grouped):
        add(f"### {group}")
        add("")
        rows = []
        for name, entry in grouped[group]:
            first, last = version_span(entry)
            notes = []
            if not entry.get("in_head"):
                notes.append("legacy-only")
            if reverse_legacy.get(name):
                notes.append("legacy target for " + ", ".join(reverse_legacy[name]))
            rows.append([
                code(name), entry.get("display_en") or "", entry.get("kind") or "", first, last,
                yn(bool(entry.get("in_head"))), trait_costs(entry), trait_cancellations(entry),
                *([usage_count(usage, "traits", name)] if internal else []), "; ".join(notes),
            ])
        for line in table(["ID", "Display name", "Kind", "First", "Last", "HEAD", "Costs", "Cancellations"] + (["Corpus uses"] if internal else []) + ["Notes"], rows):
            add(line)
        add("")
    add("## Goal strings")
    add("")
    add("Vanilla editor goals: " + ", ".join(code(g) for g in VANILLA_EDITOR_GOALS) + ".")
    add("")
    add("The stored string is `Random Teleport (Duo)`; CCU's documentation calls it `Teleport (Duo)`.")
    add("")
    goal_rows = []
    for name, entry in sorted(spec["goals"].items()):
        first, last = version_span(entry)
        goal_rows.append([code(name), ", ".join(entry.get("lists") or []), first, last, yn(bool(entry.get("in_head")))] + ([usage_count(usage, "goals", name)] if internal else []))
    for line in table(["String", "List membership", "First", "Last", "HEAD"] + (["Corpus uses"] if internal else []), goal_rows):
        add(line)
    add("")
    add("## Extra-var formats")
    add("")
    object_lists = spec.get("object_lists", {})
    investigate = sorted(set(object_lists.get("InvestigateableObjects_Slot1", {}).get("objects", [])) | set(object_lists.get("InvestigateableObjects_Slot2", {}).get("objects", [])))
    containers = sorted(object_lists.get("ContainerObjects_Slot1", {}).get("objects", []))
    add(f"- Investigate text: `investigateable-message:::<text>` in object spawner `extraVarString`. CCU investigate objects: {', '.join(code(o) for o in investigate)}.")
    add(f"- Container item: item name in object spawner `extraVarString`. CCU container objects: {', '.join(code(o) for o in containers)}.")
    add("- Level Gate mutator data: `[CCU]LevelGate::` followed by semicolon-delimited key/value elements and a trailing semicolon; see Naming formats for the compatibility-sensitive key names.")
    if internal and usage and usage.get("extra"):
        add("- Corpus usage snapshot: " + ", ".join(f"{code(k)}={v['count']}" for k, v in sorted(usage["extra"].items())[:40]) + (" ..." if len(usage["extra"]) > 40 else ""))
    add("")
    add("## Mutators")
    add("")
    rows = []
    for name, entry in sorted(mutators.items()):
        first, last = version_span(entry)
        rows.append([code(name), entry.get("display_en") or "", entry.get("class") or "", first, last, yn(bool(entry.get("in_head")))] + ([usage_count(usage, "mutators", name)] if internal else []))
    for line in table(["ID", "Display name", "Class/source", "First", "Last", "HEAD"] + (["Corpus uses"] if internal else []), rows):
        add(line)
    add("")
    add("## Items")
    add("")
    rows = []
    for name, entry in sorted(spec["items"].items()):
        first, last = version_span(entry)
        rows.append([code(name), entry.get("display_en") or "", first, last, yn(bool(entry.get("in_head")))] + ([usage_count(usage, "items", name)] if internal else []))
    for line in table(["ID", "Display name", "First", "Last", "HEAD"] + (["Corpus uses"] if internal else []), rows):
        add(line)
    add("")
    add("## Effects")
    add("")
    rows = []
    for name, entry in sorted(spec["effects"].items()):
        first, last = version_span(entry)
        rows.append([code(name), entry.get("display_en") or "", first, last, yn(bool(entry.get("in_head")))])
    for line in table(["ID", "Display name", "First", "Last", "HEAD"], rows):
        add(line)
    add("")
    add("## Legacy conversions")
    add("")
    for title, mapping in spec.get("legacy", {}).items():
        add(f"### {title}")
        add("")
        rows = []
        for old, new in sorted(mapping.items()):
            if isinstance(new, list):
                target = ", ".join(new)
            else:
                target = str(new)
            rows.append([code(old), target])
        for line in table(["Legacy name", "Replacement target(s)"], rows):
            add(line)
        add("")
    add("## CreateCustomName keys")
    add("")
    add("These keys are registered text/name entries. They are not stored as campaign identifiers, but they are part of the user-visible text interface.")
    add("")
    rows = [[code(row["key"]), row["type"]] for row in custom_names]
    for line in table(["Name key", "Name type"], rows):
        add(line)
    add("")
    render_extensions(spec, extension_traits, extension_mutators, add)
    add("## Reserved namespace for our additions")
    add("")
    add("Use `RCK_` for new trait/mutator/item/effect IDs, `[RCK+]` as the display tag, `[RCK]` for new data-mutator prefixes, and `rck-` as the start of new extra-var prefixes. The one exception is the faction family in the RCK extensions section above: added faction traits keep CCU's `Faction_<N>_<Grade>` and `<Group>_<Grade>` ID patterns (with the `[RCK+]` display tag), and `ccux_extensions.py` refuses any ID that CCU already uses.")
    if not ok:
        add("Collision check: collisions found: " + ", ".join(code(c) for c in collisions))
    elif internal:
        add("Collision check: no harvested CCU spec identifier, legacy alias, object-list entry, display tag, or CreateCustomName key uses `RCK_`, `[RCK`, or a `rck-` prefix.")
    add("")
    return "\n".join(lines) + "\n"


def render_extensions(spec: dict, extension_traits: dict, extension_mutators: dict, add) -> None:
    ext = spec.get("extensions") or {}
    factions = ext.get("factions") or {}
    if not extension_traits and not extension_mutators and not factions:
        return
    add("## RCK extensions")
    add("")
    add(md(ext.get("note") or "Additions made by RCK, not part of CCU."))
    add("")
    if factions:
        all_traits = spec["traits"]
        grades = factions.get("grades") or {}
        named = factions.get("named") or {}
        add("### Factions")
        add("")
        add(f"Numbered factions `Faction_1` to `Faction_{factions.get('numbered')}` and {len(named)} named factions. Each faction key has one designer trait per grade, `<key>_<Grade>`. The holder of `<key>_Aligned` is a member; a named faction also counts the matching vanilla agents as members. The other grades set the holder and every member to that relationship, both ways; `Territorial` then escalates on turf.")
        add("")
        for line in table(["Grade suffix", "Relationship"], [[code(g), md(r)] for g, r in grades.items()]):
            add(line)
        add("")
        if factions.get("territorial"):
            add(md(factions["territorial"]))
            add("")
        member = factions.get("member")
        if member:
            add(f"`<key>_{member}` is a player trait, available in the character creator: it only makes the holder a member, so playable characters can start inside a faction. It can't be lost or swapped.")
            add("")
        add("When several faction rules match one pair, the first relationship in this list wins: " + ", ".join(code(r) for r in factions.get("precedence") or []) + ".")
        add("")
        for line in table(["Named faction", "Members"], [[code(k), md(v)] for k, v in named.items()]):
            add(line)
        add("")
        add("Which IDs CCU already has (`CCU`) and which RCK adds (`added`):")
        add("")
        rows = []
        for key in factions.get("keys") or []:
            row = [code(key)]
            for grade in list(grades) + ([member] if member else []):
                entry = all_traits.get(f"{key}_{grade}")
                row.append("-" if entry is None else ("added" if entry.get("extension") else "CCU"))
            rows.append(row)
        for line in table(["Key"] + list(grades) + ([member] if member else []), rows):
            add(line)
        add("")
        matrix = factions.get("matrix") or {}
        if matrix:
            add("### Faction relationship matrix")
            add("")
            add(f"Campaign or level mutator {code(matrix.get('prefix'))}. " + (matrix.get("format") or ""))
            add(f"Example: `{matrix.get('prefix')}1>2=Hateful;3<>Blahd=Annoyed;7>*=Friendly;`. Entries naming both sides beat `*` entries, and the strongest relationship wins within each. The matrix beats faction traits but not an agent's own Player, Trait Gate or General relationship traits. CCU has no such mutator; the prefix is an RCK addition.")
            legacy = matrix.get("legacy_prefixes") or []
            if legacy:
                add("Older spellings, still read so existing campaigns keep working: " + ", ".join(code(x) for x in legacy) + ". Write the prefix above in new content.")
            add("")
        recruit = factions.get("recruit") or {}
        if recruit:
            add("### Faction recruiting")
            add("")
            add(md(recruit.get("rules") or ""))
            add("")
            rows = [[code(n), "NPC trait", md(p)] for n, p in (recruit.get("traits") or {}).items()]
            rows += [[code(n), "mutator", md(p)] for n, p in (recruit.get("mutators") or {}).items()]
            for line in table(["ID", "Kind", "Policy"], rows):
                add(line)
            add("")
            rmatrix = recruit.get("matrix") or {}
            if rmatrix:
                add(f"Campaign or level mutator {code(rmatrix.get('prefix'))}. " + (rmatrix.get("format") or ""))
                add(f"Example: `{rmatrix.get('prefix')}Cop=Free;Crepe,Blahd=Paid;*=Off;`. CCU has no such mutator; the prefix is an RCK addition, with no older spelling.")
                add("")
    innocence = ext.get("street_innocence") or {}
    if innocence:
        add("### Street innocence")
        add("")
        add(md(innocence.get("rules") or ""))
        add("")
        for line in table(["ID", "Kind"], [[code(innocence.get("trait")), "NPC trait"], [code(innocence.get("mutator")), "mutator"]]):
            add(line)
        add("")
    others = sorted(name for name, entry in extension_traits.items()
                    if not entry.get("faction") and not entry.get("recruit") and not entry.get("street_innocence"))
    if others:
        add("### Other extension traits")
        add("")
        for line in table(["ID", "Display name", "Kind"], [[code(n), md(extension_traits[n].get("display_en") or ""), extension_traits[n].get("kind") or ""] for n in others]):
            add(line)
        add("")
    if extension_mutators:
        add("### Extension mutators")
        add("")
        add("Mutators RCK adds. CCU content never uses these IDs.")
        add("")
        for line in table(["ID", "Display name", "Description"], [[code(n), md(e.get("display_en") or ""), md(e.get("description_en") or "")] for n, e in sorted(extension_mutators.items())]):
            add(line)
        add("")


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", type=Path, default=DEFAULT_SPEC)
    ap.add_argument("--internal", action="store_true", help="add corpus usage, commit hashes and harvest notes (for docs/internal only)")
    ap.add_argument("--usage", type=Path, default=DEFAULT_USAGE, help="corpus usage JSON, read only with --internal")
    ap.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    ap.add_argument("--upstream", type=Path, default=DEFAULT_UPSTREAM)
    ap.add_argument("--roguelibs", type=Path, default=DEFAULT_ROGUELIBS)
    args = ap.parse_args(argv)

    spec = load_json(args.spec)
    usage = load_json(args.usage, default=None) if args.internal else None
    if args.internal and "internal" not in args.output.parts:
        ap.error("--internal output must go under docs/internal")
    custom_names = collect_custom_names(args.upstream, args.roguelibs)
    text = render(spec, usage, custom_names, args.internal)
    args.output.write_text(text, encoding="utf-8", newline="\r\n")
    print(f"wrote {args.output} ({len(text.splitlines())} lines, {len(custom_names)} CreateCustomName keys)")


if __name__ == "__main__":
    main()
