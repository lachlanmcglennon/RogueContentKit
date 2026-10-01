#!/usr/bin/env python3
"""Role-aware string-key checker for RCK's Streets of Rogue API calls."""

from __future__ import annotations

import argparse
import csv
import difflib
import json
import os
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


# A C# decompile of the game's Assembly-CSharp.dll (e.g. from ILSpy): $SOR_DECOMP, else .ref\\decomp in this repository.
DEFAULT_DECOMP = Path(os.environ.get("SOR_DECOMP") or Path(__file__).resolve().parents[2] / ".ref" / "decomp")
def _find_sorcampaigns_data() -> Path:
    """The sorcampaigns toolkit's data folder: $SORCAMPAIGNS_DATA, else a sorcampaigns checkout beside this repository
    or beside any folder above it (so builds in ..\\sor-ccu-frozen\\<hash> find it too)."""
    env = os.environ.get("SORCAMPAIGNS_DATA")
    if env:
        return Path(env)
    repo = Path(__file__).resolve().parents[2]
    for folder in repo.parents:
        if (folder / "sorcampaigns" / "data").is_dir():
            return folder / "sorcampaigns" / "data"
    return repo.parent / "sorcampaigns" / "data"


DEFAULT_DATA = _find_sorcampaigns_data()


ROLE_LABELS = {
    "agent_name": "vanilla agent name",
    "agent_name_fragment": "substring of at least one vanilla agent name",
    "audio_clip": "AudioHandler clip key",
    "button_name": "interaction button/interface key",
    "button_pressed": "AgentInteractions.PressedButton key",
    "ccu_trait": "RCK trait id (docs/ccu-interface.json)",
    "dialogue_key": "dialogue key",
    "explosion_type": "SpawnerMain/Explosion explosion type",
    "goal_name": "agent goal string",
    "item_category": "InvItem category",
    "item_name": "item name",
    "item_type": "InvItem.itemType",
    "money_cost": "PlayfieldObject.determineMoneyCost transaction type",
    "mutator": "challenge/mutator name",
    "name_type": "NameDB type",
    "object_name": "object name",
    "relationship": "relationship type",
    "status_effect": "status effect name",
    "trait_name": "vanilla trait name",
}


@dataclass(frozen=True)
class Use:
    role: str
    value: str
    file: str
    line: int
    expr: str


@dataclass(frozen=True)
class Unknown:
    use: Use
    suggestions: tuple[str, ...]


class Vocab:
    def __init__(self) -> None:
        self.values: dict[str, set[str]] = defaultdict(set)
        self.evidence: dict[tuple[str, str], str] = {}

    def add(self, role: str, value: str, evidence: str) -> None:
        if value is None:
            return
        value = value.strip()
        if not value:
            return
        self.values[role].add(value)
        self.evidence.setdefault((role, value), evidence)

    def add_many(self, role: str, values: Iterable[str], evidence: str) -> None:
        for value in values:
            self.add(role, value, evidence)

    def has(self, role: str, value: str) -> bool:
        if role == "agent_name_fragment":
            return any(value in agent_name for agent_name in self.values.get("agent_name", set()))
        return value in self.values.get(role, set())


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def rel(path: Path, root: Path) -> str:
    try:
        return str(path.relative_to(root)).replace("/", "\\")
    except ValueError:
        return str(path).replace("/", "\\")


