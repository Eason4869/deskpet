"""Exercise the packaging filename contract without producing release archives."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from tools.release_assets import installer_asset_name


class ReleaseAssetTests(unittest.TestCase):
    def test_preview_assets_preserve_full_release_identity(self):
        self.assertEqual(installer_asset_name('0.9.5-dev.2'), 'DeskPet-Windows-Setup-0.9.5-dev.2.exe')

    def test_python_version_order_matches_client(self):
        from tools.release_assets import version_key, release_metadata
        self.assertLess(version_key('0.9.4-dev.2'), version_key('0.9.4-dev.10'))
        self.assertLess(version_key('0.9.4-dev.10'), version_key('0.9.4'))
        self.assertEqual(version_key('0.9.4.0'), version_key('v0.9.4'))
        self.assertEqual(release_metadata('0.9.5-dev.2', 'v0.9.5-dev.2', 'dev')['prerelease'], True)
        with self.assertRaises(ValueError):
            release_metadata('0.9.5-dev.2', 'v0.9.5-dev.2', 'main')
        for value in ['0.9.4.1-dev.2', '0.9.4-dev.2147483648', 'vv0.9.4']:
            with self.assertRaises(ValueError):
                version_key(value)
        for value in ['0.9.4-dev', '0.9.4.0', 'v0.9.4']:
            with self.assertRaises(ValueError):
                release_metadata(value, 'v'+value, 'dev' if '-dev' in value else 'main')

    def test_numeric_transition_preserves_05_bridge_releases(self):
        for version, expected in (
            ('0.5.6', 'DeskPet-Setup-0.5.6.exe'),
            ('0.5.11', 'DeskPet-Setup-0.5.11.exe'),
            ('0.6.0', 'DeskPet-Windows-Setup-0.6.0.exe'),
            ('1.0.0', 'DeskPet-Windows-Setup-1.0.0.exe'),
        ):
            with self.subTest(version=version):
                self.assertEqual(installer_asset_name(version), expected)

    def test_invalid_versions_cannot_become_paths(self):
        for value in ('', '../0.6.0', '0.6.0.exe', '0.6.0\n', '2147483648.0.0', '9' * 5000):
            with self.subTest(version=value[:30]):
                with self.assertRaises(ValueError):
                    installer_asset_name(value)


if __name__ == '__main__':
    unittest.main()
