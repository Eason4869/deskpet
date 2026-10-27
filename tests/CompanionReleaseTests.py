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
    def test_independent_material_version_is_bound_to_registry(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp:
            root=Path(temp)
            directory,index,report=self.fixture(root)
            registry={'schema_version':1,'companions':{role:{'pack_version':VERSION,'min_app_version':VERSION} for role in release.IDS}}
            (root/'assets/official').mkdir(parents=True)
            (root/'assets/official/companion-versions.json').write_text(json.dumps(registry))
            new='0.11.1-dev.1'
            index.rename(directory/('DeskPet-Companions-'+new+'.json'))
            with patch.object(release,'identity',return_value={'version':new,'tag':'companions-v'+new,'commit':'a'*40,'prerelease':True}):
                self.assertEqual(6,len(release.prepare(root,'companions-v'+new,directory)['assets']))
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

    def test_complete_package_above_old_limit_can_be_published(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG, 'prerelease': True, 'commit': 'fixture'}):
            directory, index, report = self.fixture(Path(temp))
            catalog = json.loads(index.read_text())
            receipt = json.loads(report.read_text())
            entry = catalog['companions'][0]
            package = directory / entry['asset_name']
            with package.open('wb') as stream:
                stream.truncate(257 * 1024 * 1024)
            digest = hashlib.sha256(package.read_bytes()).hexdigest()
            entry.update(size=package.stat().st_size, sha256=digest)
            receipt['companions'][0].update(size=entry['size'], sha256=digest)
            index.write_text(json.dumps(catalog), encoding='utf-8')
            report.write_text(json.dumps(receipt), encoding='utf-8')
            self.assertEqual(6, len(release.prepare(directory.parent, TAG, directory)['assets']))
            for size in (release.POLICY['maximum_archive_bytes'],):
                with package.open('wb') as stream:
                    stream.truncate(size)
                digest = release.file_digest(package)
                entry.update(size=size, sha256=digest)
                receipt['companions'][0].update(size=size, sha256=digest)
                index.write_text(json.dumps(catalog), encoding='utf-8')
                report.write_text(json.dumps(receipt), encoding='utf-8')
                self.assertEqual(6, len(release.prepare(directory.parent, TAG, directory)['assets']))
            with package.open('wb') as stream:
                stream.truncate(release.POLICY['maximum_archive_bytes'] + 1)
            with patch.object(release, 'file_digest', side_effect=AssertionError('must reject before reading oversized bytes')):
                with self.assertRaises(ValueError):
                    release.prepare(directory.parent, TAG, directory)

    def test_native_receipt_binds_all_bytes_and_roles(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG, 'prerelease': True, 'commit': 'fixture'}):
            directory, index, report = self.fixture(Path(temp))
            self.assertEqual(6, len(release.prepare(directory.parent, TAG, directory)['assets']))
            original = report.read_bytes()
            data = json.loads(original)
            for mutation in ('digest', 'final-root', 'missing-role'):
                changed = copy.deepcopy(data)
                if mutation == 'digest': changed['companions'][0]['sha256'] = '0' * 64
                if mutation == 'final-root': changed['companions'][0]['finalRootValidated'] = False
                if mutation == 'missing-role': changed['companions'].pop()
                report.write_text(json.dumps(changed), encoding='utf-8')
                with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                    release.prepare(directory.parent, TAG, directory)
            report.write_bytes(original)
            package = next(directory.glob('*.zip'))
            package.write_bytes(package.read_bytes() + b'tampered')
            with self.assertRaises(ValueError):
                release.prepare(directory.parent, TAG, directory)

    def test_rejects_application_assets_and_index_path_escape(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG}):
            directory, index, _ = self.fixture(Path(temp))
            extra = directory / 'application.exe'
            extra.write_bytes(b'not a resource')
            with self.assertRaises(ValueError): release.prepare(directory.parent, TAG, directory)
            extra.unlink()
            data = json.loads(index.read_text())
            data['companions'][0]['asset_name'] = '../outside.zip'
            index.write_text(json.dumps(data))
            with self.assertRaises(ValueError): release.prepare(directory.parent, TAG, directory)

    def test_remote_exact_digest_url_state_and_asset_set(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG, 'prerelease': True}):
            directory, _, _ = self.fixture(Path(temp))
            expected = release.prepare(directory.parent, TAG, directory)
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

    def test_draft_temporary_urls_remain_bound_to_one_release(self):
        with tempfile.TemporaryDirectory(dir=ROOT / 'work') as temp, patch.object(release, 'identity', return_value={'version': VERSION, 'tag': TAG, 'prerelease': True}):
            directory, _, _ = self.fixture(Path(temp))
            expected = release.prepare(directory.parent, TAG, directory)
            prefix = 'https://github.com/' + release.REPOSITORY + '/releases/'

            def draft(suffix):
                return {'tag_name': TAG, 'html_url': prefix + 'tag/' + suffix,
                        'draft': True, 'prerelease': True,
                        'assets': [{'name': asset['name'], 'digest': 'sha256:' + asset['sha256'],
                                    'size': asset['size'], 'state': 'uploaded',
                                    'browser_download_url': prefix + 'download/' + suffix + '/' + asset['name']}
                                   for asset in expected['assets']]}

            for suffix in ('untagged-' + 'a' * 16, 'untagged-7d1c9c11c63bfb7c6438', 'untagged-' + 'b' * 64):
                with self.subTest(suffix=suffix):
                    try:
                        release.verify_remote(expected, draft(suffix), True)
                    except ValueError as error:
                        self.fail('A matching temporary draft URL must be accepted: ' + str(error))

            valid = draft('untagged-7d1c9c11c63bfb7c6438')
            for suffix in ('untagged-' + 'a' * 15, 'untagged-' + 'a' * 65, 'untagged-' + 'A' * 20,
                           'untagged-' + 'g' * 20, 'untagged-' + 'a' * 20 + '/other'):
                with self.subTest(invalid_suffix=suffix), self.assertRaises(ValueError):
                    release.verify_remote(expected, draft(suffix), True)

            mutations = [
                ('foreign-release', lambda value: value.update(html_url=value['html_url'].replace(release.REPOSITORY, 'someone/else'))),
                ('null-release-url', lambda value: value.update(html_url=None)),
                ('queried-release-url', lambda value: value.update(html_url=value['html_url'] + '?other=1')),
                ('wrong-tag', lambda value: value.update(tag_name='companions-v0.10.4-dev.1')),
                ('mixed-canonical-asset', lambda value: value['assets'][0].update(browser_download_url=prefix + 'download/' + TAG + '/' + value['assets'][0]['name'])),
                ('mixed-draft-asset', lambda value: value['assets'][0].update(browser_download_url=value['assets'][0]['browser_download_url'].replace('untagged-7d1c9c11c63bfb7c6438', 'untagged-' + 'c' * 20))),
                ('foreign-asset', lambda value: value['assets'][0].update(browser_download_url=value['assets'][0]['browser_download_url'].replace(release.REPOSITORY, 'someone/else'))),
                ('bad-digest', lambda value: value['assets'][0].update(digest='sha256:' + '0' * 64)),
                ('bad-size', lambda value: value['assets'][0].update(size=1)),
                ('bad-state', lambda value: value['assets'][0].update(state='starter')),
                ('missing-asset', lambda value: value['assets'].pop()),
                ('duplicate-asset', lambda value: value['assets'].append(value['assets'][0])),
            ]
            for name, mutate in mutations:
                changed = copy.deepcopy(valid)
                mutate(changed)
                with self.subTest(mutation=name), self.assertRaises(ValueError):
                    release.verify_remote(expected, changed, True)

            canonical = draft(TAG)
            canonical['assets'][0]['browser_download_url'] = valid['assets'][0]['browser_download_url']
            with self.assertRaises(ValueError):
                release.verify_remote(expected, canonical, True)
            valid['draft'] = False
            with self.assertRaises(ValueError):
                release.verify_remote(expected, valid, False)
            canonical = draft(TAG)
            canonical['draft'] = False
            release.verify_remote(expected, canonical, False)


if __name__ == '__main__':
    unittest.main()
