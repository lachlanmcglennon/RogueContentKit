"""Harvest CCU's externally visible identifiers (names and formats only, no logic).

CCU (Freiling87/CCU) has no licence file in its source and its releases are CC BY-NC-ND 4.0, so nothing from it is
copied into our plugin. This script reads the upstream clone in upstream/CCU at every release tag plus HEAD and
records only the identifiers that end up in saved data or
that other content refers to: trait/item/effect/mutator IDs, their unlock settings, display-name formats, default-goal
strings, legacy rename tables and appearance-pool values. The output (docs/ccu-interface.json) is the contract our
clean-room plugin must satisfy; tools/ccu-interface/ccu_compat.py checks the plugin and the corpus against it.

Usage: python tools/ccu-interface/harvest_ccu.py [--upstream upstream/CCU] [--roguelibs upstream/RogueLibs/RogueLibsCore]
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
from collections import OrderedDict
from pathlib import Path

from ccux_extensions import merge as merge_extensions

ROOT = Path(__file__).resolve().parents[2]
REFS = ["v0.1.0", "v1.0.0", "v1.1.0", "v1.2.0", "v1.4.0", "HEAD"]
BLOCK_TAG = "[CCU]"


# --------------------------------------------------------------------------------------------- C# text utilities

def is_string_start(src: str, i: int) -> bool:
    c = src[i]
    if c == '"':
        return True
    if c in "@$":
        j = i
        while j < len(src) and src[j] in "@$":
            j += 1
        return j < len(src) and src[j] == '"' and j - i <= 2
    return False


def skip_string(src: str, i: int) -> int:
    """Return the index just past the string literal starting at i ("...", @"...", $"...", $@"...")."""
    verbatim = False
    while src[i] in "@$":
        verbatim |= src[i] == "@"
        i += 1
    i += 1
    n = len(src)
    while i < n:
        if verbatim:
            if src[i] == '"':
                if i + 1 < n and src[i + 1] == '"':
                    i += 2
                    continue
                return i + 1
        else:
            if src[i] == "\\":
                i += 2
                continue
            if src[i] == '"' or src[i] == "\n":
                return i + 1
        i += 1
    return n


def strip_comments(src: str) -> str:
    """Remove // and /* */ comments while leaving string/char literals intact."""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            j = src.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "*":
            j = src.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if is_string_start(src, i):
            j = skip_string(src, i)
            out.append(src[i:j])
            i = j
            continue
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'" and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            out.append(src[i:j + 1])
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def balanced(src: str, i: int, open_ch: str, close_ch: str) -> int:
    """src[i] == open_ch; return index of the matching close_ch (skipping strings)."""
    depth, n = 0, len(src)
    while i < n:
        if is_string_start(src, i):
            i = skip_string(src, i)
            continue
        c = src[i]
        if c == open_ch:
            depth += 1
        elif c == close_ch:
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return n


