"""Bind resource-only publication to its immutable source and native-checked bytes."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import subprocess

IDS = {'e-bao', 'dafeiyu', 'marfu', 'xiaobei', 'gugugaga'}
REPOSITORY = 'Eason4869/deskpet'
ROOT = Path(__file__).resolve().parents[1]


def identity(root, tag):
    source = (root / 'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')
    version = re.search(r'DisplayVersion="([^"]+)"', source)[1]
    if not re.fullmatch(r'(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-dev\.[1-9][0-9]*)?', version) or tag != 'companions-v' + version:
        raise ValueError('Resource tag must match the canonical source version')
    def git(*args):
        return subprocess.check_output(['git', *args], cwd=root, text=True).strip()
    commit = git('rev-parse', tag + '^{commit}')
    if commit != git('rev-parse', 'HEAD') or git('status', '--porcelain'):
        raise ValueError('Publication requires a clean checkout of the exact resource tag')
    return {'version': version, 'tag': tag, 'commit': commit, 'prerelease': '-dev.' in version}


def prepare(root, tag, directory):
    result = identity(root, tag)
    version = result['version']
    index_name = 'DeskPet-Companions-' + version + '.json'
    directory = directory.resolve()
    index_path = directory / index_name
    index = json.loads(index_path.read_text(encoding='utf-8'))
    report = json.loads((directory.parent / 'native-validation.json').read_text(encoding='utf-8'))
    if index.get('schema_version') != 1 or report.get('schema_version') != 1 or report.get('status') != 'passed':
        raise ValueError('Complete index and successful native report are required')
    entries, checked = index['companions'], report['companions']
    if len(entries) != 5 or {x['id'] for x in entries} != IDS or len(checked) != 5 or {x['id'] for x in checked} != IDS:
        raise ValueError('Both index and native report must cover exactly five roles')
    checks = {x['id']: x for x in checked}
    assets = []
    for entry in entries:
        name = 'DeskPet-Companion-' + entry['id'] + '-' + version + '.zip'
        if entry['asset_name'] != name or entry['version'] != version or entry['min_app_version'] != version:
            raise ValueError('Package identity differs from the tagged source')
        path = directory / name
        if path.is_symlink() or not path.is_file():
            raise ValueError('A regular package file is required')
        digest, size = hashlib.sha256(path.read_bytes()).hexdigest(), path.stat().st_size
        check = checks[entry['id']]
        if size <= 0 or size > 256 * 1024 * 1024 or entry['sha256'] != digest or entry['size'] != size or check.get('sha256') != digest or check.get('size') != size or check.get('nativeValidated') is not True or check.get('finalRootValidated') is not True:
            raise ValueError('Package changed after authoring or native validation')
        assets.append({'name': name, 'sha256': digest, 'size': size})
    if index_path.is_symlink() or index_path.stat().st_size > 1024 * 1024:
        raise ValueError('Invalid index file')
    assets.append({'name': index_name, 'sha256': hashlib.sha256(index_path.read_bytes()).hexdigest(), 'size': index_path.stat().st_size})
    if {p.name for p in directory.iterdir()} != {x['name'] for x in assets}:
        raise ValueError('Resource publication must contain only five packages and their index')
    result['assets'] = assets
    return result


def verify_remote(expected, remote, draft):
    tag = expected['tag']
    if remote.get('tag_name') != tag or remote.get('html_url') != 'https://github.com/' + REPOSITORY + '/releases/tag/' + tag or remote.get('draft') is not draft or remote.get('prerelease') is not expected['prerelease']:
        raise ValueError('Unexpected remote release identity or publication state')
    assets = remote.get('assets', [])
    if len(assets) != len(expected['assets']) or len({x['name'] for x in assets}) != len(assets):
        raise ValueError('Unexpected remote asset set')
    by_name = {x['name']: x for x in assets}
    for asset in expected['assets']:
        actual = by_name.get(asset['name'], {})
        url = 'https://github.com/' + REPOSITORY + '/releases/download/' + tag + '/' + asset['name']
        if actual.get('digest') != 'sha256:' + asset['sha256'] or actual.get('size') != asset['size'] or actual.get('browser_download_url') != url or actual.get('state') != 'uploaded':
            raise ValueError('Remote asset bytes or URL differ: ' + asset['name'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--artifacts', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--remote', type=Path)
    parser.add_argument('--published', action='store_true')
    args = parser.parse_args()
    expected = prepare(ROOT, args.tag, args.artifacts) if args.artifacts else identity(ROOT, args.tag)
    if args.remote:
        if not args.artifacts:
            parser.error('--remote requires --artifacts')
        verify_remote(expected, json.loads(args.remote.read_text(encoding='utf-8-sig')), not args.published)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(expected, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
