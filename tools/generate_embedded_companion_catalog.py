"""Derive the Ebao-only runtime catalog without changing the full authored catalog."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    original = json.loads((ROOT / 'assets/holidays/catalog.json').read_text(encoding='utf-8-sig'))
    selected = [item for item in original['outfits'] if item['character_id'] == 'e-bao']
    if not selected or not all(item['ready'] is True for item in selected):
        raise SystemExit('The embedded character must include every complete released outfit.')
    files = {'holiday-catalog.json': {'schema_version': original['schema_version'], 'outfits': selected},
             'bundled-catalog.json': []}
    for name, data in files.items():
        path = ROOT / 'assets/official' / name
        expected = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
        if args.check:
            if not path.is_file() or path.read_text(encoding='utf-8') != expected:
                raise SystemExit('Embedded companion catalog is stale: ' + name)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(expected, encoding='utf-8', newline='')
    print('PASS one complete embedded character catalog')


if __name__ == '__main__':
    main()
