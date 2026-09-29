"""Check that campaigns only use identifiers our CCU re-implementation understands.

Walks SoR campaign JSON files (and loose chunk/character files), collects every
trait, mutator, goal, item, status effect and extra-var string, then classifies
each one as vanilla, CCU (per docs/ccu-interface.json), legacy CCU (handled by
the legacy converter), or unknown. With --manifest it also checks that every
CCU name the corpus uses is registered by our plugin.

Usage:
    python ccu_compat.py [--corpus DIR_OR_FILE] [--names names.json] [--manifest manifest.json]
                         [--json out.json] [--emit-usage docs/internal/ccu-corpus-usage.json]
                         [--fail-on-missing]

Optional corpus paths may point at .json/.dat files or directories (for example
Documents\\Streets of Rogue or the Steam Workshop content\\512900 directory). Files
that are binary or not JSON-readable are skipped read-only.
"""
from __future__ import annotations

import argparse
import collections
import json
import sys
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SPEC = ROOT / "docs" / "ccu-interface.json"
# The sorcampaigns toolkit checkout next to this repository.
DEFAULT_CORPUS = ROOT.parent / "sorcampaigns" / "data" / "corpus"
DEFAULT_NAMES = ROOT.parent / "sorcampaigns" / "data" / "names.json"

VANILLA_GOALS = {
    "", "None", "Idle", "Guard", "Patrol", "Dance", "IceSkate", "Swim", "ListenToJokeNPC", "Joke",
    "Sit", "Sleep", "CuriousObject", "Wander", "WanderInOwnedProperty", "WanderFar",
}
INVESTIGATE_PREFIX = "investigateable-message:::"
EXTRA_VAR_KEYS = ("extraVarString", "extraVarString2", "extraVarString3", "extraVarString4")
AGENT_SPAWNER_TYPES = {"Agent"}  # Verified from the current corpus; reported by goal_spawner_types.

CONTAINER_OBJECTS: set[str] = set()
INVESTIGATE_OBJECTS: set[str] = set()
VALID_ITEM_NAMES: set[str] = set()
VALID_AGENT_NAMES: set[str] = set()
VALID_OBJECT_NAMES: set[str] = set()


