"""Publication guards for promoting previously verified native packages."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from tools import promote_macos_release as promotion


class PromotionTests(unittest.TestCase):
    def fixture(self, root, bad=None):
        commit = 'a' * 40
        for arch in ['arm64', 'x86_64']:
            folder = root / arch
            folder.mkdir()
            info = dict(version='0.7.0', architecture=arch, mode='preview', commit=commit)
            if bad == 'app-commit':
                info['commit'] = 'b' * 40
            hashes = {}
            for ext in ['dmg', 'zip']:
                name = 'DeskPet-macOS-0.7.0-' + arch + '-preview.' + ext
                f = folder / name
                if ext == 'zip':
                    with ZipFile(f, 'w') as z:
                        z.writestr('DeskPet.app/Contents/Resources/build-info.json', json.dumps(info))
                else:
                    f.write_bytes(b'native CI fixture')
                hashes[name] = hashlib.sha256(f.read_bytes()).hexdigest()
                if bad == 'digest':
                    hashes[name] = '0' * 64
            report = dict(version='0.7.0', architecture=arch, mode='preview', signature='ad-hoc (not Developer ID)', notarized=False, archives_verified=['zip', 'dmg'], artifacts=hashes)
            (folder / 'build-report.json').write_text(json.dumps(report))
            (folder / 'app-smoke.json').write_text(json.dumps(dict(success=bad != 'smoke')))
        def api(args, **kwargs):
            path = args[-1]
            if '/actions/runs/' in path:
                return json.dumps(dict(status='completed', conclusion='failure' if bad == 'ci' else 'success', head_sha=commit, path='.github/workflows/macos.yml'))
            if '/git/ref/' in path:
                return json.dumps(dict(object=dict(type='commit', sha='b' * 40 if bad == 'tag' else commit)))
            return json.dumps(dict(draft=bad != 'published', tag_name='v0.7.0'))
        return api

    def test_verified_upload_and_fail_closed(self):
        for bad in [None, 'ci', 'tag', 'published', 'smoke', 'app-commit', 'digest']:
            with self.subTest(bad=bad), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp) / 'artifacts'
                root.mkdir()
                api = self.fixture(root, bad)
                argv = ['promotion', '--artifact-root', str(root), '--output', str(Path(tmp) / 'out'), '--version', '0.7.0', '--commit', 'a' * 40, '--repository', 'owner/repo', '--run-id', '1']
                with patch.object(sys, 'argv', argv), patch.object(promotion.subprocess, 'check_output', side_effect=api), patch.object(promotion.subprocess, 'run') as uploads:
                    if bad:
                        with self.assertRaises(ValueError):
                            promotion.main()
                        uploads.assert_not_called()
                    else:
                        promotion.main()
                        self.assertEqual(uploads.call_count, 4)
                        manifest = json.loads((Path(tmp) / 'out/mac-upload-manifest.json').read_text())
                        self.assertEqual([x['name'] for x in manifest['assets']], ['DeskPet-macOS-0.7.0-' + arch + '-preview.' + ext for arch in ['arm64', 'x86_64'] for ext in ['dmg', 'zip']])


if __name__ == '__main__':
    unittest.main()
