"""One-off hosted publication of already verified immutable dev artifacts."""
from pathlib import Path
import hashlib
import json
import re
import subprocess

REPO = 'Eason4869/deskpet'
COMMIT = '594363af7c2cf313ea11ff5890c0fab8d42d8c26'
VERSION = '0.10.1-dev.2'
TAG = 'v' + VERSION
ROOT = Path.cwd()


def run(*args):
    return subprocess.check_output(args, text=True).strip()


def api(endpoint):
    return json.loads(run('gh', 'api', 'repos/' + REPO + '/' + endpoint))


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def tag_commit():
    ref = api('git/ref/tags/' + TAG)['object']
    if ref['type'] == 'tag':
        ref = api('git/tags/' + ref['sha'])['object']
    require(ref['type'] == 'commit', 'Tag must resolve to a commit')
    return ref['sha']


def main():
    for run_id, path in [(39599762160, 'check.yml'), (39599763511, 'macos.yml'),
                         (39602006292, 'windows-release.yml')]:
        result = api('actions/runs/' + str(run_id))
        require((result['head_sha'], result['head_branch'], result['status'], result['conclusion'], result['path'])
                == (COMMIT, 'dev', 'completed', 'success', '.github/workflows/' + path),
                'Workflow provenance mismatch: ' + str(run_id))
    require(tag_commit() == COMMIT, 'Remote tag identity mismatch')
    require(api('releases/latest')['tag_name'] == 'v0.10.0', 'Stable latest changed')
    run('git', 'fetch', 'origin', 'main', 'dev', '--tags')
    run('python', 'tools/verify_windows_release.py', '--tag', TAG, '--latest', 'v0.10.0',
        '--output', 'work/expected-metadata.json')
    expected = json.loads(Path('work/expected-metadata.json').read_text())
    windows = Path('work/windows')
    require(json.loads((windows / 'release-metadata.json').read_text()) == expected,
            'Windows artifact metadata mismatch')
    names = {'DeskPet-Windows-App-' + VERSION + '.exe', 'DeskPet-Windows-Setup-' + VERSION + '.exe',
             'DeskPet-Windows-' + VERSION + '.zip', 'DeskPet-Source-' + VERSION + '.zip',
             'DeskPet-Skin-Template-v2-' + VERSION + '.zip'}
    names.update(name + '-HD-Skin-' + VERSION + '.zip'
                 for name in ['Ebao', 'Dafeiyu', 'Marfu', 'Xiaobei', 'gugugaga'])
    records = {}
    for line in (windows / 'SHA256.txt').read_text(encoding='utf-8-sig').splitlines():
        match = re.fullmatch(r'([0-9a-f]{64})  ([^/\\]+)', line)
        require(match is not None, 'Unsafe or malformed manifest path')
        sha, name = match.groups()
        require(name not in records and name in names, 'Unexpected or duplicate Windows asset')
        file = windows / name
        require(digest(file) == sha, 'Windows digest mismatch: ' + name)
        records[name] = dict(sha256=sha, size=file.stat().st_size)
    require(set(records) == names, 'Windows manifest is incomplete')
    notes = Path(__file__).resolve().parents[1] / 'release-notes' / (TAG + '.md')
    # Resume only the empty draft created by this publication's first hosted run.
    # GitHub's tag lookup can omit drafts; always address this known draft by ID.
    release = api('releases/419560067')
    require(release['draft'] and release['prerelease'] and not release['assets']
            and release['tag_name'] == TAG and release['name'] == 'DeskPet ' + VERSION
            and release['body'].replace('\r\n', '\n').strip() == notes.read_text().replace('\r\n', '\n').strip(),
            'Expected the exact empty draft owned by the first publication run')
    release_id = release['id']
    print('Created release draft ' + str(release_id), flush=True)
    run('gh', 'release', 'upload', TAG, '--repo', REPO, *(str(windows / name) for name in sorted(names)))
    run('python', 'tools/promote_macos_release.py', '--artifact-root', 'work/macos', '--output', 'work/promoted',
        '--version', VERSION, '--commit', COMMIT, '--repository', REPO,
        '--run-id', '39599763511', '--release-id', str(release_id))
    mac = json.loads(Path('work/promoted/mac-upload-manifest.json').read_text())
    require(mac['version'] == VERSION and mac['commit'] == COMMIT and len(mac['assets']) == 4,
            'Mac upload identity mismatch')
    for record in mac['assets']:
        name = record['name']
        require(re.fullmatch(r'DeskPet-macOS-0\.10\.1-dev\.2-(arm64|x86_64)-preview\.(dmg|zip)', name)
                is not None and name not in records, 'Unexpected Mac asset')
        records[name] = dict(sha256=record['sha256'], size=record['size'])
    checksums = Path('work/SHA256.txt')
    checksums.write_text(''.join(records[name]['sha256'] + '  ' + name + '\n' for name in sorted(records)), encoding='utf-8')
    run('gh', 'release', 'upload', TAG, '--repo', REPO, str(checksums))
    records['SHA256.txt'] = dict(sha256=digest(checksums), size=checksums.stat().st_size)

    def verify_assets(release):
        require(release['tag_name'] == TAG and release['prerelease'] and len(release['assets']) == 15,
                'Release channel or asset count mismatch')
        actual = {asset['name']: asset for asset in release['assets']}
        require(set(actual) == set(records), 'Remote asset names mismatch')
        for name, info in records.items():
            asset = actual[name]
            require(asset['state'] == 'uploaded' and asset['digest'] == 'sha256:' + info['sha256']
                    and asset['size'] == info['size'], 'Remote digest or size mismatch: ' + name)

    release = api('releases/' + str(release_id))
    require(release['draft'], 'Expected an unpublished draft')
    verify_assets(release)
    require(tag_commit() == COMMIT and api('releases/latest')['tag_name'] == 'v0.10.0',
            'Release identity changed before publication')
    run('gh', 'release', 'edit', TAG, '--repo', REPO, '--draft=false', '--prerelease', '--latest=false')
    release = api('releases/' + str(release_id))
    require(not release['draft'] and api('releases/latest')['tag_name'] == 'v0.10.0', 'Published channel mismatch')
    verify_assets(release)
    Path('work/published-release.json').write_text(json.dumps(release, indent=2), encoding='utf-8')
    print('Published ' + release['html_url'] + '; all 15 remote hashes verified', flush=True)


if __name__ == '__main__':
    main()