def load_json(path: Path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


class Usage:
    def __init__(self, label: str = ""):
        self.label = label
        self.traits = collections.Counter()
        self.mutators = collections.Counter()
        self.goals = collections.Counter()
        self.items = collections.Counter()
        self.extra = collections.Counter()
        self.where: dict[tuple[str, str], set[str]] = collections.defaultdict(set)

        self.candidates = 0
        self.files = 0
        self.skipped = 0
        self.skip_reasons = collections.Counter()

        self.investigate = 0
        self.investigate_objects = collections.Counter()
        self.investigate_offlist = collections.Counter()
        self.investigate_slots = collections.Counter()

        self.container_item_objects = collections.Counter()
        self.container_item_offlist = collections.Counter()
        self.container_item_values = collections.Counter()
        self.container_non_item_values = collections.Counter()
        self.container_non_item_objects = collections.Counter()
        self.container_non_item_classes = collections.Counter()

        self.goal_spawner_types = collections.Counter()
        self.goal_spawner_examples: dict[str, str] = {}

    def note(self, kind: str, name: str, src: str, n: int = 1):
        if name is None or n <= 0:
            return
        getattr(self, kind)[name] += n
        self.where[(kind, name)].add(src)

    def merge(self, other: "Usage"):
        for attr in ("traits", "mutators", "goals", "items", "extra", "skip_reasons", "investigate_objects",
                     "investigate_offlist", "investigate_slots", "container_item_objects",
                     "container_item_offlist", "container_item_values", "container_non_item_values",
                     "container_non_item_objects", "container_non_item_classes", "goal_spawner_types"):
            getattr(self, attr).update(getattr(other, attr))
        for key, files in other.where.items():
            self.where[key].update(files)
        self.candidates += other.candidates
        self.files += other.files
        self.skipped += other.skipped
        self.investigate += other.investigate
        self.goal_spawner_examples.update(other.goal_spawner_examples)


def classify_container_non_item(value: str) -> str:
    if INVESTIGATE_PREFIX in value:
        return "investigate-text"
    if value.startswith(("[CCU]", "[RCK]")):
        return "ccu-data"
    if value == "Randomized":
        return "randomized"
    if value in VALID_AGENT_NAMES:
        return "agent-name"
    if value in VALID_OBJECT_NAMES:
        return "object-name"
    if "\n" in value or "\r" in value or len(value) > 40 or any(ord(ch) > 127 for ch in value):
        return "free-text"
    return "other"


def scan_character(ch: dict, u: Usage, src: str):
    for t in ch.get("traits") or []:
        if isinstance(t, str):
            u.note("traits", t, src)
    for it in ch.get("items") or []:
        if isinstance(it, str):
            u.note("items", it, src)


def scan_chunk(chunk: dict, u: Usage, src: str):
    for ch in chunk.get("customCharacterList") or []:
        if isinstance(ch, dict):
            scan_character(ch, u, src)

    for sp in chunk.get("spawnerList") or []:
        if not isinstance(sp, dict):
            continue

        spawner_type = sp.get("spawnerType")
        goal = sp.get("defaultGoal")
        if goal is not None:
            if goal != "":
                type_name = str(spawner_type)
                u.goal_spawner_types[type_name] += 1
                u.goal_spawner_examples.setdefault(type_name, f"{src}: {sp.get('spawnerName') or ''}")
            if spawner_type in AGENT_SPAWNER_TYPES:
                u.note("goals", str(goal), src)

        obj = sp.get("spawnerName") or ""
        for key in EXTRA_VAR_KEYS:
            value = sp.get(key)
            if not isinstance(value, str) or not value:
                continue

            investigate_count = value.count(INVESTIGATE_PREFIX)
            if investigate_count:
                u.investigate += investigate_count
                u.note("extra", INVESTIGATE_PREFIX, src, investigate_count)
                u.note("extra", f"{INVESTIGATE_PREFIX} on {obj}", src, investigate_count)
                u.investigate_objects[obj] += investigate_count
                u.investigate_slots[key] += investigate_count
                if obj not in INVESTIGATE_OBJECTS:
                    u.investigate_offlist[obj] += investigate_count

            if value.startswith(("[CCU]", "[RCK]")):
                u.note("extra", value.split("::", 1)[0] + ("::" if "::" in value else ""), src)

            if key != "extraVarString" or spawner_type != "Object" or INVESTIGATE_PREFIX in value:
                continue

            if value in VALID_ITEM_NAMES:
                u.note("items", value, src)
                u.note("extra", "container item", src)
                if obj in CONTAINER_OBJECTS:
                    u.note("extra", f"container item on {obj}", src)
                u.container_item_objects[obj] += 1
                u.container_item_values[value] += 1
                if obj not in CONTAINER_OBJECTS:
                    u.container_item_offlist[obj] += 1
            elif obj in CONTAINER_OBJECTS:
                value_class = classify_container_non_item(value)
                u.container_non_item_values[value] += 1
                u.container_non_item_objects[obj] += 1
                u.container_non_item_classes[value_class] += 1
                u.note("extra", f"container non-item ({value_class})", src)


def scan_file(path: Path, u: Usage):
    u.candidates += 1
    try:
        data = load_json(path)
    except (OSError, UnicodeDecodeError, ValueError) as exc:
        u.skipped += 1
        u.skip_reasons[type(exc).__name__] += 1
        return

    u.files += 1
    src = str(path)
    if not isinstance(data, dict):
        return

    for m in data.get("mutatorList") or []:
        if isinstance(m, str):
            u.note("mutators", m, src)

    for key in ("customCharacterData", "customCharacterData1", "customCharacterData2", "customCharacterData3", "customCharacterData4"):
        if isinstance(data.get(key), dict):
            scan_character(data[key], u, src)

    for lvl in data.get("levelList") or []:
        if not isinstance(lvl, dict):
            continue
        for m in lvl.get("levelMutators") or []:
            if isinstance(m, str):
                u.note("mutators", m, src)
        for chunk in lvl.get("chunkList") or []:
            if isinstance(chunk, dict):
                scan_chunk(chunk, u, src)

    for pack in data.get("chunkPackToCampaignList") or []:
        if not isinstance(pack, dict):
            continue
        for chunk in pack.get("chunkList") or []:
            if isinstance(chunk, dict):
                scan_chunk(chunk, u, src)

    for chunk in data.get("chunkList") or []:
        if isinstance(chunk, dict):
            scan_chunk(chunk, u, src)

    if "spawnerList" in data:
        scan_chunk(data, u, src)
    if "traits" in data and "characterName" in data:
        scan_character(data, u, src)


def iter_candidates(corpus: Path) -> Iterable[Path]:
    if corpus.is_file():
        yield corpus
        return
    if not corpus.exists():
        return
    for pattern in ("*.json", "*.dat"):
        yield from corpus.rglob(pattern)


def scan_corpus(corpus: Path) -> Usage:
    u = Usage(str(corpus))
    for path in iter_candidates(corpus):
        scan_file(path, u)
    return u


def classify(u: Usage, spec: dict, names: dict, manifest: dict | None):
    vanilla_traits = set(names.get("unlock.Trait", [])) | set(names.get("StatusEffectNameDB", []))
    vanilla_mutators = set(names.get("unlock.Challenge", []))
    vanilla_items = set(names.get("unlock.Item", [])) | set(names.get("ItemNameDB", []))
    ccu_traits = set(spec["traits"])
    ccu_mutators = {k for k in spec["mutators"] if not k.startswith("<dynamic:")}
    ccu_goals = set(spec["goals"])
    ccu_items = set(spec["items"])
    legacy_traits = set(spec["legacy"].get("TraitConversions", {}))
    legacy_mutators = set(spec["legacy"].get("MutatorConversions", {})) | set(spec["legacy"].get("ChallengeConversions", {}))
    legacy_goals = {g for g, v in spec["goals"].items() if "SceneSetters_Legacy" in v.get("lists", [])}

    def bucket(name, vanilla, ccu, legacy, extra_ccu=lambda n: False):
        if name in vanilla:
            return "vanilla"
        if name in ccu or extra_ccu(name):
            return "ccu"
        if name in legacy:
            return "legacy"
        return "unknown"

    def is_dynamic_mutator(n: str) -> bool:
        return n.startswith("[CCU]")

    res = {}
    for kind, counter, vanilla, ccu, legacy, extra in (
        ("traits", u.traits, vanilla_traits, ccu_traits, legacy_traits, lambda n: False),
        ("mutators", u.mutators, vanilla_mutators, ccu_mutators, legacy_mutators, is_dynamic_mutator),
        ("goals", u.goals, VANILLA_GOALS, ccu_goals, legacy_goals, lambda n: False),
        ("items", u.items, vanilla_items, ccu_items, set(), lambda n: False),
    ):
        rows = []
        manifest_names = set(manifest.get(kind, [])) if manifest else set()
        for name, n in counter.most_common():
            b = bucket(name, vanilla, ccu, legacy, extra)
            row = {"name": name, "count": n, "bucket": b, "files": len(u.where[(kind, name)])}
            if manifest is not None and b in ("ccu", "legacy"):
                row["handled"] = name in manifest_names or (
                    kind == "mutators" and is_dynamic_mutator(name) and manifest.get("dynamic_mutators", False))
            rows.append(row)
        res[kind] = rows

    res["extra"] = [
        {"name": k, "count": v, "files": len(u.where[("extra", k)])}
        for k, v in u.extra.most_common()
    ]
    res["investigate_count"] = u.investigate
    res["investigate_objects"] = object_rows(u.investigate_objects, INVESTIGATE_OBJECTS)
    res["investigate_offlist"] = object_rows(u.investigate_offlist, INVESTIGATE_OBJECTS)
    res["investigate_slots"] = dict(u.investigate_slots.most_common())
    res["container_item_objects"] = object_rows(u.container_item_objects, CONTAINER_OBJECTS)
    res["container_item_offlist"] = object_rows(u.container_item_offlist, CONTAINER_OBJECTS)
    res["container_item_values"] = counter_rows(u.container_item_values)
    res["container_non_item"] = {
        "distinct_values": len(u.container_non_item_values),
        "count": sum(u.container_non_item_values.values()),
        "classes": dict(u.container_non_item_classes.most_common()),
        "objects": counter_rows(u.container_non_item_objects),
        "examples": [
            {"value": k, "count": v, "class": classify_container_non_item(k)}
            for k, v in u.container_non_item_values.most_common(25)
        ],
    }
    res["goal_spawner_types"] = dict(u.goal_spawner_types.most_common())
    res["goal_spawner_examples"] = u.goal_spawner_examples
    res["files"] = {"candidates": u.candidates, "readable_json": u.files, "skipped": u.skipped,
                    "skip_reasons": dict(u.skip_reasons)}
    return res


def counter_rows(counter: collections.Counter, limit: int | None = None):
    items = counter.most_common(limit)
    return [{"name": k, "count": v} for k, v in items]


def object_rows(counter: collections.Counter, allowed: set[str]):
    return [
        {"name": k, "count": v, "in_ccu_list": k in allowed}
        for k, v in counter.most_common()
    ]


def make_usage_json(res: dict, corpora: list[str], spec_path: Path):
    out = {"source": {"spec": str(spec_path), "corpora": corpora}}
    for kind in ("traits", "mutators", "goals", "items"):
        out[kind] = {
            row["name"]: {"bucket": row["bucket"], "count": row["count"], "files": row["files"]}
            for row in res[kind]
            if row["bucket"] in ("ccu", "legacy")
        }
    out["extra"] = {
        row["name"]: {"count": row["count"], "files": row["files"]}
        for row in res["extra"]
        if row["name"] == INVESTIGATE_PREFIX
        or row["name"].startswith(f"{INVESTIGATE_PREFIX} on ")
        or row["name"].startswith("container item")
        or row["name"].startswith("[CCU]")
    }
    return out




def short_repr(value: str, limit: int = 120) -> str:
    text = repr(value)
    if len(text) > limit:
        return text[:limit - 1] + "…"
    return text

def print_rows(title: str, rows: list[dict], limit: int):
    if not rows:
        return
    shown = rows[:limit]
    suffix = "" if len(rows) <= limit else f" ... +{len(rows) - limit} more"
    print(f"{title}: " + ", ".join(f"{r['name']}x{r['count']}" for r in shown) + suffix)


def print_result(label: str, res: dict, manifest: dict | None, show_unknown: int, show_extra: int, show_objects: int):
    files = res["files"]
    print(f"== {label} ==")
    skipped = f"; skipped {files['skipped']}" if files["skipped"] else ""
    print(f"scanned {files['readable_json']} JSON-readable files ({files['candidates']} candidates{skipped}); "
          f"investigate-text occurrences: {res['investigate_count']}")
    if files["skip_reasons"]:
        print("skips: " + ", ".join(f"{k}={v}" for k, v in files["skip_reasons"].items()))
    if res["goal_spawner_types"]:
        print("goal spawner types: " + ", ".join(f"{k}={v}" for k, v in res["goal_spawner_types"].items()))

    missing = 0
    for kind in ("traits", "mutators", "goals", "items"):
        rows = res[kind]
        by = collections.Counter(r["bucket"] for r in rows)
        line = f"{kind:9s} distinct={len(rows):4d}  " + "  ".join(
            f"{b}={by[b]}" for b in ("vanilla", "ccu", "legacy", "unknown"))
        if manifest is not None:
            miss = [r for r in rows if r.get("handled") is False]
            missing += len(miss)
            line += f"  not-handled={len(miss)}"
        print(line)
        unk = [r for r in rows if r["bucket"] == "unknown"][:show_unknown]
        if unk:
            print("    unknown: " + ", ".join(f"{short_repr(r['name'])}x{r['count']}" for r in unk))
        if manifest is not None:
            miss = [r for r in rows if r.get("handled") is False]
            if miss:
                print("    NOT HANDLED: " + ", ".join(f"{r['name']}x{r['count']}" for r in miss[:show_unknown]))

    if res["extra"]:
        print_rows("extra-var formats", res["extra"], show_extra)
    print_rows("investigate objects", res["investigate_objects"], show_objects)
    print_rows("investigate off-list objects", res["investigate_offlist"], show_objects)
    print_rows("container item objects", res["container_item_objects"], show_objects)
    print_rows("container item off-list objects", res["container_item_offlist"], show_objects)

    non_item = res["container_non_item"]
    if non_item["count"]:
        print(f"container slot non-item values on CCU container objects: {non_item['count']} uses, "
              f"{non_item['distinct_values']} distinct; classes={non_item['classes']}")
        examples = non_item["examples"][:min(show_unknown, 10)]
        if examples:
            print("    examples: " + ", ".join(
                f"{short_repr(e['value'])}x{e['count']}({e['class']})" for e in examples))
    return missing


def main(argv=None):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", type=Path, action="append",
                    help="JSON/.dat file or directory to scan. Repeat to compare corpora separately.")
    ap.add_argument("--spec", type=Path, default=DEFAULT_SPEC)
    ap.add_argument("--names", type=Path, default=DEFAULT_NAMES)
    ap.add_argument("--manifest", type=Path)
    ap.add_argument("--json", type=Path)
    ap.add_argument("--emit-usage", type=Path)
    ap.add_argument("--show-unknown", type=int, default=25)
    ap.add_argument("--show-extra", type=int, default=60)
    ap.add_argument("--show-objects", type=int, default=40)
    ap.add_argument("--fail-on-missing", action="store_true")
    args = ap.parse_args(argv)

    spec = load_json(args.spec)
    object_lists = spec.get("object_lists", {})
    CONTAINER_OBJECTS.update(object_lists.get("ContainerObjects_Slot1", {}).get("objects", []))
    INVESTIGATE_OBJECTS.update(object_lists.get("InvestigateableObjects_Slot1", {}).get("objects", []))
    INVESTIGATE_OBJECTS.update(object_lists.get("InvestigateableObjects_Slot2", {}).get("objects", []))

    names = load_json(args.names)
    VALID_ITEM_NAMES.update(names.get("unlock.Item", []))
    VALID_ITEM_NAMES.update(names.get("ItemNameDB", []))
    VALID_ITEM_NAMES.update(spec.get("items", {}).keys())
    VALID_AGENT_NAMES.update(names.get("AgentNameDB", []))
    VALID_OBJECT_NAMES.update(names.get("ObjectNameDB", []))

    manifest = load_json(args.manifest) if args.manifest else None
    corpus_paths = args.corpus or [DEFAULT_CORPUS]

    usages = [scan_corpus(path) for path in corpus_paths]
    aggregate = Usage("aggregate")
    for usage in usages:
        aggregate.merge(usage)

    per_corpus = {usage.label: classify(usage, spec, names, manifest) for usage in usages}
    aggregate_res = classify(aggregate, spec, names, manifest)

    total_missing = 0
    for usage in usages:
        total_missing += print_result(usage.label, per_corpus[usage.label], manifest,
                                      args.show_unknown, args.show_extra, args.show_objects)
    if len(usages) > 1:
        total_missing += print_result("aggregate", aggregate_res, manifest,
                                      args.show_unknown, args.show_extra, args.show_objects)

    if args.json:
        payload = dict(aggregate_res)
        payload["corpora"] = per_corpus
        args.json.write_text(json.dumps(payload, indent=1, ensure_ascii=False), encoding="utf-8")
    if args.emit_usage:
        usage_json = make_usage_json(aggregate_res, [str(p) for p in corpus_paths], args.spec)
        args.emit_usage.write_text(json.dumps(usage_json, indent=1, ensure_ascii=False), encoding="utf-8")

    return 1 if (args.fail_on_missing and total_missing) else 0


if __name__ == "__main__":
    sys.exit(main())
