"""Verify and promote existing native macOS CI packages to a draft release."""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
from pathlib import Path
from zipfile import ZipFile

if __package__:
    from .release_assets import release_metadata
else:
    from release_assets import release_metadata


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--artifact-root', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--commit', required=True)
    p.add_argument('--repository', required=True)
    p.add_argument('--run-id', type=int, required=True)
    p.add_argument('--release-id', type=int, required=True)
    a = p.parse_args()
    if not re.fullmatch(r'[0-9a-f]{40}', a.commit):
        raise ValueError('Expected a full commit SHA')
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', a.repository):
        raise ValueError('Invalid repository')
    tag = 'v' + a.version
    def api(path):
        return json.loads(subprocess.check_output(['gh', 'api', 'repos/' + a.repository + path], text=True))
    run = api('/actions/runs/' + str(a.run_id))
    metadata = release_metadata(a.version, tag, run['head_branch'])
    if run['status'] != 'completed' or run['conclusion'] != 'success' or run['head_sha'] != a.commit or run['path'] != '.github/workflows/macos.yml':
        raise ValueError('Expected a successful native macOS run for the tagged commit')
    ref = api('/git/ref/tags/' + tag)['object']
    if ref['type'] == 'tag':
        ref = api('/git/tags/' + ref['sha'])['object']
    if ref['type'] != 'commit' or ref['sha'] != a.commit:
        raise ValueError('Tag target differs from expected release commit')
    # Drafts are addressed by numeric ID; tag lookup can omit unpublished releases.
    release = api('/releases/' + str(a.release_id))
    if not release['draft'] or release['tag_name'] != tag or release['prerelease'] is not metadata['prerelease']:
        raise ValueError('Promotion requires an existing draft release in the expected channel')
    a.output.mkdir(parents=True, exist_ok=False)
    records = []
    for arch in ['arm64', 'x86_64']:
        reports = [f for f in a.artifact_root.rglob('build-report.json')
                   if json.loads(f.read_text())['architecture'] == arch]
        if len(reports) != 1:
            raise ValueError('Expected one native build report for ' + arch)
        report = json.loads(reports[0].read_text())
        if (report['version'], report['mode'], report['signature'], report['notarized'],
            set(report['archives_verified'])) != (a.version, 'preview', 'ad-hoc (not Developer ID)', False, {'zip', 'dmg'}):
            raise ValueError('Unexpected preview provenance')
        smoke = [f for f in a.artifact_root.rglob('app-smoke.json') if arch in str(f)]
        if len(smoke) != 1 or json.loads(smoke[0].read_text())['success'] is not True:
            raise ValueError('Native app smoke did not pass for ' + arch)
        zip_name = 'DeskPet-macOS-' + a.version + '-' + arch + '-preview.zip'
        with ZipFile(reports[0].parent / zip_name) as bundle:
            if bundle.testzip() is not None:
                raise ValueError('Corrupt Mac archive')
            infos = [n for n in bundle.namelist() if n.endswith('/Resources/build-info.json')]
            if len(infos) != 1 or json.loads(bundle.read(infos[0])) != dict(version=a.version, architecture=arch, mode='preview', commit=a.commit):
                raise ValueError('Bundled app has a different release identity')
        for suffix in ['dmg', 'zip']:
            name = 'DeskPet-macOS-' + a.version + '-' + arch + '-preview.' + suffix
            source = reports[0].parent / name
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            if report['artifacts'].get(name) != digest:
                raise ValueError('Build report hash differs: ' + name)
            shutil.copyfile(source, a.output / name)
            records.append(dict(name=name, sha256=digest, size=source.stat().st_size))
    manifest = dict(commit=a.commit, version=a.version, assets=records)
    (a.output / 'mac-upload-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    for record in records:
        subprocess.run(['gh', 'release', 'upload', tag, str(a.output / record['name']), '--repo', a.repository], check=True)
        print('Uploaded verified ' + record['name'], flush=True)


if __name__ == '__main__':
    main()
