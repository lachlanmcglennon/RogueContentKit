"""Completeness gate for RCK trait descriptions: every trait id has exactly one well-formed description.

Expected ids are the traits in RCK/Core/Generated/RckData.g.cs plus any Rck.RegisterExtension<T> call in RCK/.

Usage: python tools/clean-room/check_complete.py [descriptions.json]
       (default RCK/Core/Data/trait-descriptions.json). Exit 1 on the first problem.
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RCK = ROOT / "RCK"
RCKDATA = RCK / "Core" / "Generated" / "RckData.g.cs"
ENTRY_RE = re.compile(r'new RckTraitInfo\(typeof\(T\.[^)]+\), "([^"]+)"')


def fail(msg: str):
    print("FAIL descriptions: " + msg)
    raise SystemExit(1)


def expected_ids() -> list[str]:
    ids = ENTRY_RE.findall(RCKDATA.read_text(encoding="utf-8-sig"))
    if not ids:
        fail("no trait ids parsed from RckData.g.cs")
    for path in RCK.rglob("*.cs"):
        if {"bin", "obj"} & {p.lower() for p in path.parts}:
            continue
        s = path.read_text(encoding="utf-8-sig", errors="replace")
        for m in re.finditer(r"RegisterExtension\s*<\s*([^>\s]+)", s):
            name = m.group(1).split(".")[-1]
            if name != "TTrait":
                ids.append(name)
    return ids


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else RCK / "Core" / "Data" / "trait-descriptions.json"
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, list):
        fail("top level is not a JSON array")
    exp_counts = Counter(expected_ids())
    got_ids = [row.get("id") for row in data]
    got_counts = Counter(got_ids)
    missing = sorted((exp_counts - got_counts).elements())
    extra = sorted((got_counts - exp_counts).elements())
    dupes = sorted(k for k, v in got_counts.items() if v != 1)
    if missing:
        fail("trait ids with no description: " + ", ".join(missing[:20]))
    if extra:
        fail("descriptions for unknown ids: " + ", ".join(extra[:20]))
    if dupes:
        fail("duplicate ids: " + ", ".join(dupes[:20]))
    if got_ids != sorted(got_ids):
        fail("entries are not sorted by id")
    for i, row in enumerate(data):
        for key in ["id", "en", "kind", "source", "dynamic", "confidence"]:
            if key not in row:
                fail(f"row {i} has no {key}")
        text = row["en"]
        if not isinstance(text, str) or not text.strip():
            fail(f"{row['id']} has empty text")
        if len(text) > 300:
            fail(f"{row['id']} text is {len(text)} characters (max 300)")
        for bad in ["{", "<", "CCU", "[RCK]"]:
            if bad in text:
                fail(f"{row['id']} text contains {bad}")
        if row["kind"] not in {"designer", "player", "legacy", "extension"}:
            fail(f"{row['id']} has invalid kind {row['kind']}")
        if row["confidence"] not in {"high", "medium", "low"}:
            fail(f"{row['id']} has invalid confidence {row['confidence']}")
        if not isinstance(row["dynamic"], bool):
            fail(f"{row['id']} dynamic is not a boolean")
    print(f"descriptions: {len(data)} complete, {len(exp_counts)} expected ids, no gaps or extras")


if __name__ == "__main__":
    main()
