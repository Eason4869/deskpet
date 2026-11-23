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
from build_official_companions import manifest_references


TEST_OUTPUT = Path(__file__).resolve().parents[1] / 'work'
TEST_OUTPUT.mkdir(exist_ok=True)


class BundledEbaoTests(unittest.TestCase):
    def fixture(self, root):
        repo = root / 'repo'
        source = root / 'source'
        source.mkdir()
        manifest = {'id': 'e-bao', 'pack_version': '1.0.4-dev.1',
                    'preview': 'preview.png', 'avatar': 'avatar.png', 'gaze': {'file': 'gaze.png'},
                    'animations': {str(i): {'file': 'animations/'+str(i)+'.png'} for i in range(17)},
                    'alternate_animations': {str(i): {'file': 'alternate/'+str(i)+'.png'} for i in range(9)},
                    'extra_animations': {str(i): {'file': 'extra/'+str(i)+'.png'} for i in range(2)}}
        (source / 'pet.json').write_text(json.dumps(manifest), encoding='utf-8')
        (source / 'companions.json').write_text(json.dumps({'id': 'e-bao', 'version': '1.0.4-dev.1', 'min_app_version': '0.11.0-dev.3'}), encoding='utf-8')
        references = manifest_references(manifest) | {'companions.json', 'holidays/catalog.json', 'holidays/outfit.png'}
        for name in references - {'pet.json', 'companions.json'}:
            path = source / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(('reviewed '+name).encode())
        pin = {'pack_version': '1.0.4-dev.1', 'min_app_version': '0.11.0-dev.3',
               'payload_sha256': bundle.payload_digest(source, references)}
        (repo / 'assets/official').mkdir(parents=True)
        (repo / 'assets/official/companion-versions.json').write_text(json.dumps({'companions': {'e-bao': pin}}))
        package = root / 'e.zip'
        with zipfile.ZipFile(package, 'w') as archive:
            for path in source.rglob('*'):
                if path.is_file(): archive.write(path, path.relative_to(source).as_posix())
        return repo, package, root / 'embedded'

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_inventory_is_bound_to_exact_archive_and_entry_bytes(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp))
            inventory = bundle.prepare(repo, package, output)
            self.assertEqual('defaults', inventory['coverage'])
            self.assertEqual(2, inventory['schema_version'])
            self.assertEqual(33, len(inventory['files']))
            self.assertEqual(hashlib.sha256(package.read_bytes()).hexdigest(), inventory['canonical_zip_sha256'])
            self.assertEqual(package.read_bytes(), (output / 'e-bao.canonical.zip').read_bytes())
            self.assertEqual(hashlib.sha256((output / 'e-bao.zip').read_bytes()).hexdigest(), inventory['zip_sha256'])
            with zipfile.ZipFile(output / 'e-bao.zip') as archive:
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
                    target.writestr(name, b'different pixels' if name == 'animations/0.png' else source.read(name))
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

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_omitted_wardrobe_is_still_bound_to_the_complete_material_pin(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp)); bundle.prepare(repo, package, output)
            canonical = output / 'e-bao.canonical.zip'
            with zipfile.ZipFile(canonical) as source:
                files = {n: source.read(n) for n in source.namelist()}
            files['holidays/outfit.png'] = b'changed unembedded art'
            with zipfile.ZipFile(canonical, 'w') as target:
                for n, data in files.items(): target.writestr(n, data)
            inventory = output / 'e-bao.inventory.json'; value = json.loads(inventory.read_text())
            value['canonical_zip_sha256'] = hashlib.sha256(canonical.read_bytes()).hexdigest(); inventory.write_text(json.dumps(value))
            with self.assertRaises(ValueError): bundle.verify_prepared(repo, output)

    @patch.object(bundle, 'input_stamp', return_value='fixture')
    @patch.object(bundle, 'check_manifest')
    def test_manifest_closure_cannot_be_forged_by_rehashing_partial_inventory(self, *_):
        for omission in ('animations/0.png', 'extra/0.png'):
            with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
                repo, package, output = self.fixture(Path(temp)); bundle.prepare(repo, package, output)
                archive = output / 'e-bao.zip'
                with zipfile.ZipFile(archive) as source:
                    files = {n: source.read(n) for n in source.namelist() if n != omission}
                with zipfile.ZipFile(archive, 'w') as target:
                    for n, data in files.items(): target.writestr(n, data)
                inventory = output / 'e-bao.inventory.json'; value = json.loads(inventory.read_text())
                value['files'] = [e for e in value['files'] if e['path'] != omission]
                value['zip_sha256'] = hashlib.sha256(archive.read_bytes()).hexdigest(); inventory.write_text(json.dumps(value))
                with self.assertRaises(ValueError): bundle.verify_prepared(repo, output)

    @patch.object(bundle, 'input_stamp', return_value='new-defaults-schema')
    @patch.object(bundle, 'check_manifest')
    def test_old_complete_cache_is_invalidated_and_rederived_from_verified_source(self, *_):
        with tempfile.TemporaryDirectory(dir=TEST_OUTPUT) as temp:
            repo, package, output = self.fixture(Path(temp)); bundle.prepare(repo, package, output)
            (output / 'e-bao.zip').write_bytes(package.read_bytes())
            (output / 'inputs.txt').write_text('old-complete-layout')
            with patch.object(bundle.subprocess, 'run', side_effect=AssertionError('must reuse canonical proof')):
                bundle.ensure(repo, output)
            bundle.verify_prepared(repo, output)
            with zipfile.ZipFile(output / 'e-bao.zip') as derived:
                self.assertEqual(len(derived.namelist()), 33)
                self.assertFalse(any(n.startswith('holidays/') for n in derived.namelist()))

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
