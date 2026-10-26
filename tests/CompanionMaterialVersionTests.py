import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import build_official_companions as authoring


class MaterialVersionTests(unittest.TestCase):
    def test_payload_ignores_only_release_identity_fields(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'pet.json').write_text(json.dumps({'pack_version': '0.11.0-dev.3', 'system_prompt': 'original'}))
            (root / 'companions.json').write_text(json.dumps({'id': 'e-bao', 'version': '0.11.0-dev.3', 'min_app_version': '0.11.0-dev.3'}))
            (root / 'sprite.png').write_bytes(b'original pixels')
            references = {'pet.json', 'companions.json', 'sprite.png'}
            first = authoring.payload_digest(root, references)
            (root / 'pet.json').write_text(json.dumps({'pack_version': '0.11.1-dev.1', 'system_prompt': 'original'}))
            self.assertEqual(first, authoring.payload_digest(root, references))
            (root / 'pet.json').write_text(json.dumps({'pack_version': '0.11.1-dev.1', 'system_prompt': 'changed'}))
            self.assertNotEqual(first, authoring.payload_digest(root, references))

    def test_registry_fails_closed_on_content_drift(self):
        record = {'pack_version': '0.11.0-dev.3', 'min_app_version': '0.11.0-dev.3', 'payload_sha256': 'a' * 64}
        authoring.verify_material_record(record, 'a' * 64)
        with self.assertRaisesRegex(ValueError, 'material'):
            authoring.verify_material_record(record, 'b' * 64)

    def test_filename_uses_material_not_application_version(self):
        self.assertEqual('DeskPet-Companion-e-bao-0.11.0-dev.3.zip', authoring.package_name('e-bao', '0.11.0-dev.3'))

    def test_changed_payload_requires_higher_material_version(self):
        old = {'pack_version': '0.11.0-dev.3', 'min_app_version': '0.11.0-dev.3', 'payload_sha256': 'a' * 64}
        with self.assertRaises(ValueError):
            authoring.verify_material_transition(old, dict(old, payload_sha256='b' * 64))
        with self.assertRaises(ValueError):
            authoring.verify_material_transition(old, dict(old, pack_version='0.11.1-dev.1'))
        authoring.verify_material_transition(old, dict(old, pack_version='0.11.1-dev.1', payload_sha256='b' * 64))


if __name__ == '__main__':
    unittest.main()
