"""Resource publication guards; owned repositories and simulated GitHub responses."""
from pathlib import Path
import copy
import hashlib
import importlib.util
import json
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('companion_release', ROOT / 'tools/companion_release.py')
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
VERSION = '0.10.3-dev.1'
TAG = 'companions-v' + VERSION


class CompanionReleaseTests(unittest.TestCase):
    def test_exact_tag_and_clean_source_required(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp:
            root = Path(temp)
            (root / 'src').mkdir()
            (root / 'src/ReleaseInfo.cs').write_text('DisplayVersion="' + VERSION + '";', encoding='utf-8')
            def git(*args):
                return subprocess.check_output(['git', *args], cwd=root, stderr=subprocess.STDOUT, text=True)
            git('init', '-q')
            git('config', 'user.name', 'DeskPet fixture')
            git('config', 'user.email', 'fixture@example.invalid')
            git('add', '.')
            git('commit', '-qm', 'Owned publication fixture')
            git('tag', TAG)
            self.assertTrue(release.identity(root, TAG)['prerelease'])
            with self.assertRaises(ValueError):
                release.identity(root, 'v' + VERSION)
            (root / 'dirty').write_text('change')
            with self.assertRaises(ValueError):
                release.identity(root, TAG)
            git('add', '.')
            git('commit', '-qm', 'Different source')
            with self.assertRaises(ValueError):
                release.identity(root, TAG)

    def fixture(self, root):
        directory = root / 'artifacts'
        directory.mkdir()
        entries, checks = [], []
        for role in sorted(release.IDS):
            name = 'DeskPet-Companion-' + role + '-' + VERSION + '.zip'
            payload = ('independently validated fixture ' + role).encode()
            (directory / name).write_bytes(payload)
            digest = hashlib.sha256(payload).hexdigest()
            entries.append({'id': role, 'version': VERSION, 'min_app_version': VERSION, 'asset_name': name, 'sha256': digest, 'size': len(payload)})
            checks.append({'id': role, 'sha256': digest, 'size': len(payload), 'nativeValidated': True, 'finalRootValidated': True})
        index = directory / ('DeskPet-Companions-' + VERSION + '.json')
        index.write_text(json.dumps({'schema_version': 1, 'companions': entries}), encoding='utf-8')
        report = root / 'native-validation.json'
        report.write_text(json.dumps({'schema_version': 1, 'status': 'passed', 'companions': checks}), encoding='utf-8')
        return directory, index, report

    def test_native_receipt_binds_all_bytes_and_roles(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG, 'prerelease': True, 'commit': 'fixture'}):
            directory, index, report = self.fixture(Path(temp))
            self.assertEqual(6, len(release.prepare(ROOT, TAG, directory)['assets']))
            original = report.read_bytes()
            data = json.loads(original)
            for mutation in ('digest', 'final-root', 'missing-role'):
                changed = copy.deepcopy(data)
                if mutation == 'digest': changed['companions'][0]['sha256'] = '0' * 64
                if mutation == 'final-root': changed['companions'][0]['finalRootValidated'] = False
                if mutation == 'missing-role': changed['companions'].pop()
                report.write_text(json.dumps(changed), encoding='utf-8')
                with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                    release.prepare(ROOT, TAG, directory)
            report.write_bytes(original)
            package = next(directory.glob('*.zip'))
            package.write_bytes(package.read_bytes() + b'tampered')
            with self.assertRaises(ValueError):
                release.prepare(ROOT, TAG, directory)

    def test_rejects_application_assets_and_index_path_escape(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG}):
            directory, index, _ = self.fixture(Path(temp))
            extra = directory / 'application.exe'
            extra.write_bytes(b'not a resource')
            with self.assertRaises(ValueError): release.prepare(ROOT, TAG, directory)
            extra.unlink()
            data = json.loads(index.read_text())
            data['companions'][0]['asset_name'] = '../outside.zip'
            index.write_text(json.dumps(data))
            with self.assertRaises(ValueError): release.prepare(ROOT, TAG, directory)

    def test_remote_exact_digest_url_state_and_asset_set(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG, 'prerelease': True}):
            directory, _, _ = self.fixture(Path(temp))
            expected = release.prepare(ROOT, TAG, directory)
            remote = {'tag_name': TAG, 'html_url': 'https://github.com/' + release.REPOSITORY + '/releases/tag/' + TAG, 'draft': True, 'prerelease': True, 'assets': []}
            for asset in expected['assets']:
                remote['assets'].append({'name': asset['name'], 'digest': 'sha256:' + asset['sha256'], 'size': asset['size'], 'state': 'uploaded', 'browser_download_url': 'https://github.com/' + release.REPOSITORY + '/releases/download/' + TAG + '/' + asset['name']})
            release.verify_remote(expected, remote, True)
            for key, value in [('digest', None), ('size', 1), ('state', 'starter'), ('browser_download_url', 'https://example.invalid/file')]:
                changed = copy.deepcopy(remote)
                changed['assets'][0][key] = value
                with self.subTest(key=key), self.assertRaises(ValueError): release.verify_remote(expected, changed, True)
            changed = copy.deepcopy(remote)
            changed['assets'].append(changed['assets'][0])
            with self.assertRaises(ValueError): release.verify_remote(expected, changed, True)
            with self.assertRaises(ValueError): release.verify_remote(expected, remote, False)
            remote['draft'] = False
            release.verify_remote(expected, remote, False)


if __name__ == '__main__':
    unittest.main()
