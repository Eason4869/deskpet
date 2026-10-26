"""Read-only verification of the handoff, then copy reviewed holiday assets into authoring sources."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

IDS = ('e-bao', 'dafeiyu', 'marfu', 'xiaobei', 'gugugaga')


def import_assets(handoff, repository):
    handoff, repository = handoff.resolve(), repository.resolve()
    manifest = json.loads((handoff / 'delivery-manifest.json').read_text(encoding='utf-8-sig'))
    entries = manifest['files']
    for item in entries:
        path = (handoff / item['path']).resolve()
        if not path.is_relative_to(handoff) or not path.is_file() or path.stat().st_size != item['bytes']:
            raise ValueError('Handoff path or size changed: ' + item['path'])
        with path.open('rb') as source:
            if hashlib.file_digest(source, 'sha256').hexdigest() != item['sha256']:
                raise ValueError('Handoff digest changed: ' + item['path'])
    outfits, defaults, definitions = [], [], []
    for role in IDS:
        root = handoff / 'materials' / role
        catalog = json.loads((root / 'holidays/catalog.json').read_text(encoding='utf-8-sig'))
        if len(catalog['outfits']) != 10 or any(x['character_id'] != role for x in catalog['outfits']):
            raise ValueError('Expected exactly ten outfits per role')
        outfits += catalog['outfits']
        for source in sorted(root.rglob('*')):
            if not source.is_file():
                continue
            relative = source.relative_to(root)
            if relative.parts[0] == 'holidays':
                if relative.as_posix() == 'holidays/catalog.json':
                    continue
                if source.name == 'outfit.json':
                    definitions.append({'path': relative.relative_to('holidays').as_posix(), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
                target = repository / 'assets' / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
            else:
                target = repository / 'assets/complete-defaults' / role / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
                if source.suffix != '.png':
                    continue
                defaults.append({'id': role, 'path': relative.as_posix(), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
    target = repository / 'assets/holidays/catalog.json'
    target.write_text(json.dumps({'schema_version': 1, 'outfits': outfits}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    # Keep the existing embedded fallback only. Full resources are downloaded as a role package.
    fallback = [entry for entry in outfits if entry['character_id'] == 'e-bao' and entry['holiday_id'] == 'halloween']
    (repository / 'assets/official/halloween-catalog.json').write_text(json.dumps({'schema_version': 1, 'outfits': [x for x in outfits if x['holiday_id'] == 'halloween']}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (repository / 'assets/official/holiday-catalog.json').write_text(json.dumps({'schema_version': 1, 'outfits': fallback}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (repository / 'assets/official/reviewed-outfit-definitions.json').write_text(json.dumps({'schema_version': 1, 'files': definitions}, indent=2) + '\n', encoding='utf-8')
    (repository / 'assets/official/reviewed-default-hashes.json').write_text(json.dumps({'schema_version': 1, 'files': defaults}, indent=2) + '\n', encoding='utf-8')
    print(f'Verified {len(entries)} handoff files; imported {len(outfits)} outfits; recorded {len(defaults)} default PNG hashes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--handoff', type=Path, required=True)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    import_assets(args.handoff, args.repository)
