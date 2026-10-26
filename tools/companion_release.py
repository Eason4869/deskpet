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
POLICY = json.loads((ROOT / 'assets/official/companion-package-policy.json').read_text(encoding='utf-8'))



def file_digest(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()

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
    registry_path = root / 'assets/official/companion-versions.json'
    registry = json.loads(registry_path.read_text(encoding='utf-8'))['companions'] if registry_path.exists() else None
    assets = []
    for entry in entries:
        material = registry[entry['id']] if registry is not None else {'pack_version': version, 'min_app_version': version}
        name = 'DeskPet-Companion-' + entry['id'] + '-' + material['pack_version'] + '.zip'
        if entry['asset_name'] != name or entry['version'] != material['pack_version'] or entry['min_app_version'] != material['min_app_version']:
            raise ValueError('Package identity differs from the tagged source')
        path = directory / name
        if path.is_symlink() or not path.is_file():
            raise ValueError('A regular package file is required')
        size = path.stat().st_size
        if size <= 0 or size > POLICY['maximum_archive_bytes']:
            raise ValueError('Package exceeds the shared archive size policy')
        digest = file_digest(path)
        check = checks[entry['id']]
        if entry['sha256'] != digest or entry['size'] != size or check.get('sha256') != digest or check.get('size') != size or check.get('nativeValidated') is not True or check.get('finalRootValidated') is not True:
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
    if remote.get('tag_name') != tag or remote.get('draft') is not draft or remote.get('prerelease') is not expected['prerelease']:
        raise ValueError('Unexpected remote release identity or publication state')
    prefix = 'https://github.com/' + REPOSITORY + '/releases/'
    download_tag = tag
    html_url = remote.get('html_url')
    if html_url != prefix + 'tag/' + tag:
        temporary = re.fullmatch(re.escape(prefix + 'tag/') + r'(untagged-[a-f0-9]{16,64})', html_url) if isinstance(html_url, str) else None
        if draft is not True or temporary is None:
            raise ValueError('Unexpected remote release URL')
        download_tag = temporary[1]
    assets = remote.get('assets', [])
    if len(assets) != len(expected['assets']) or len({x['name'] for x in assets}) != len(assets):
        raise ValueError('Unexpected remote asset set')
    by_name = {x['name']: x for x in assets}
    for asset in expected['assets']:
        actual = by_name.get(asset['name'], {})
        url = prefix + 'download/' + download_tag + '/' + asset['name']
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
