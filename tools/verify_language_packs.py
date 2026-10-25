"""Audit all shipped translations against the independent Chinese resource contract."""
import argparse
import hashlib
import json
from pathlib import Path

from verify_localization import load_catalog, placeholders

ROOT = Path(__file__).resolve().parents[1]


def audit(directory):
    files = sorted(directory.glob("*.json"))
    canonical = load_catalog((directory / "zh_CN.json").read_text(encoding="utf-8"))
    baseline = json.loads((ROOT / "tests/fixtures/localization/text-baseline.json").read_text(encoding="utf-8"))
    feature = json.loads((ROOT / "tests/fixtures/localization/feature-text-baseline.json").read_text(encoding="utf-8"))
    formatted = {item["key"] for item in baseline["resources"] + feature['resources'] if item["format"]}
    errors, packs = [], []
    for path in files:
        try:
            raw = path.read_bytes()
            entries = load_catalog(raw.decode("utf-8"))
            missing = sorted(set(canonical) - set(entries))
            extra = sorted(set(entries) - set(canonical))
            if missing:
                errors.append(f"{path.stem}: missing keys: {missing}")
            if extra:
                errors.append(f"{path.stem}: unknown keys: {extra}")
            for key, value in entries.items():
                if not isinstance(value, str) or not value.strip() or "\ufffd" in value:
                    errors.append(f"{path.stem}: invalid/empty translation: {key}")
                if key in formatted and key in canonical and placeholders(value) != placeholders(canonical[key]):
                    errors.append(f"{path.stem}: changed format arguments: {key}")
            packs.append(dict(locale=path.stem, keys=len(entries), missing=len(missing), extra=len(extra), sha256=hashlib.sha256(raw).hexdigest()))
        except (ValueError, UnicodeError) as error:
            errors.append(f"{path.name}: {error}")
    expected = {"zh_CN", "zh_TW", "en_US", "ja_JP", "ko_KR", "fr_FR", "de_DE", "hi_IN", "id_ID", "it_IT", "pt_BR", "es_419", "es_ES"}
    if {path.stem for path in files} != expected:
        errors.append("Expected exactly the thirteen accepted language packs.")
    return dict(success=not errors, canonical_keys=len(canonical), packs=packs, errors=errors)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=ROOT / "assets/locales")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    result = audit(args.directory)
    text = json.dumps(result, ensure_ascii=True, indent=2)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0 if result["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
