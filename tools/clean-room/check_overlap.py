"""Clean-room gate for RCK text: no N-word run of Custom Content Utilities' trait descriptions may appear in ours.

CCU's descriptions are CC BY-NC-ND 4.0, so RCK writes its own. This reads CCU's description strings from a local
clone at upstream/CCU (never shipped, never printed in full) and reports every RCK description that shares an N-word
run with one. Runs made only of words from a trait's own id or display name are allowed: those are the compatibility
names campaigns store.

Usage:
  python tools/clean-room/check_overlap.py [descriptions.json] [--n 5]
      Checks the trait description table (default RCK/Core/Data/trait-descriptions.json). Exit 1 on any overlap.
  python tools/clean-room/check_overlap.py --sweep [--n 5]
      Also scans RCK/, RogueLibsPlus/, the public docs/ (not docs/internal) and README.md and lists each hit that is
      not a compatibility name. Report only: exit 0.
The CCU clone is upstream/CCU, or the folder in $RCK_CCU_UPSTREAM. Without it this prints a warning and exits 0.
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
N = int(sys.argv[sys.argv.index("--n") + 1]) if "--n" in sys.argv else 5
SWEEP = "--sweep" in sys.argv
positional = [a for i, a in enumerate(sys.argv[1:], 1) if not a.startswith("--") and sys.argv[i - 1] != "--n"]
TABLE = Path(positional[0]) if positional else ROOT / "RCK" / "Core" / "Data" / "trait-descriptions.json"


def words(t: str) -> list[str]:
    t = re.sub(r"<[^>]+>|\\n|\{\d+\}", " ", t.lower())
    return re.findall(r"[a-z0-9']+", t)


def literals(s: str):
    for m in re.finditer(r'@"((?:[^"]|"")*)"|\$?"((?:[^"\\]|\\.)*)"', s):
        yield m.group(1) or m.group(2) or ""


def grams(w: list[str]):
    return (tuple(w[i:i + N]) for i in range(len(w) - N + 1))


upstream = Path(os.environ["RCK_CCU_UPSTREAM"]) if os.environ.get("RCK_CCU_UPSTREAM") else ROOT / "upstream" / "CCU"
if not upstream.is_dir():
    print(f"WARN clean-room overlap check skipped: {upstream} is not present")
    sys.exit(0)

ccu: set[tuple[str, ...]] = set()
for p in upstream.rglob("*.cs"):
    s = p.read_text(encoding="utf-8", errors="ignore")
    for m in re.finditer(r"WithDescription\s*\((.*?)\)\s*;", s, re.S):
        for lit in literals(m.group(1)):
            ccu.update(grams(words(lit)))
if not ccu:
    sys.exit("No CCU descriptions found under upstream/CCU; is the clone complete?")

names: dict[str, set[str]] = {}
data = (ROOT / "RCK" / "Core" / "Generated" / "RckData.g.cs").read_text(encoding="utf-8-sig")
for m in re.finditer(r'new RckTraitInfo\(typeof\([^)]*\), "([^"]+)", [^,]+, "[^"]*", "[^"]*", "([^"]+)"', data):
    names[m.group(1)] = set(words(m.group(1).replace("_", " ") + " " + m.group(2)))
all_names = list(names.values())

entries = json.loads(TABLE.read_text(encoding="utf-8-sig"))
bad = 0
for e in entries:
    tid, text = e["id"], e.get("en", "")
    own = names.get(tid, set(words(tid.replace("_", " "))))
    hits = [" ".join(g) for g in grams(words(text)) if g in ccu and not set(g) <= own]
    if hits:
        bad += 1
        print(f"OVERLAP {tid}: {len(hits)} run(s), e.g. '{hits[0]}'")
print(f"clean-room: checked {len(entries)} descriptions, {bad} overlapping (n={N})")

if SWEEP:
    targets = [ROOT / "RCK", ROOT / "RogueLibsPlus", ROOT / "docs", ROOT / "README.md"]
    skip_dirs = {"bin", "obj", "internal"}
    exts = {".cs", ".md", ".txt", ".json", ".py", ".ps1"}
    files = []
    for t in targets:
        if t.is_file():
            files.append(t)
        elif t.is_dir():
            files += [p for p in t.rglob("*") if p.is_file() and p.suffix in exts
                      and not skip_dirs & {x.lower() for x in p.relative_to(ROOT).parts[:-1]}]
    total = 0
    for p in sorted(files):
        if p.name == "trait-descriptions.json" or p.name.endswith(".g.cs") or p.name == "ccu-interface.json":
            continue  # the table is checked above; generated data holds names only
        text = p.read_text(encoding="utf-8", errors="ignore")
        for n, line in enumerate(text.splitlines(), 1):
            for g in grams(words(line)):
                if g in ccu and not any(set(g) <= nm for nm in all_names):
                    total += 1
                    print(f"SWEEP {p.relative_to(ROOT)}:{n}: '{' '.join(g)}'")
                    break
    print(f"clean-room sweep: {len(files)} files, {total} line(s) with a non-name {N}-word run")

sys.exit(1 if bad else 0)