def statement_end(src: str, i: int) -> int:
    """Index of the ';' ending the statement that contains position i (depth-aware)."""
    depth, n = 0, len(src)
    while i < n:
        if is_string_start(src, i):
            i = skip_string(src, i)
            continue
        c = src[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth < 0:
                return i
        elif c == ";" and depth == 0:
            return i
        i += 1
    return n


def split_top(s: str, sep: str = ",") -> list[str]:
    parts, depth, cur, i = [], 0, [], 0
    while i < len(s):
        if is_string_start(s, i):
            j = skip_string(s, i)
            cur.append(s[i:j])
            i = j
            continue
        c = s[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == sep and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    if "".join(cur).strip():
        parts.append("".join(cur).strip())
    return parts


def unquote(lit: str) -> str | None:
    lit = lit.strip()
    m = re.fullmatch(r'(@?)"((?:[^"\\]|\\.|"")*)"', lit, re.S)
    if not m:
        return None
    body = m.group(2)
    if m.group(1):
        return body.replace('""', '"')
    return re.sub(r"\\(.)", lambda e: {"n": "\n", "t": "\t", "r": "\r", "0": "\0"}.get(e.group(1), e.group(1)), body)


# --------------------------------------------------------------------------------------------- git access

def git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True,
                          encoding="utf-8", errors="replace").stdout


def read_tree(repo: Path, ref: str) -> dict[str, str]:
    names = [p for p in git(repo, "ls-tree", "-r", "--name-only", ref).splitlines() if p.endswith(".cs")]
    proc = subprocess.run(["git", "-C", str(repo), "cat-file", "--batch"],
                          input="".join(f"{ref}:{p}\n" for p in names).encode("utf-8"), capture_output=True, check=True)
    data, files, pos = proc.stdout, {}, 0
    for p in names:
        header_end = data.index(b"\n", pos)
        size = int(data[pos:header_end].split()[2])
        body = data[header_end + 1:header_end + 1 + size]
        pos = header_end + 1 + size + 1
        files[p] = body.decode("utf-8-sig", errors="replace")
    return files


# --------------------------------------------------------------------------------------------- harvesting

CLASS_RE = re.compile(r"\b(?:class|struct)\s+(\w+)(?:\s*<[^>{]*>)?\s*(?::\s*([\w.]+(?:<[^>{]*>)?))?")
NS_RE = re.compile(r"\bnamespace\s+([\w.]+)")


def const_table(files: dict[str, str]) -> dict[str, str]:
    """ClassName.Const -> value for every `const string` in the given sources."""
    table: dict[str, str] = {}
    for src in files.values():
        src = strip_comments(src)
        for m in re.finditer(r"\bconst\s+string\b", src):
            end = statement_end(src, m.end())
            cls = None
            for cm in CLASS_RE.finditer(src, 0, m.start()):
                cls = cm.group(1)
            for part in split_top(src[m.end():end]):
                mm = re.fullmatch(r"(\w+)\s*=\s*(.+)", part.strip(), re.S)
                if mm and cls:
                    v = unquote(mm.group(2))
                    if v is not None:
                        table[f"{cls}.{mm.group(1)}"] = v
    return table


class Harvester:
    def __init__(self, files: dict[str, str], consts: dict[str, str]):
        self.files = {p: strip_comments(s) for p, s in files.items()}
        self.consts = consts
        self.classes: dict[str, dict] = {}
        for path, src in self.files.items():
            for m in CLASS_RE.finditer(src):
                ns = None
                for nm in NS_RE.finditer(src, 0, m.start()):
                    ns = nm.group(1)
                base = (m.group(2) or "").split("<")[0].split(".")[-1] or None
                body_start = src.find("{", m.end())
                body_end = balanced(src, body_start, "{", "}") if body_start >= 0 else m.end()
                self.classes.setdefault(m.group(1), {"namespace": ns, "base": base, "path": path,
                                                      "body": src[body_start:body_end + 1]})

    def base_chain(self, name: str) -> list[str]:
        chain, seen = [], set()
        while name and name not in seen:
            seen.add(name)
            chain.append(name)
            name = self.classes.get(name, {}).get("base")
        return chain

    def value(self, expr: str):
        expr = expr.strip()
        s = unquote(expr)
        if s is not None:
            return s
        m = re.fullmatch(r"nameof\(\s*([\w.]+)\s*\)", expr)
        if m:
            return m.group(1).split(".")[-1]
        m = re.fullmatch(r"typeof\(\s*([\w.]+)\s*\)(?:\.Name)?", expr)
        if m:
            return m.group(1).split(".")[-1]
        if re.fullmatch(r"-?\d+", expr):
            return int(expr)
        if expr in ("true", "false"):
            return expr == "true"
        m = re.fullmatch(r"(?:\w+\.)*(\w+)\.(\w+)", expr)
        if m and f"{m.group(1)}.{m.group(2)}" in self.consts:
            return self.consts[f"{m.group(1)}.{m.group(2)}"]
        return {"expr": re.sub(r"\s+", " ", expr)[:160]}

    def init_block(self, text: str, start: int) -> dict:
        """Parse `{ A = x, B = { ... }, ... }` starting at text[start] == '{'."""
        end = balanced(text, start, "{", "}")
        out: dict = {}
        for part in split_top(text[start + 1:end]):
            m = re.fullmatch(r"(\w+)\s*=\s*(.+)", part, re.S)
            if not m:
                continue
            key, val = m.group(1), m.group(2).strip()
            if val.startswith("{"):
                inner = val[1:val.rfind("}")]
                items = split_top(inner)
                if items and all(re.fullmatch(r"\w+\s*=.*", it, re.S) for it in items):
                    out[key] = self.init_block(val, 0)
                else:
                    out[key] = [self.value(it) for it in items]
            else:
                out[key] = self.value(val)
        return out

    def english_name(self, stmt: str, what: str = "WithName"):
        i = stmt.find("." + what + "(")
        if i < 0:
            return None
        j = balanced(stmt, stmt.index("(", i), "(", ")")
        seg = stmt[i:j]
        m = re.search(r"\[\s*LanguageCode\.English\s*\]\s*=\s*", seg)
        if m:
            k = m.end()
            depth, n = 0, len(seg)
            while k < n:
                if is_string_start(seg, k):
                    k = skip_string(seg, k)
                    continue
                c = seg[k]
                if c in "([{":
                    depth += 1
                elif c in ")]}":
                    if depth == 0:
                        break
                    depth -= 1
                elif c == "," and depth == 0:
                    break
                k += 1
            return seg[m.end():k].strip()
        m = re.search(r"new\s+CustomNameInfo\s*\(", seg)
        if not m:
            return None
        k = balanced(seg, m.end() - 1, "(", ")")
        args = split_top(seg[m.end():k])
        return args[0] if args else ""

    def unlock(self, stmt: str):
        i = stmt.find(".WithUnlock(")
        if i < 0:
            return None, None
        m = re.compile(r"\s*new\s+(\w+)\s*(\([^)]*\))?\s*").match(stmt, i + len(".WithUnlock("))
        if not m:
            return None, None
        cls = m.group(1)
        body = self.init_block(stmt, m.end()) if m.end() < len(stmt) and stmt[m.end()] == "{" else {}
        if m.group(2) and m.group(2).strip("() "):
            body["_ctor"] = [self.value(a) for a in split_top(m.group(2)[1:-1])]
        return cls, body

    def registrations(self, method: str):
        pat = re.compile(r"\bCreateCustom" + method + r"\s*<\s*(\w+)\s*>\s*\(")
        for path, src in self.files.items():
            for m in pat.finditer(src):
                stmt_start = max(src.rfind(";", 0, m.start()), src.rfind("{", 0, m.start()),
                                 src.rfind("}", 0, m.start())) + 1
                stmt = src[stmt_start:statement_end(src, m.start()) + 1]
                yield m.group(1), path, stmt


def prettify(name: str, player: bool = False) -> str:
    s = name.replace("_", " ")
    if player:
        s = s.replace("2", "+")
    return s.strip()


def display_name(h: Harvester, cls: str, expr: str | None, kind: str) -> str | None:
    if expr is None:
        return None
    lit = unquote(expr)
    if lit is not None:
        return lit
    v = h.value(expr)
    if isinstance(v, str):
        return v
    ns = (h.classes.get(cls, {}).get("namespace") or "").split(".")
    m = re.fullmatch(r"DisplayName\(\s*typeof\(\s*(\w+)\s*\)\s*(?:,\s*(.+))?\)", expr, re.S)
    if m:
        custom = unquote(m.group(2)) if m.group(2) else None
        return f"{BLOCK_TAG} {prettify(ns[2]) if len(ns) > 2 else ''} - {custom or prettify(m.group(1))}"
    m = re.fullmatch(r"DesignerName\(\s*typeof\(\s*(\w+)\s*\)\s*(?:,\s*(.+))?\)", expr, re.S)
    if m:
        custom = unquote(m.group(2)) if m.group(2) else None
        group = (prettify(ns[2]) if len(ns) > 2 else "") if kind == "mutator" else (prettify(ns[-1]) if ns else "")
        return f"{BLOCK_TAG} {group} - {custom or prettify(m.group(1))}"
    m = re.fullmatch(r"PlayerName\(\s*typeof\(\s*(\w+)\s*\)\s*\)", expr)
    if m:
        return prettify(m.group(1), player=kind != "mutator")
    return "{" + re.sub(r"\s+", " ", expr) + "}"


def trait_kind(h: Harvester, cls: str, stmt: str, unlock_cls: str | None, unlock: dict | None,
               name_expr: str | None) -> str:
    chain = h.base_chain(cls)
    in_cc = (unlock or {}).get("IsAvailableInCC")
    name_expr = name_expr or ""
    if "T_DesignerTrait" in chain or unlock_cls == "TU_DesignerUnlock" or name_expr.startswith("DesignerName"):
        return "designer"
    if ("T_PlayerTrait" in chain or unlock_cls == "TU_PlayerUnlock" or name_expr.startswith("PlayerName")
            or in_cc is True):
        return "player"
    if "PostProcess_DesignerTrait" in stmt or in_cc == {"expr": "Core.designerEdition"}:
        return "designer"
    return "other"


def string_array(body: str, prop: str):
    m = re.search(r"\b" + prop + r"\s*=>\s*new\s+(?:string\s*\[\s*\]|List\s*<\s*string\s*>\s*(?:\(\s*\))?)\s*\{", body)
    if not m:
        return None
    end = balanced(body, m.end() - 1, "{", "}")
    vals = [unquote(x) for x in split_top(body[m.end():end])]
    return vals if all(v is not None for v in vals) else None


def harvest_ref(files: dict[str, str], consts: dict[str, str]) -> dict:
    h = Harvester(files, consts)
    out: dict = {"traits": {}, "items": {}, "effects": {}, "mutators": {}, "goals": {}, "legacy": {},
                 "object_lists": {}}

    for cls, path, stmt in h.registrations("Trait"):
        unlock_cls, unlock = h.unlock(stmt)
        name_expr = h.english_name(stmt)
        kind = trait_kind(h, cls, stmt, unlock_cls, unlock, name_expr)
        info = h.classes.get(cls, {})
        chain = h.base_chain(cls)
        rolls = None
        for c in chain:
            rolls = string_array(h.classes.get(c, {}).get("body", ""), "Rolls")
            if rolls is not None:
                break
        out["traits"][cls] = {
            "kind": kind,
            "namespace": info.get("namespace"),
            "bases": chain[1:],
            "path": path,
            "display_en": display_name(h, cls, name_expr, kind),
            "unlock_class": unlock_cls,
            "unlock": unlock,
            "rolls": rolls,
        }

    for cls, path, stmt in h.registrations("Item"):
        unlock_cls, unlock = h.unlock(stmt)
        info = h.classes.get(cls, {})
        out["items"][cls] = {"namespace": info.get("namespace"), "bases": h.base_chain(cls)[1:], "path": path,
                             "display_en": display_name(h, cls, h.english_name(stmt), "item"),
                             "unlock_class": unlock_cls, "unlock": unlock}

    for cls, path, stmt in h.registrations("Effect"):
        info = h.classes.get(cls, {})
        out["effects"][cls] = {"namespace": info.get("namespace"), "path": path,
                               "display_en": display_name(h, cls, h.english_name(stmt), "effect")}

    for path, src in h.files.items():
        for m in re.finditer(r"\bCreateCustomUnlock\s*\(\s*new\s+(\w+)\s*\(", src):
            cls = m.group(1)
            args = split_top(src[m.end():balanced(src, m.end() - 1, "(", ")")])
            stmt_start = max(src.rfind(";", 0, m.start()), src.rfind("{", 0, m.start()),
                             src.rfind("}", 0, m.start())) + 1
            stmt = src[stmt_start:statement_end(src, m.start()) + 1]
            chain = h.base_chain(cls)
            if cls == "MutatorUnlock" and args:
                name = h.value(args[0])
            elif "MutatorUnlock" in chain:
                body = h.classes[cls]["body"]
                bm = re.search(r"\bbase\s*\(", body)
                bargs = split_top(body[bm.end():balanced(body, bm.end() - 1, "(", ")")]) if bm else []
                name = h.value(bargs[0]) if bargs else cls
                if isinstance(name, dict) and args:
                    name = h.value(args[0])
            else:
                continue
            if isinstance(name, dict):
                out["mutators"][f"<dynamic:{cls}>"] = {"path": path, "expr": name["expr"], "class": cls,
                                                       "bases": chain[1:]}
                continue
            owner = name if name in h.classes else cls
            out["mutators"][name] = {"class": cls, "path": path, "bases": chain[1:],
                                     "display_en": display_name(h, owner, h.english_name(stmt), "mutator")}

    for path, src in h.files.items():
        if '"Knocked Out"' not in src and '"Random Teleport' not in src:
            continue
        for m in re.finditer(r"\bconst\s+string\b", src):
            end = statement_end(src, m.end())
            for part in split_top(src[m.end():end]):
                mm = re.fullmatch(r"(\w+)\s*=\s*(.+)", part.strip(), re.S)
                v = unquote(mm.group(2)) if mm else None
                if v:
                    out["goals"][v] = {"const": mm.group(1), "path": path, "lists": []}
        for lm in re.finditer(r"List\s*<\s*string\s*>\s*(\w+)\s*=\s*new\s+List\s*<\s*string\s*>\s*(?:\(\s*\))?\s*\{", src):
            end = balanced(src, lm.end() - 1, "{", "}")
            for item in split_top(src[lm.end():end]):
                for g in out["goals"].values():
                    if g["const"] == item.strip().split(".")[-1]:
                        g["lists"].append(lm.group(1))

    for path, src in h.files.items():
        for dm in re.finditer(r"Dictionary\s*<\s*string\s*,\s*Type(\s*\[\s*\])?\s*>\s*(\w+)\s*=\s*new[^{;]*\{", src):
            end = balanced(src, dm.end() - 1, "{", "}")
            table = OrderedDict()
            for entry in split_top(src[dm.end():end]):
                entry = entry.strip()
                if not entry.startswith("{"):
                    continue
                kv = split_top(entry[1:entry.rfind("}")])
                if len(kv) != 2:
                    continue
                key = h.value(kv[0])
                if isinstance(key, dict):
                    continue
                table[key] = re.findall(r"typeof\(\s*(\w+)\s*\)", kv[1])
            if table:
                out["legacy"][dm.group(2)] = {"path": path, "map": table}

    for path, src in h.files.items():
        for lm in re.finditer(r"List\s*<\s*string\s*>\s*(\w*Objects\w*)\s*=\s*new\s+List\s*<\s*string\s*>\s*(?:\(\s*\))?\s*\{", src):
            end = balanced(src, lm.end() - 1, "{", "}")
            vals = []
            for item in split_top(src[lm.end():end]):
                v = h.value(item)
                if isinstance(v, dict):
                    v = item.strip().split(".")[-1]
                vals.append(v)
            out["object_lists"][lm.group(1)] = {"path": path, "objects": vals}
    return out


# --------------------------------------------------------------------------------------------- merge

def merge(per_ref: dict[str, dict]) -> dict:
    merged: dict = {}
    for section in ("traits", "items", "effects", "mutators", "goals"):
        acc: dict = OrderedDict()
        for ref in REFS:
            for key, info in per_ref[ref][section].items():
                entry = acc.setdefault(key, {"versions": []})
                entry["versions"].append(ref)
                entry.update(info)  # later refs win for descriptive fields
        for entry in acc.values():
            entry["in_head"] = "HEAD" in entry["versions"]
        merged[section] = OrderedDict(sorted(acc.items(), key=lambda kv: kv[0].lower()))
    legacy: dict = OrderedDict()
    for ref in REFS:
        for name, table in per_ref[ref]["legacy"].items():
            dst = legacy.setdefault(name, OrderedDict())
            for k, v in table["map"].items():
                dst[k] = v
    merged["legacy"] = legacy
    merged["object_lists"] = per_ref["HEAD"]["object_lists"]
    return merged


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--upstream", default=str(ROOT / "upstream" / "CCU"))
    ap.add_argument("--roguelibs", default=str(ROOT / "upstream" / "RogueLibs" / "RogueLibsCore"))
    ap.add_argument("--out", default=str(ROOT / "docs" / "ccu-interface.json"))
    a = ap.parse_args()
    repo = Path(a.upstream)
    rl_files = {str(p): p.read_text(encoding="utf-8-sig") for p in Path(a.roguelibs).rglob("*.cs")}
    rl_consts = const_table(rl_files)
    bunny = Path(a.upstream).parent / "BunnyLibs"
    if bunny.exists():
        rl_consts.update(const_table(read_tree(bunny, "HEAD")))

    per_ref, commits = {}, {}
    for ref in REFS:
        files = read_tree(repo, ref)
        consts = dict(rl_consts)
        consts.update(const_table(files))
        per_ref[ref] = harvest_ref(files, consts)
        commits[ref] = git(repo, "rev-parse", "--short", ref).strip()
        r = per_ref[ref]
        print(f"{ref:7} {commits[ref]} traits={len(r['traits'])} items={len(r['items'])} effects={len(r['effects'])}"
              f" mutators={len(r['mutators'])} goals={len(r['goals'])}"
              f" legacy={sum(len(t['map']) for t in r['legacy'].values())}")

    head_core = strip_comments(read_tree(repo, "HEAD").get("CCU/Core.cs", ""))
    guid = re.search(r'pluginGUID\s*=\s*"([^"]+)"', head_core)
    ver = re.search(r'pluginVersion\s*=\s*"([^"]+)"', head_core)
    result = OrderedDict(
        source=OrderedDict(repo="https://github.com/Freiling87/CCU", refs=commits,
                           note="Identifiers and formats only. No CCU code or text is copied (CCU's source has no licence file; its releases are CC BY-NC-ND 4.0)."),
        plugin=OrderedDict(guid=guid.group(1) if guid else None, head_version=ver.group(1) if ver else None),
        **merge(per_ref),
    )
    merge_extensions(result)
    Path(a.out).parent.mkdir(parents=True, exist_ok=True)
    Path(a.out).write_text(json.dumps(result, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print("wrote", a.out)


if __name__ == "__main__":
    main()
