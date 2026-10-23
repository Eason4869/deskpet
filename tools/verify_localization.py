"""Validate the single embedded catalog and source inventory, using Python stdlib.

Roslyn is used only to establish/review the initial inventory. CI does not need it.
"""
import argparse
import collections
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HAN = re.compile(r"[\u3400-\u9fff]")


def digest(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def string_tokens(source):
    """Yield (start,end,value,kind), excluding comments and character literals.

    Interpolated/raw literals are surfaced explicitly for review, never silently
    treated as ordinary strings. Both preprocessor branches remain in the audit.
    """
    i = 0
    escapes = {"0": "\0", "a": "\a", "b": "\b", "f": "\f", "n": "\n", "r": "\r", "t": "\t", "v": "\v"}
    while i < len(source):
        if source.startswith("//", i):
            j = source.find("\n", i); i = len(source) if j < 0 else j + 1; continue
        if source.startswith("/*", i):
            j = source.find("*/", i + 2)
            if j < 0: raise ValueError("Unterminated C# comment")
            i = j + 2; continue
        if source[i] == "'":
            i += 1
            while i < len(source):
                if source[i] == "\\": i += 2
                elif source[i] == "'": i += 1; break
                else: i += 1
            continue
        prefix = next((p for p in ('$@"', '@$"', '@"', '$"', '"') if source.startswith(p, i)), None)
        if prefix is None: i += 1; continue
        start = i; i += len(prefix); value = []; verbatim = "@" in prefix; interpolated = "$" in prefix
        if source.startswith('""', i):
            quotes = 1
            while i < len(source) and source[i] == '"': quotes += 1; i += 1
            close = source.find('"' * quotes, i)
            if close < 0: raise ValueError("Unterminated raw C# literal")
            yield start, close + quotes, source[i:close], "raw"
            i = close + quotes; continue
        while i < len(source):
            c = source[i]; i += 1
            if c == '"':
                if verbatim and i < len(source) and source[i] == '"': value.append('"'); i += 1; continue
                break
            if c == "\\" and not verbatim:
                c = source[i]; i += 1
                if c in "uU":
                    count = 4 if c == "u" else 8
                    value.append(chr(int(source[i:i + count], 16))); i += count
                elif c == "x":
                    match = re.match(r"[0-9a-fA-F]{1,4}", source[i:])
                    if not match: raise ValueError("Invalid C# hex escape")
                    value.append(chr(int(match[0], 16))); i += len(match[0])
                else: value.append(escapes.get(c, c))
            else: value.append(c)
        else: raise ValueError("Unterminated C# string")
        yield start, i, "".join(value), "interpolated" if interpolated else "literal"


def load_catalog(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result: raise ValueError("Duplicate resource key: " + key)
            result[key] = value
        return result
    result = json.loads(text, object_pairs_hook=pairs)
    if not isinstance(result, dict): raise ValueError("Catalog must be an object")
    for key, value in result.items():
        if not key.strip() or not isinstance(value, str): raise ValueError("Invalid catalog entry: " + key)
        if not re.fullmatch(r"[a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*)+", key): raise ValueError("Invalid resource key: " + key)
    return result


def placeholders(template):
    indexes = set(); i = 0
    while i < len(template):
        if template[i:i + 2] in ("{{", "}}"): i += 2; continue
        if template[i] == "{":
            match = re.match(r"\{([0-9]+)(?:,-?\d+)?(?::[^{}]*)?\}", template[i:])
            if not match: raise ValueError("Invalid numbered format template: " + template)
            indexes.add(int(match[1])); i += len(match[0]); continue
        if template[i] == "}": raise ValueError("Unescaped closing brace: " + template)
        i += 1
    return indexes


def production_files():
    return sorted(list((ROOT / "src").rglob("*.cs")) + [p for folder in ["desktop/DeskPet.Desktop", "desktop/DeskPet.Platform.Mac"] for p in (ROOT / folder).rglob("*.cs") if not {"obj", "bin", "Tests"}.intersection(p.parts)])


def check(phase="complete"):
    catalog_path = ROOT / "assets/locales/zh_CN.json"
    raw = catalog_path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"): raise ValueError("Catalog must be UTF-8 without BOM")
    catalog = load_catalog(raw.decode("utf-8"))
    inventory = [json.loads(line) for line in (ROOT / "docs/localization/migration-inventory.jsonl").read_text(encoding="utf-8").splitlines()]
    errors = []; references = collections.Counter(); residual = collections.Counter()
    for path in production_files():
        relative = path.relative_to(ROOT).as_posix()
        source = path.read_text(encoding="utf-8-sig")
        for _, _, value, kind in string_tokens(source):
            if HAN.search(value): residual[(relative, digest(value))] += 1
        for method, arguments in re.findall(r"TextResources\.(Get|Format|StartupMessage)\(([^\n;]*?)\)", source):
            keys = re.findall(r'"([a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*)+)"', arguments)
            if not keys: continue
            key = keys[0]; references[key] += 1
            if key not in catalog: errors.append(relative + ": missing key " + key)
    expected = collections.Counter()
    for item in inventory:
        if item["decision"] == "exclude":
            if not item.get("exclusion_reason") or not item.get("reviewed"): errors.append("Unreviewed exclusion: " + item["file"])
            if HAN.search(item["value"]): expected[(item["file"], digest(item["value"]))] += 1
        elif item["decision"] == "migrate":
            if item["resource_key"] not in catalog: errors.append("Inventory resource absent: " + item["resource_key"])
        else: errors.append("Unclassified candidate")
    if phase == "complete":
        for (file, hashed), count in (residual - expected).items(): errors.append(file + ": new/unclassified C# text " + hashed[:12] + " x" + str(count))
        for (file, hashed), count in (expected - residual).items(): errors.append(file + ": excluded text changed " + hashed[:12] + " x" + str(count))
        baseline = json.loads((ROOT / "tests/fixtures/localization/text-baseline.json").read_text(encoding="utf-8"))
        for item in baseline["resources"]:
            key = item["key"]
            if catalog.get(key) != item["expected"]: errors.append("Original text drift: " + key)
            if item["format"]:
                try:
                    actual = placeholders(catalog[key])
                    if actual != set(range(item["argument_count"])): errors.append("Placeholder mismatch: " + key)
                except ValueError as error: errors.append(str(error))
        for key in catalog:
            if not references[key]: errors.append("Unreferenced key: " + key)
            if not catalog[key]: errors.append("Unexplained empty resource: " + key)
    return {"success": not errors, "phase": phase, "resources": len(catalog), "candidates": len(inventory), "migrated": sum(i["decision"] == "migrate" for i in inventory), "excluded": sum(i["decision"] == "exclude" for i in inventory), "errors": errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["check", "inventory"])
    parser.add_argument("--phase", choices=["inventory", "complete"], default="complete")
    parser.add_argument("--baseline")
    parser.add_argument("--report")
    args = parser.parse_args()
    if args.command == "inventory":
        print("Initial compiler inventory: dotnet run --project tools/localization/Extract.csproj -- <repo>.")
        print("Review every candidate before updating the committed migration inventory; never auto-approve exclusions.")
        return 0
    result = check(args.phase)
    if args.report:
        path = ROOT / args.report; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["success"] else 1


if __name__ == "__main__": raise SystemExit(main())