def line_no(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def unescape_csharp_string(raw: str) -> str:
    try:
        return bytes(raw, "utf-8").decode("unicode_escape")
    except Exception:
        return raw


def strip_comments(line: str) -> str:
    return re.sub(r"//.*$", "", line)


def find_line(path: Path, needle: str) -> str:
    try:
        for idx, line in enumerate(read_text(path).splitlines(), 1):
            if needle in line:
                return f"{path.name}:{idx}"
    except FileNotFoundError:
        pass
    return path.name


def parse_enum_row_ids(path: Path) -> list[tuple[str, int]]:
    text = read_text(path)
    match = re.search(r"public\s+enum\s+rowIds\s*\{(?P<body>.*?)\n\s*\}", text, re.S)
    if not match:
        return []
    body = match.group("body")
    out: list[tuple[str, int]] = []
    for m in re.finditer(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*,", body):
        value = m.group(1)
        out.append((value, line_no(text, match.start("body") + m.start(1))))
    return out


def add_name_db(vocab: Vocab, decomp: Path, name: str, roles: Iterable[str]) -> None:
    path = decomp / "Google2u" / f"{name}NameDB.cs"
    if not path.exists():
        return
    for value, line in parse_enum_row_ids(path):
        evidence = f"{path.name}:{line}"
        for role in roles:
            vocab.add(role, value, evidence)
        if name == "Dialogue" and value.startswith("NA_"):
            vocab.add("dialogue_key", value[3:], evidence)


def harvest_cases(vocab: Vocab, path: Path, role: str, method_name: str | None = None, signature: str | None = None) -> None:
    """Add every `case "X":` label in a file, or in one method. `signature` is a regex picking one overload."""
    if not path.exists():
        return
    text = read_text(path)
    if method_name or signature:
        pattern = signature if signature else rf"\b{re.escape(method_name or '')}\b"
        method = re.search(pattern + r"[^\{]*\{", text)
        if not method:
            return
        start = method.end()
        depth = 1
        pos = start
        while pos < len(text) and depth:
            if text[pos] == "{":
                depth += 1
            elif text[pos] == "}":
                depth -= 1
            pos += 1
        scope = text[start:pos]
        base = start
    else:
        scope = text
        base = 0
    for m in re.finditer(r'case\s+"([^"]+)"\s*:', scope):
        value = unescape_csharp_string(m.group(1))
        vocab.add(role, value, f"{path.name}:{line_no(text, base + m.start(1))}")


def harvest_regex_strings(vocab: Vocab, path: Path, role: str, pattern: str) -> None:
    if not path.exists():
        return
    text = read_text(path)
    for m in re.finditer(pattern, text):
        value = unescape_csharp_string(m.group(1))
        vocab.add(role, value, f"{path.name}:{line_no(text, m.start(1))}")


def harvest_decomp_literals(vocab: Vocab, decomp: Path, role: str, pattern: str) -> None:
    """Add the first string argument of every matching vanilla call anywhere in the decompile."""
    regex = re.compile(pattern)
    for path in decomp.rglob("*.cs"):
        text = read_text(path)
        for m in regex.finditer(text):
            vocab.add(role, unescape_csharp_string(m.group(1)), f"{path.name}:{line_no(text, m.start(1))}")


def load_status_and_trait_vocab(vocab: Vocab, decomp: Path) -> None:
    """Status effects and traits share StatusEffectNameDB, so harvest each kind from where vanilla defines or uses it.

    A name that is only ever a trait (e.g. UpperCrusty) must not pass as a status effect, and vice versa.
    """
    effects = decomp / "StatusEffects.cs"
    for signature in (
        r"public int GetStatusEffectTime\(string statusEffectName\)",
        r"public int GetStatusEffectHate\(string statusEffectName\)",
        r"public bool isPositiveStatusEffect\(string statusEffectName\)",
        r"public bool IsAntidoteEffect\(string statusEffectName\)",
        r"public void AddStatusEffect\(string statusEffectName, bool showText, Agent causingAgent, uint cameFromClient, bool dontPrevent, int specificTime\)",
    ):
        harvest_cases(vocab, effects, "status_effect", signature=signature)
    harvest_decomp_literals(vocab, decomp, "status_effect", r'\bAddStatusEffect\(\s*"([^"]+)"')
    # Wearables list trait names in InvItem.contents; ItemFunctions.EquipArmor applies each one with AddStatusEffect.
    harvest_regex_strings(vocab, decomp / "InvItem.cs", "status_effect", r'contents\.Add\("([^"]+)"\)')

    harvest_regex_strings(vocab, decomp / "Unlocks.cs", "trait_name", r'new Unlock\("([^"]+)",\s*"Trait"')
    harvest_cases(vocab, effects, "trait_name", signature=r"public void AddTrait\(string traitName, bool isStarting, bool justRefresh\)")
    harvest_cases(vocab, effects, "trait_name", signature=r"public void RemoveTrait\(string traitName, bool onlyLocal\)")
    harvest_decomp_literals(vocab, decomp, "trait_name", r'\b(?:AddTrait|hasTrait)\(\s*"([^"]+)"')


def load_ccu_spec(vocab: Vocab, repo: Path) -> set[str]:
    spec_path = repo / "docs" / "ccu-interface.json"
    ccu_traits: set[str] = set()
    if not spec_path.exists():
        return ccu_traits
    data = json.loads(read_text(spec_path))
    traits = data.get("traits") if isinstance(data, dict) else None
    if isinstance(traits, dict):
        trait_ids = traits.keys()
    elif isinstance(data, dict):
        trait_ids = [key for key, value in data.items() if isinstance(value, dict)]
    else:
        trait_ids = []
    for trait_id in trait_ids:
        ccu_traits.add(str(trait_id))
        vocab.add("ccu_trait", str(trait_id), f"{rel(spec_path, repo)}")
        vocab.add("trait_name", str(trait_id), f"{rel(spec_path, repo)}")
    return ccu_traits


def load_vanilla_vocab(vocab: Vocab, decomp: Path, data_dir: Path) -> None:
    add_name_db(vocab, decomp, "Agent", ["agent_name"])
    add_name_db(vocab, decomp, "Item", ["item_name"])
    add_name_db(vocab, decomp, "Object", ["object_name"])
    # StatusEffectNameDB mixes traits and effects; load_status_and_trait_vocab separates them.
    load_status_and_trait_vocab(vocab, decomp)
    add_name_db(vocab, decomp, "Interface", ["button_name"])
    add_name_db(vocab, decomp, "Dialogue", ["dialogue_key"])
    add_name_db(vocab, decomp, "Unlock", ["mutator"])

    for value in ["Agent", "Item", "Object", "StatusEffect", "Interface", "Dialogue", "Description", "Unlock"]:
        vocab.add("name_type", value, "NameDB.cs:GetName")

    agent_interactions = decomp / "AgentInteractions.cs"
    harvest_regex_strings(vocab, agent_interactions, "button_name", r'AddButton\("([^"]+)"')
    harvest_cases(vocab, agent_interactions, "button_pressed", "PressedButton")
    harvest_regex_strings(vocab, agent_interactions, "money_cost", r'determineMoneyCost\("([^"]+)"\)')
    harvest_regex_strings(vocab, agent_interactions, "money_cost", r'determineMoneyCost\([^;\n]*,\s*"([^"]+)"\)')

    playfield = decomp / "PlayfieldObject.cs"
    harvest_cases(vocab, playfield, "money_cost", "determineMoneyCost")

    audio = decomp / "AudioHandler.cs"
    harvest_regex_strings(vocab, audio, "audio_clip", r'audioClipDic\.Add\("([^"]+)"')
    harvest_regex_strings(vocab, audio, "audio_clip", r'LoadFile\("([^"]+)"')
    # The 4-argument Play overload holds the switch of clip aliases ("RevolverFire" picks RevolverFire1-3).
    harvest_cases(vocab, audio, "audio_clip", signature=r"public void Play\(PlayfieldObject playfieldObject, string clipName, uint cameFromClient, bool dontPlayOnClients\)")

    explosion = decomp / "Explosion.cs"
    harvest_cases(vocab, explosion, "explosion_type", "SetupExplosion")
    spawner = decomp / "SpawnerMain.cs"
    harvest_cases(vocab, spawner, "explosion_type", "SpawnExplosion")
    vocab.add("explosion_type", "Hack", find_line(spawner, '"Hack"'))

    inv_item = decomp / "InvItem.cs"
    harvest_regex_strings(vocab, inv_item, "item_type", r'itemType\s*=\s*"([^"]+)"')
    harvest_regex_strings(vocab, inv_item, "item_category", r'Categories\.Add\("([^"]+)"')

    relationships = decomp / "Relationships.cs"
    harvest_regex_strings(vocab, relationships, "relationship", r'relType\s*=\s*"([^"]+)"')
    harvest_regex_strings(vocab, relationships, "relationship", r'SetRel(?:Initial)?\([^;\n]*,\s*"([^"]+)"')

    brain = decomp / "BrainUpdate.cs"
    harvest_regex_strings(vocab, brain, "goal_name", r'defaultGoal\s*==\s*"([^"]+)"')
    harvest_regex_strings(vocab, brain, "goal_name", r'SetDefaultGoal\("([^"]+)"')
    harvest_cases(vocab, brain, "goal_name")
    agent = decomp / "Agent.cs"
    harvest_regex_strings(vocab, agent, "goal_name", r'SetDefaultGoal\("([^"]+)"')
    harvest_regex_strings(vocab, agent, "goal_name", r'defaultGoal\s*=\s*"([^"]+)"')
    harvest_regex_strings(vocab, agent, "agent_name", r'agentName\s*==\s*"([^"]+)"')
    for goal_path in decomp.glob("Goal*.cs"):
        harvest_regex_strings(vocab, goal_path, "goal_name", r'goalName\s*=\s*"([^"]+)"')

    vocab_path = data_dir / "vocab.json"
    if vocab_path.exists():
        data = json.loads(read_text(vocab_path))
        role_map = {
            "Agent.goal": "goal_name",
            "Agent.items": "item_name",
            "Agent.name": "agent_name",
            "Item.name": "item_name",
            "Object.name": "object_name",
            "Floor.name": "object_name",
            "Wall.name": "object_name",
            "char.items": "item_name",
            "char.traits.npc": "trait_name",
            "char.traits.player": "trait_name",
            "campaign.mutators": "mutator",
            "level.mutators": "mutator",
        }
        for source_role, role in role_map.items():
            values = data.get(source_role)
            if isinstance(values, dict):
                vocab.add_many(role, values.keys(), f"{rel(vocab_path, data_dir.parent)}:{source_role}")

    items_path = data_dir / "items.txt"
    if items_path.exists():
        for line in read_text(items_path).splitlines():
            value = line.strip()
            if value and not value.startswith("#"):
                vocab.add("item_name", value.split()[0], f"{rel(items_path, data_dir.parent)}")


def harvest_ccu_customs(vocab: Vocab, repo: Path) -> None:
    for path in (repo / "RCK").rglob("*.cs"):
        text = read_text(path)
        r = rel(path, repo)
        consts: dict[str, str] = {}
        raw_consts = [(m.group(1), m.group(2).strip()) for m in re.finditer(r'(?:private|public|internal)?\s*const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?);', text)]
        for _ in range(8):
            changed = False
            for name, expr in raw_consts:
                if name in consts:
                    continue
                pieces: list[str] = []
                ok = True
                for token in [part.strip() for part in expr.split("+")]:
                    sm = re.fullmatch(r'"((?:\\.|[^"\\])*)"', token)
                    if sm:
                        pieces.append(unescape_csharp_string(sm.group(1)))
                    elif re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', token) and token in consts:
                        pieces.append(consts[token])
                    else:
                        ok = False
                        break
                if ok and pieces:
                    consts[name] = "".join(pieces)
                    changed = True
            if not changed:
                break
        for m in re.finditer(r'\[ButtonLabel\("[^"]+"\)\]\s*public\s+const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=', text):
            name = m.group(1)
            if name in consts:
                vocab.add("button_name", consts[name], f"{r}:{line_no(text, m.start(1))}")
        for m in re.finditer(r'(?:CreateCustomName|SafeName)\(\s*"([^"]+)"\s*,\s*(?:NameTypes\.|")?([A-Za-z]+)', text):
            key, typ = m.group(1), m.group(2).strip('"')
            if typ == "Interface":
                vocab.add("button_name", key, f"{r}:{line_no(text, m.start(1))}")
            elif typ == "Item":
                vocab.add("item_name", key, f"{r}:{line_no(text, m.start(1))}")
            elif typ == "StatusEffect":
                vocab.add("status_effect", key, f"{r}:{line_no(text, m.start(1))}")
            elif typ == "Dialogue" and key.startswith("NA_"):
                # SayDialogue(key) falls back to the NA_<key> Dialogue name.
                vocab.add("dialogue_key", key[3:], f"{r}:{line_no(text, m.start(1))}")
        for m in re.finditer(r'CreateCustomItem<([A-Za-z_][A-Za-z0-9_]*)>', text):
            vocab.add("item_name", m.group(1), f"{r}:{line_no(text, m.start(1))}")
        for m in re.finditer(r'CreateCustomEffect<([A-Za-z_][A-Za-z0-9_]*)>', text):
            vocab.add("status_effect", m.group(1), f"{r}:{line_no(text, m.start(1))}")


def load_allowlist(path: Path) -> dict[tuple[str, str], str]:
    allow: dict[tuple[str, str], str] = {}
    if not path.exists():
        return allow
    with path.open("r", encoding="utf-8-sig", newline="") as fh:
        reader = csv.reader(fh, delimiter="\t")
        for row in reader:
            if not row or row[0].startswith("#"):
                continue
            if len(row) < 3:
                raise SystemExit(f"Bad allowlist row in {path}: {row!r}")
            role, value, reason = row[0].strip(), row[1].strip(), row[2].strip()
            if not reason:
                raise SystemExit(f"Allowlist entry {role}:{value} needs a reason")
            allow[(role, value)] = reason
    return allow


def csharp_strings(segment: str) -> list[str]:
    return [unescape_csharp_string(m.group(1)) for m in re.finditer(r'"((?:\\.|[^"\\])*)"', segment)]


def literal_after(pattern: str, line: str) -> str | None:
    match = re.search(pattern, line)
    return unescape_csharp_string(match.group(1)) if match else None


def extract_array_values(text: str, name: str) -> list[tuple[str, int]]:
    match = re.search(rf'\b{name}\b\s*=\s*(?:new\s+[\w<>]+(?:\[\])?\s*(?:\([^)]*\))?\s*)?\{{(?P<body>.*?)\}};', text, re.S)
    if not match:
        return []
    body = match.group("body")
    out: list[tuple[str, int]] = []
    for m in re.finditer(r'"((?:\\.|[^"\\])*)"', body):
        out.append((unescape_csharp_string(m.group(1)), line_no(text, match.start("body") + m.start(1))))
    return out


def extract_dict_pairs(text: str, name: str) -> list[tuple[str, str, int]]:
    """Return (key, value, line) for a `Name = new Dictionary<string, string> { ["k"] = "v", ... };` initializer."""
    match = re.search(rf'\b{name}\b\s*=\s*new[^{{]+\{{(?P<body>.*?)\n\s*\}};', text, re.S)
    if not match:
        return []
    out: list[tuple[str, str, int]] = []
    for entry in re.finditer(r'\[\s*"([^"]+)"\s*\]\s*=\s*"([^"]+)"', match.group("body")):
        out.append((unescape_csharp_string(entry.group(1)), unescape_csharp_string(entry.group(2)),
                    line_no(text, match.start("body") + entry.start(2))))
    return out


def extract_ccu_uses(repo: Path) -> list[Use]:
    uses: list[Use] = []
    ccu_root = repo / "RCK"

    def add(path: Path, text: str, role: str, value: str, line: int, expr: str) -> None:
        if value:
            uses.append(Use(role, value, rel(path, repo), line, expr.strip()))

    for path in sorted(ccu_root.rglob("*.cs")):
        if "\\Generated\\" in str(path) or "\\_Template\\" in str(path):
            continue
        text = read_text(path)
        lines = text.splitlines()

        vanilla_class = re.search(r'class\s+VanillaButtons\b(?P<body>.*?)\n\s*\}', text, re.S)
        if vanilla_class:
            for m in re.finditer(r'const\s+string\s+\w+\s*=\s*"([^"]+)"', vanilla_class.group("body")):
                value = unescape_csharp_string(m.group(1))
                line = line_no(text, vanilla_class.start("body") + m.start(1))
                add(path, text, "button_name", value, line, "VanillaButtons const")
                add(path, text, "button_pressed", value, line, "VanillaButtons const")

        for idx, original in enumerate(lines, 1):
            line = strip_comments(original)
            if not line.strip():
                continue

            for m in re.finditer(r'AddPressed\([^,]+,\s*"([^"]+)"\s*,\s*"([^"]+)"', line):
                add(path, text, "button_name", unescape_csharp_string(m.group(1)), idx, "AddPressed buttonName")
                add(path, text, "button_pressed", unescape_csharp_string(m.group(2)), idx, "AddPressed pressedButton")

            for m in re.finditer(r'AddCustom\([^,]+,\s*"([^"]+)"', line):
                add(path, text, "button_name", unescape_csharp_string(m.group(1)), idx, "AddCustom buttonName")

            for m in re.finditer(r'\.AddButton\(\s*"([^"]+)"', line):
                add(path, text, "button_name", unescape_csharp_string(m.group(1)), idx, ".AddButton")

            for m in re.finditer(r'\.HasButton\(\s*"([^"]+)"', line):
                add(path, text, "button_name", unescape_csharp_string(m.group(1)), idx, ".HasButton")

            for m in re.finditer(r'\.RemoveButton\(\s*"([^"]+)"', line):
                add(path, text, "button_name", unescape_csharp_string(m.group(1)), idx, ".RemoveButton")

            for m in re.finditer(r'PressedButton\([^;\n]*,\s*"([^"]+)"\s*,', line):
                add(path, text, "button_pressed", unescape_csharp_string(m.group(1)), idx, "PressedButton")

            for m in re.finditer(r'determineMoneyCost\(\s*"([^"]+)"\s*\)', line):
                add(path, text, "money_cost", unescape_csharp_string(m.group(1)), idx, "determineMoneyCost")

            for m in re.finditer(r'determineMoneyCost\([^;\n]*,\s*"([^"]+)"\s*\)', line):
                add(path, text, "money_cost", unescape_csharp_string(m.group(1)), idx, "determineMoneyCost")

            for m in re.finditer(r'\b(?:SayDialogue|Say)\(\s*"([^"]+)"', line):
                add(path, text, "dialogue_key", unescape_csharp_string(m.group(1)), idx, m.group(0).split("(")[0])

            for m in re.finditer(r'GetName\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\)', line):
                add(path, text, "name_type", unescape_csharp_string(m.group(2)), idx, "NameDB.GetName type")
                typ = unescape_csharp_string(m.group(2))
                role = {
                    "Agent": "agent_name",
                    "Item": "item_name",
                    "Object": "object_name",
                    "StatusEffect": "status_effect",
                    "Interface": "button_name",
                    "Dialogue": "dialogue_key",
                    "Unlock": "mutator",
                }.get(typ)
                if role:
                    add(path, text, role, unescape_csharp_string(m.group(1)), idx, "NameDB.GetName key")

            for m in re.finditer(r'\b(?:hasTrait|AddTrait|RemoveTrait)\([^)"\n]*"([^"]+)"', line):
                value = unescape_csharp_string(m.group(1))
                add(path, text, "trait_name", value, idx, "trait lookup")

            for m in re.finditer(r'\bAgentTraits\.Has\([^)"\n]*"([^"]+)"', line):
                add(path, text, "trait_name", unescape_csharp_string(m.group(1)), idx, "trait lookup")

            for m in re.finditer(r'\bEnsureTrait\([^)"\n]*"([^"]+)"', line):
                add(path, text, "trait_name", unescape_csharp_string(m.group(1)), idx, "EnsureTrait")

            for m in re.finditer(r'\bVanillaEffect\s*=>\s*"([^"]+)"', line):
                add(path, text, "status_effect", unescape_csharp_string(m.group(1)), idx, "VanillaEffect")

            for m in re.finditer(r'\b(?:AddStatusEffect|hasStatusEffect|RemoveStatusEffect|AddPermanentStatus)\([^)"\n]*"([^"]+)"', line):
                add(path, text, "status_effect", unescape_csharp_string(m.group(1)), idx, "status-effect lookup")

            for m in re.finditer(r'\b(?:HasItem|FindItem|AddItem|SpawnItem)\([^)"\n]*"([^"]+)"', line):
                add(path, text, "item_name", unescape_csharp_string(m.group(1)), idx, "item lookup")

            for m in re.finditer(r'invItemName\s*(?:==|=)\s*"([^"]+)"', line):
                add(path, text, "item_name", unescape_csharp_string(m.group(1)), idx, "invItemName")

            for m in re.finditer(r'itemType\s*(?:==|=)\s*"([^"]+)"', line):
                add(path, text, "item_type", unescape_csharp_string(m.group(1)), idx, "itemType")

            for m in re.finditer(r'Categories\.(?:Contains|Add)\(\s*"([^"]+)"', line):
                add(path, text, "item_category", unescape_csharp_string(m.group(1)), idx, "item category")

            for m in re.finditer(r'agentName\s*(?:==|=)\s*"([^"]+)"', line):
                add(path, text, "agent_name", unescape_csharp_string(m.group(1)), idx, "agentName")

            for m in re.finditer(r'\b(?:[A-Za-z_][A-Za-z0-9_]*\.)?agentName\.Contains\(\s*"([^"]+)"', line):
                add(path, text, "agent_name_fragment", unescape_csharp_string(m.group(1)), idx, "agentName.Contains")

            for m in re.finditer(r'\bname\.Contains\(\s*"([^"]+)"', line):
                add(path, text, "agent_name_fragment", unescape_csharp_string(m.group(1)), idx, "agent name Contains")

            for m in re.finditer(r'(?:SetRel|SetRelInitial)\([^;\n]*,\s*"([^"]+)"', line):
                add(path, text, "relationship", unescape_csharp_string(m.group(1)), idx, "relationship set")

            for m in re.finditer(r'relType\s*==\s*"([^"]+)"', line):
                add(path, text, "relationship", unescape_csharp_string(m.group(1)), idx, "relationship compare")

            for m in re.finditer(r'SetDefaultGoal\(\s*"([^"]+)"', line):
                add(path, text, "goal_name", unescape_csharp_string(m.group(1)), idx, "SetDefaultGoal")

            for m in re.finditer(r'SpawnExplosion\([^;\n]*,\s*"([^"]+)"', line):
                add(path, text, "explosion_type", unescape_csharp_string(m.group(1)), idx, "SpawnExplosion")

            for m in re.finditer(r'audioHandler\.Play\([^;\n]*,\s*"([^"]+)"', line):
                add(path, text, "audio_clip", unescape_csharp_string(m.group(1)), idx, "audioHandler.Play")

            for m in re.finditer(r'challenges\.Contains\(\s*"([^"]+)"', line):
                add(path, text, "mutator", unescape_csharp_string(m.group(1)), idx, "challenge lookup")

        # Context-sensitive collections whose values later flow into game APIs.
        for value, line in extract_array_values(text, "SensitiveButtons"):
            add(path, text, "button_name", value, line, "SensitiveButtons")
        for value, line in extract_array_values(text, "MotivationItems"):
            add(path, text, "item_name", value, line, "MotivationItems")
        for value, line in extract_array_values(text, "DangerousItems"):
            add(path, text, "item_name", value, line, "DangerousItems")
        for value, line in extract_array_values(text, "QuestItems"):
            add(path, text, "item_name", value, line, "QuestItems")
        for table in ("SabotageObjects", "SkippedObjects"):
            for value, line in extract_array_values(text, table):
                add(path, text, "object_name", value, line, table)
        for table in ("ShadyAgents", "RadiantExcluded"):
            for value, line in extract_array_values(text, table):
                add(path, text, "agent_name", value, line, table)
        for value, line in extract_array_values(text, "LanguageTraits"):
            add(path, text, "ccu_trait", value, line, "LanguageTraits")
            if value != "Polyglot":
                add(path, text, "button_name", "RCK_Learn_" + value, line, "dynamic RCK_Learn_ button")

        # Trait -> status-effect tables whose values are passed to AddStatusEffect.
        for table in ("PermanentStatuses", "DrugStatuses"):
            for key, value, line in extract_dict_pairs(text, table):
                add(path, text, "ccu_trait", key, line, f"{table} trait")
                add(path, text, "status_effect", value, line, f"{table} status effect")
        for key, value, line in extract_dict_pairs(text, "PermanentTraits"):
            add(path, text, "ccu_trait", key, line, "PermanentTraits trait")
            add(path, text, "trait_name", value, line, "PermanentTraits vanilla trait")

        for match in re.finditer(r'ClipByTrait\s*=\s*new[^{]+\{(?P<body>.*?)\n\s*\};', text, re.S):
            body = match.group("body")
            for entry in re.finditer(r'\[\s*"([^"]+)"\s*\]\s*=\s*"([^"]+)"', body):
                line = line_no(text, match.start("body") + entry.start(2))
                add(path, text, "ccu_trait", unescape_csharp_string(entry.group(1)), line, "AmbientAudio trait")
                add(path, text, "audio_clip", unescape_csharp_string(entry.group(2)), line, "AmbientAudio clip")

    return uses


def validate(uses: list[Use], vocab: Vocab, allow: dict[tuple[str, str], str]) -> tuple[list[Unknown], list[Use]]:
    unknowns: list[Unknown] = []
    allowed_unknowns: list[Use] = []
    seen: set[Use] = set()
    for use in uses:
        if use in seen:
            continue
        seen.add(use)
        if vocab.has(use.role, use.value):
            continue
        if (use.role, use.value) in allow or ("*", use.value) in allow:
            allowed_unknowns.append(use)
            continue
        candidates = sorted(vocab.values.get("agent_name" if use.role == "agent_name_fragment" else use.role, set()))
        suggestions = tuple(difflib.get_close_matches(use.value, candidates, n=5, cutoff=0.58))
        wrong_kind = [ROLE_LABELS.get(role, role) for role in ("trait_name", "status_effect", "item_name", "agent_name", "audio_clip", "object_name")
                      if role != use.role and vocab.has(role, use.value)]
        if wrong_kind:
            suggestions = (f"(wrong kind: this is a {' / '.join(wrong_kind)})",) + suggestions
        unknowns.append(Unknown(use, suggestions))
    return unknowns, allowed_unknowns


def format_output(uses: list[Use], unknowns: list[Unknown], allowed: list[Use], vocab: Vocab) -> str:
    lines: list[str] = []
    lines.append(f"check-game-strings: scanned {len(set(u.file for u in uses))} RCK files; checked {len(uses)} role-aware string uses.")
    role_counts = Counter(u.role for u in uses)
    lines.append("Checked roles: " + ", ".join(f"{role}={count}" for role, count in sorted(role_counts.items())))
    lines.append(f"Unknowns: {len(unknowns)} ({len(allowed)} allowlisted)")
    if unknowns:
        lines.append("")
        lines.append("Unallowlisted unknowns:")
        for item in sorted(unknowns, key=lambda x: (x.use.file, x.use.line, x.use.role, x.use.value)):
            use = item.use
            label = ROLE_LABELS.get(use.role, use.role)
            suggestion = ""
            if item.suggestions:
                suggestion = " suggestions: " + ", ".join(item.suggestions)
            lines.append(f"- {use.file}:{use.line}: {use.role} \"{use.value}\" via {use.expr} is not a known {label}.{suggestion}")
    lines.append("")
    if unknowns:
        lines.append(f"FAILED: {len(unknowns)} unallowlisted unknown game-string use(s).")
    else:
        lines.append("OK: no unallowlisted unknown game-string uses.")
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2], help="sor-ccu repository root")
    parser.add_argument("--decomp", type=Path, default=DEFAULT_DECOMP, help="Assembly-CSharp decompile directory")
    parser.add_argument("--data", type=Path, default=DEFAULT_DATA, help="sorcampaigns data directory")
    parser.add_argument("--allowlist", type=Path, default=None, help="TSV allowlist with role, value, reason")
    args = parser.parse_args(argv)

    repo = args.repo.resolve()
    decomp = args.decomp.resolve()
    data = args.data.resolve()
    allow_path = args.allowlist or (repo / "tools" / "check-game-strings" / "allowlist.tsv")
    if not (repo / "RCK").exists():
        raise SystemExit(f"RCK source not found under {repo}")
    if not decomp.exists():
        raise SystemExit(f"decomp directory not found: {decomp}")

    vocab = Vocab()
    load_vanilla_vocab(vocab, decomp, data)
    load_ccu_spec(vocab, repo)
    harvest_ccu_customs(vocab, repo)
    allow = load_allowlist(allow_path)
    uses = extract_ccu_uses(repo)
    unknowns, allowed = validate(uses, vocab, allow)
    print(format_output(uses, unknowns, allowed, vocab))
    return 1 if unknowns else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
