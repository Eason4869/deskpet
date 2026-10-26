"""Verify the reviewed authoring inventory and unchanged handoff PNG bytes."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IDS = {'e-bao', 'dafeiyu', 'marfu', 'xiaobei', 'gugugaga'}
HOLIDAYS = {'halloween', 'christmas', 'spring-festival', 'lantern', 'valentines-day', 'qingming', 'dragon-boat', 'mid-autumn', 'national-day', 'qixi'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def verify(artifacts=None):
    outfits = read(ROOT / 'assets/holidays/catalog.json')['outfits']
    assert len(outfits) == 50
    for role in IDS:
        entries = [x for x in outfits if x['character_id'] == role]
        assert len(entries) == 10 and {x['holiday_id'] for x in entries} == HOLIDAYS
        assert len({x['outfit_id'] for x in entries}) == 10
        for entry in entries:
            definition = read(ROOT / 'assets/holidays' / entry['definition'])
            for key in ('character_id', 'holiday_id', 'outfit_id', 'name', 'version'):
                assert definition[key] == entry[key]
            clips = {(x['action'], x['variant']): x for x in definition['clips']}
            assert len(clips) == (28 if role in {'e-bao', 'gugugaga'} else 27)
            for action in ('petting', 'poked', 'jumping'):
                clip = clips[action, 'default']
                assert clip['frame_count'] == 8 and clip['loop'] is False
            assert clips['idle', 'default']['loop'] is True
            assert clips['petting', 'default']['path'] != clips['poked', 'default']['path']
            for clip in list(clips.values()) + [definition['gaze'], definition['easter_egg']]:
                path = ROOT / 'assets/holidays' / clip['path']
                assert path.is_file() and path.resolve().is_relative_to((ROOT / 'assets/holidays').resolve())
    defaults = read(ROOT / 'assets/official/reviewed-default-hashes.json')['files']
    assert len(defaults) == 152 and {x['id'] for x in defaults} == IDS
    for item in defaults:
        assert digest(ROOT / 'assets/complete-defaults' / item['id'] / item['path']) == item['sha256']
    if artifacts:
        manifest = read(artifacts / 'delivery-manifest.json')
        assert manifest['packages'] == 5 and manifest['appearances'] == 55
        for item in manifest['files']:
            path = artifacts / item['path']
            assert path.resolve().is_relative_to(artifacts.resolve())
            assert path.stat().st_size == item['bytes'] and digest(path) == item['sha256']
    print('PASS five complete roles / fifty holiday outfits / 152 unchanged default PNGs')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifacts', type=Path)
    verify(parser.parse_args().artifacts)
