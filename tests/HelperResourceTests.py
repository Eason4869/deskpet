"""The helper may grow with translations, but never with artwork or unexplained data."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from tools.localization.helper_resources import validate_helper_resources


class HelperResourceTests(unittest.TestCase):
    def setUp(self):
        self.expected = {f"Dafeiyu.Locales.{i}.json": (100_000, str(i).zfill(64)) for i in range(13)}
        self.manifest = [dict(name=k, length=v[0], sha256=v[1]) for k, v in self.expected.items()]
        self.payload_size = sum(v[0] for v in self.expected.values())

    def test_thirteen_reviewed_catalogs_may_exceed_the_former_one_mib_total(self):
        validate_helper_resources(self.payload_size + 200_000, self.manifest, self.expected)

    def test_even_a_tiny_artwork_resource_is_rejected(self):
        with self.assertRaises(ValueError):
            validate_helper_resources(self.payload_size + 200_010, self.manifest + [dict(name='Dafeiyu.EbaoAtlas', length=10, sha256='a'*64)], self.expected)

    def test_code_or_unlisted_data_cannot_hide_behind_a_large_locale_budget(self):
        with self.assertRaises(ValueError):
            validate_helper_resources(self.payload_size + 1024*1024, self.manifest, self.expected)

    def test_missing_and_duplicate_catalogs_are_rejected(self):
        for manifest in [self.manifest[:-1], self.manifest + [self.manifest[0]]]:
            with self.assertRaises(ValueError):
                validate_helper_resources(self.payload_size + 200_000, manifest, self.expected)

    def test_same_size_but_changed_catalog_is_rejected(self):
        changed = [dict(row) for row in self.manifest]
        changed[0]['sha256'] = 'f'*64
        with self.assertRaises(ValueError):
            validate_helper_resources(self.payload_size + 200_000, changed, self.expected)

    def test_wrong_resource_lengths_and_impossible_file_size_are_rejected(self):
        for length in [0, -1, 100_001]:
            changed = [dict(row) for row in self.manifest]
            changed[0]['length'] = length
            with self.assertRaises(ValueError):
                validate_helper_resources(self.payload_size + 200_000, changed, self.expected)
        with self.assertRaises(ValueError):
            validate_helper_resources(self.payload_size - 1, self.manifest, self.expected)


if __name__ == '__main__':
    unittest.main()
