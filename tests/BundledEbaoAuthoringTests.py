"""Pin/transaction tests; full PNG and native coverage runs on production packs."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import prepare_bundled_ebao as bundle


TEST_OUTPUT = Path(__file__).resolve().parents[1] / 'work'
TEST_OUTPUT.mkdir(exist_ok=True)


class BundledEbaoTests(unittest.TestCase):
    def fixture(self, root):
        repo = root / 'repo'
        source = root / 'source'
        source.mkdir()
        for name, value in {
            'pet.json': {'id': 'e-bao', 'pack_version': '1.0.4-dev.1'},
            'companions.json': {'id': 'e-bao', 'version': '1.0.4-dev.1', 'min_app_version': '0.11.0-dev.3'}
        }.items():
            (source / name).write_text(json.dumps(value), encoding='utf-8')
        (source / 'idle.png').write_bytes(b'reviewed pixels')
        pin = {'pack_version': '1.0.4-dev.1', 'min_app_version': '0.11.0-dev.3',
               'payload_sha256': bundle.payload_digest(source, {'pet.json', 'companions.json', 'idle.png'})}
        (repo / 'assets/official').mkdir(parents=True)
        (repo / 'assets/official/companion-versions.json').write_text(json.dumps({'companions': {'e-bao': pin}}))
        package = root / 'e.zip'
        with zipfile.ZipFile(package, 'w') as archive:
            for path in source.iterdir():
                archive.write(path, path.name)
        return repo, package, root / 'embedded'

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_inventory_is_bound_to_exact_archive_and_entry_bytes(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp))
            inventory = bundle.prepare(repo, package, output)
            self.assertEqual(hashlib.sha256(package.read_bytes()).hexdigest(), inventory['zip_sha256'])
            self.assertEqual(package.read_bytes(), (output / 'e-bao.zip').read_bytes())
            with zipfile.ZipFile(package) as archive:
                self.assertEqual(set(archive.namelist()), {e['path'] for e in inventory['files']})
                for entry in inventory['files']:
                    data = archive.read(entry['path'])
                    self.assertEqual(len(data), entry['length'])
                    self.assertEqual(hashlib.sha256(data).hexdigest(), entry['sha256'])

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_changed_payload_fails_without_replacing_last_verified_bundle(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp))
            bundle.prepare(repo, package, output)
            previous = (output / 'e-bao.zip').read_bytes()
            inventory = (output / 'e-bao.inventory.json').read_bytes()
            changed = Path(temp) / 'changed.zip'
            with zipfile.ZipFile(package) as source, zipfile.ZipFile(changed, 'w') as target:
                for name in source.namelist():
                    target.writestr(name, b'different pixels' if name == 'idle.png' else source.read(name))
            with self.assertRaisesRegex(ValueError, 'Official material changed'):
                bundle.prepare(repo, changed, output)
            self.assertEqual(previous, (output / 'e-bao.zip').read_bytes())
            self.assertEqual(inventory, (output / 'e-bao.inventory.json').read_bytes())

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_production_gate_rejects_stale_pin_without_reauthoring(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp))
            bundle.prepare(repo, package, output)
            self.assertTrue(hasattr(bundle, 'verify_prepared'), 'production build must verify prepared bundle against current pin')
            bundle.verify_prepared(repo, output)
            registry=repo/'assets/official/companion-versions.json'
            value=json.loads(registry.read_text());value['companions']['e-bao']['payload_sha256']='0'*64;registry.write_text(json.dumps(value))
            with self.assertRaises(ValueError):
                bundle.verify_prepared(repo, output)

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_cache_rechecks_bytes_without_extraction_or_republication(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp))
            bundle.prepare(repo, package, output)
            with patch.object(bundle, 'prepare', side_effect=AssertionError('cache must be read-only')):
                bundle.ensure(repo, output)
                archive=output/'e-bao.zip'
                data=bytearray(archive.read_bytes());data[-1]^=1;archive.write_bytes(data)
                with self.assertRaises(ValueError):
                    bundle.ensure(repo, output)

    def test_unsafe_zip_entry_is_rejected_before_output_publication(self):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp))
            with zipfile.ZipFile(package, 'a') as archive:
                archive.writestr('../escape.png', b'bad')
            with self.assertRaises(ValueError):
                bundle.prepare(repo, package, output)
            self.assertFalse((output / 'e-bao.zip').exists())
            self.assertFalse((output / 'escape.png').exists())


if __name__ == '__main__':
    unittest.main()
