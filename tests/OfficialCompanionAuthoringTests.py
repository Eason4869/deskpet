"""Package authoring boundaries independent of the full artwork inventory."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location('official_authoring', Path(__file__).resolve().parents[1] / 'tools/build_official_companions.py')
authoring = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(authoring)


class AuthoringTests(unittest.TestCase):
    def test_paths_reject_traversal_and_platform_aliases(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ('../outside.png', '/outside.png', 'a\\b.png', 'C:/a.png', 'a//b.png', './a.png', 'a/../b.png', 'a./b.png', 'a /b.png', 'con.png', 'a/*.png', 'a\x00.png'):
                with self.subTest(name=name), self.assertRaises(ValueError):
                    authoring.safe_path(root, name)
            self.assertEqual(root.resolve() / 'animations/idle.png', authoring.safe_path(root, 'animations/idle.png'))

    def test_archive_exact_bytes_and_determinism(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source'
            source.mkdir()
            (source / 'pet.json').write_bytes(b'{"name":"fixture"}\n')
            (source / 'frame.png').write_bytes(bytes(range(256)))
            expected = {'pet.json', 'frame.png'}
            first, second = root / 'one.zip', root / 'two.zip'
            authoring.write_archive(source, expected, first)
            authoring.write_archive(source, expected, second)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            with zipfile.ZipFile(first) as archive:
                self.assertEqual(sorted(expected), archive.namelist())
                for name in expected:
                    self.assertEqual((source / name).read_bytes(), archive.read(name))

    def test_archive_rejects_unreferenced_and_missing_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source'
            source.mkdir()
            (source / 'pet.json').write_text('{}')
            (source / 'stale.png').write_bytes(b'stale')
            with self.assertRaises(ValueError):
                authoring.write_archive(source, {'pet.json'}, root / 'bad.zip')
            self.assertFalse((root / 'bad.zip').exists())
            with self.assertRaises(ValueError):
                authoring.write_archive(source, {'pet.json', 'stale.png', 'missing.png'}, root / 'bad.zip')

    def test_versions_and_existing_output_fail_before_mutation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / 'existing'
            output.mkdir()
            marker = output / 'keep.txt'
            marker.write_text('keep')
            with self.assertRaises(ValueError):
                authoring.create_output(root, output, '0.10.3-dev.1')
            for version in ('../escape', 'v0.10.3', '0.10.3-dev.01', '0.10.3\n', '0.10.3-beta.1', '0.10.3.1-dev.1', '65535.1.0', '0.10.3-dev.2147483648'):
                with self.subTest(version=version), self.assertRaises(ValueError):
                    authoring.create_output(root, root / 'new', version)
            self.assertEqual('keep', marker.read_text())
            self.assertFalse((root / 'new').exists())

    def test_duplicate_json_fields_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'bad.json'
            path.write_text('{"version":1,"version":2}')
            with self.assertRaises(ValueError):
                authoring.read_json(path)


if __name__ == '__main__':
    unittest.main()
