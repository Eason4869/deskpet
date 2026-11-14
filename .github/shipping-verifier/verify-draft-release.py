"""Verify the actual draft manifests and shipping Windows ZIP before publication."""
import argparse
import hashlib
import json
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--release', type=Path, required=True)
p.add_argument('--assets', type=Path, required=True)
p.add_argument('--commit', required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
r = json.loads(a.release.read_text(encoding='utf-8'))
assert r['draft'] and r['prerelease'] and r['tag_name'] == 'v1.0.4-dev.1'
assert r['target_commitish'] == a.commit
assets = {x['name']: x for x in r['assets']}
assert len(assets) == len(r['assets'])
checked = {}
for line in (a.assets / 'SHA256.txt').read_text(encoding='utf-8-sig').splitlines():
    if not line.strip():
        continue
    digest, relative = line.split('  ', 1)
    relative = relative.lstrip('*').replace('\\', '/')
    assert relative and not relative.startswith('/') and ':' not in relative and '..' not in relative.split('/')
    name = relative.rsplit('/', 1)[-1]
    assert name not in checked and assets[name]['digest'] == 'sha256:' + digest
    checked[name] = digest
m = json.loads((a.assets / 'mac-upload-manifest.json').read_text(encoding='utf-8'))
assert m['commit'] == a.commit and m['version'] == '1.0.4-dev.1'
for item in m['assets']:
    name = item['name']
    assert name not in checked and assets[name]['digest'] == 'sha256:' + item['sha256']
    assert assets[name]['size'] == item['size']
    checked[name] = item['sha256']
assert set(assets) == set(checked) | {'SHA256.txt', 'mac-upload-manifest.json'}
for name in ('SHA256.txt', 'mac-upload-manifest.json', 'DeskPet-Windows-1.0.4-dev.1.zip'):
    path = a.assets / name
    with path.open('rb') as f:
        digest = hashlib.file_digest(f, 'sha256').hexdigest()
    assert assets[name]['digest'] == 'sha256:' + digest
    assert path.stat().st_size == assets[name]['size']
    checked[name] = digest
runtime = a.assets / 'DeskPet-Windows-1.0.4-dev.1.zip'
assert runtime.stat().st_size <= 300 * 1024 * 1024
a.output.write_text(json.dumps(dict(release_id=r['id'], commit=a.commit, assets=checked,
    runtime_bytes=runtime.stat().st_size, full_download_hash_verified=[
        'SHA256.txt', 'mac-upload-manifest.json', runtime.name]), indent=2) + '\n', encoding='utf-8')
print('PASS draft identity, all', len(assets), 'asset digests, full shipping ZIP hash and legacy 300 MiB size limit')
