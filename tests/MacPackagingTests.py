"""Structural/failure tests; native execution is verified separately on macOS."""
import json
import os
import plistlib
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from mac_package import PackagingError, create_bundle, create_disk_image, macho_architectures, release_requirements, sign_bundle, validate_bundle


class MacPackagingTests(unittest.TestCase):
    def setUp(self):
        self.scratch = Path(__file__).resolve().parents[1] / 'work/mac-packaging-tests'
        self.scratch.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=self.scratch)
        self.root = Path(self.temp.name)
        self.publish = self.root / 'publish'
        self.publish.mkdir()
        for name in ['DeskPet', 'libcoreclr.dylib', 'libAvaloniaNative.dylib', 'libDeskPetMac.dylib']:
            self.binary(self.publish / name)
        (self.publish / 'DeskPet.dll').write_bytes(b'managed-assembly-fixture')
        (self.publish / 'DeskPet.deps.json').write_text('{}')
        (self.publish / 'DeskPet.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
            'tfm': 'net10.0', 'includedFrameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.9'}]}}))
        self.icon = self.root / 'DeskPet.icns'
        self.icon.write_bytes(b'icns' + struct.pack('>I', 16) + b'icp4' + struct.pack('>I', 8))
        self.app = self.root / 'DeskPet.app'

    def tearDown(self):
        self.assertTrue(self.root.resolve().is_relative_to(self.scratch.resolve()))
        self.assertFalse(self.root.is_symlink())
        self.temp.cleanup()

    @staticmethod
    def binary(path, cpu=0x0100000C):
        path.write_bytes(struct.pack('<8I', 0xFEEDFACF, cpu, 0, 2, 0, 0, 0, 0))

    def make(self, **kw):
        return create_bundle(self.publish, self.app, 'arm64', '0.5.5', 'preview', self.icon, **kw)

    def test_bundle_has_real_metadata_and_current_version(self):
        self.make()
        info = plistlib.loads((self.app / 'Contents/Info.plist').read_bytes())
        self.assertEqual('0.5.5', info['CFBundleShortVersionString'])
        self.assertEqual('14.0', info['LSMinimumSystemVersion'])
        self.assertTrue(info['LSUIElement'])

        self.assertTrue(info['NSMicrophoneUsageDescription'])
        self.assertIn('预览', info['CFBundleDisplayName'])
        if os.name != 'nt':
            self.assertTrue((self.app / 'Contents/MacOS/DeskPet').stat().st_mode & 0o111)
        result = validate_bundle(self.app, 'arm64', '0.5.5', 'preview')
        self.assertEqual(4, len(result['native_binaries']))
        self.assertEqual((self.app/'Contents/Resources/im-logos.LICENSE.txt').read_bytes(),(Path(__file__).resolve().parents[1]/'assets/im-logos.LICENSE.txt').read_bytes())

    def test_dev_display_identity_keeps_numeric_platform_version(self):
        create_bundle(self.publish, self.app, 'arm64', '0.9.5-dev.2', 'preview', self.icon, commit='a'*40)
        info=plistlib.loads((self.app/'Contents/Info.plist').read_bytes())
        self.assertEqual(info['CFBundleShortVersionString'],'0.9.5')
        self.assertEqual(info['CFBundleVersion'],'0.9.5')
        identity=json.loads((self.app/'Contents/Resources/build-info.json').read_text())
        self.assertEqual(identity['version'],'0.9.5-dev.2')

    def test_wrong_architecture_rejected_before_app_is_created(self):
        self.binary(self.publish / 'libDeskPetMac.dylib', 0x01000007)
        with self.assertRaisesRegex(PackagingError, 'architecture'):
            self.make()
        self.assertFalse(self.app.exists())

    def test_preview_signatures_allow_separately_ad_hoc_signed_runtime(self):
        self.make()
        with patch('mac_package.run') as command:
            sign_bundle(self.app, 'arm64', '0.5.5', 'preview')
        calls = [call.args[0] for call in command.call_args_list]
        self.assertTrue(any('--entitlements' in args for args in calls))
        self.assertTrue(all('runtime' not in args for args in calls))
        self.assertTrue(all('--timestamp' not in args for args in calls))

    def test_managed_files_are_signed_before_bundle_executable(self):
        self.make()
        with patch('mac_package.run') as command:
            sign_bundle(self.app, 'arm64', '0.5.5', 'preview')
        signed = [call.args[0][-1] for call in command.call_args_list if '--sign' in call.args[0]]
        managed = self.app / 'Contents/MacOS/DeskPet.dll'
        apphost = self.app / 'Contents/MacOS/DeskPet'
        self.assertIn(managed, signed)
        self.assertLess(signed.index(managed), signed.index(apphost))

    def test_release_native_helpers_have_hardened_runtime(self):
        self.binary(self.publish / 'createdump')
        create_bundle(self.publish, self.app, 'arm64', '0.5.5', 'release', self.icon)
        with patch.dict(os.environ, {'MACOS_SIGN_IDENTITY': 'Developer ID Application: Example (ABCDEFGHIJ)', 'MACOS_NOTARY_PROFILE': 'profile'}), patch('mac_package.run') as command:
            sign_bundle(self.app, 'arm64', '0.5.5', 'release')
        helper = next(call.args[0] for call in command.call_args_list if Path(call.args[0][-1]).name == 'createdump')
        self.assertIn('runtime', helper)
        self.assertIn('--timestamp', helper)

    def test_disk_image_retries_only_transient_busy_errors(self):
        busy = subprocess.CalledProcessError(1, ['hdiutil'], stderr='hdiutil: create failed - Resource busy')
        success = subprocess.CompletedProcess(['hdiutil'], 0, stdout='created: preview.dmg\n', stderr='')
        with patch('mac_package.run', side_effect=[busy, success]) as command, patch('mac_package.time.sleep') as sleep:
            create_disk_image(self.root, self.root / 'preview.dmg', 'preview')
        self.assertEqual(2, command.call_count)
        sleep.assert_called_once_with(2)
        bad_input = subprocess.CalledProcessError(1, ['hdiutil'], stderr='hdiutil: create failed - No such file')
        with patch('mac_package.run', side_effect=bad_input) as command, patch('mac_package.time.sleep') as sleep:
            with self.assertRaises(subprocess.CalledProcessError):
                create_disk_image(self.root, self.root / 'preview.dmg', 'preview')
        self.assertEqual(1, command.call_count)
        sleep.assert_not_called()
        with patch('mac_package.run', side_effect=busy) as command, patch('mac_package.time.sleep') as sleep:
            with self.assertRaises(subprocess.CalledProcessError):
                create_disk_image(self.root, self.root / 'preview.dmg', 'preview')
        self.assertEqual(3, command.call_count)
        self.assertEqual([2, 4], [call.args[0] for call in sleep.call_args_list])

    def test_missing_native_bridge_rejected(self):
        (self.publish / 'libDeskPetMac.dylib').unlink()
        with self.assertRaisesRegex(PackagingError, 'libDeskPetMac'):
            self.make()

    def test_framework_dependent_publish_rejected(self):
        (self.publish / 'DeskPet.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
            'framework': {'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}}}))
        with self.assertRaisesRegex(PackagingError, 'self-contained'):
            self.make()

    def test_user_data_or_certificate_never_enters_bundle(self):
        for name in ['settings.json', '.env', 'certificate.p12']:
            with self.subTest(name=name):
                path = self.publish / name
                path.write_text('test-not-a-secret')
                with self.assertRaisesRegex(PackagingError, 'private'):
                    self.make()
                path.unlink()

    def test_existing_output_is_preserved(self):
        self.app.mkdir()
        sentinel = self.app / 'preserve.txt'
        sentinel.write_text('original')
        with self.assertRaisesRegex(PackagingError, 'exists'):
            self.make()
        self.assertEqual('original', sentinel.read_text())

    def test_destination_inside_publish_rejected(self):
        with self.assertRaisesRegex(PackagingError, 'inside'):
            create_bundle(self.publish, self.publish / 'bad.app', 'arm64', '0.5.5', 'preview', self.icon)

    def test_modified_metadata_is_detected(self):
        self.make()
        info_path = self.app / 'Contents/Info.plist'
        info = plistlib.loads(info_path.read_bytes())
        info['CFBundleVersion'] = '0.5.4'
        info_path.write_bytes(plistlib.dumps(info))
        with self.assertRaisesRegex(PackagingError, 'version'):
            validate_bundle(self.app, 'arm64', '0.5.5', 'preview')

    def test_release_fails_closed_without_signing_and_notary_profile(self):
        with self.assertRaises(PackagingError):
            release_requirements({})
        with self.assertRaises(PackagingError):
            release_requirements({'MACOS_SIGN_IDENTITY': '-', 'MACOS_NOTARY_PROFILE': 'profile'})
        self.assertEqual(('Developer ID Application: Example (ABCDEFGHIJ)', 'profile'), release_requirements({
            'MACOS_SIGN_IDENTITY': 'Developer ID Application: Example (ABCDEFGHIJ)', 'MACOS_NOTARY_PROFILE': 'profile'}))

    def test_thin_and_fat_macho_headers(self):
        self.assertEqual({'arm64'}, macho_architectures(self.publish / 'DeskPet'))
        fat = self.root / 'universal.dylib'
        fat.write_bytes(struct.pack('>2I', 0xCAFEBABE, 2) + struct.pack('>5I', 0x01000007, 0, 48, 32, 0)
                        + struct.pack('>5I', 0x0100000C, 0, 80, 32, 0) + bytes(64))
        self.assertEqual({'x86_64', 'arm64'}, macho_architectures(fat))
        self.assertEqual(set(), macho_architectures(self.publish / 'DeskPet.dll'))


if __name__ == '__main__':
    unittest.main()
