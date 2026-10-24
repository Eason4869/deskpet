"""Package authoring boundaries independent of the full artwork inventory."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from unittest.mock import patch
from PIL import Image

SPEC = importlib.util.spec_from_file_location('official_authoring', Path(__file__).resolve().parents[1] / 'tools/build_official_companions.py')
authoring = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(authoring)


class AuthoringTests(unittest.TestCase):
    def test_final_quiet_overlay_wins_over_legacy_export_and_fps_defaults(self):
        # Small authoring fixture exercises collect_role's actual ordering. Full
        # PNG/native validation is a separate integration gate, not mocked there.
        for role in ('e-bao', 'gugugaga'):
            with self.subTest(role=role), tempfile.TemporaryDirectory() as directory:
                root=Path(directory); repo=root/'repo'; defaults=root/'defaults'
                source=repo/'assets/bundled-skins'/role if role=='gugugaga' else defaults/role
                source.mkdir(parents=True)
                animations={a:{'file':'animations/'+a+'.png','columns':8,'frames':4,'fps':8,'loop':a in ('idle','sleeping','lifted')} for a in authoring.BASE+authoring.INTERACTIONS}
                alternate={'sleeping':{'file':'variants/sleeping.png','columns':8,'frames':8,'fps':8,'loop':True}}
                manifest={'name':'Fixture','preview':'preview.png','gaze':{'file':'gaze.png'},'animations':animations,'alternate_animations':alternate,'voice_presets':{'speech':['voice'],'live':['voice']}}
                authoring.write_json(source/'pet.json',manifest)
                for name in authoring.manifest_references(manifest)-{'pet.json'}:
                    path=source/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'old')
                avatars=repo/'assets/avatars';avatars.mkdir(parents=True);Image.new('RGBA',(2,2)).save(avatars/(role+'.png'))
                if role=='e-bao':
                    extra=repo/'assets/additional';extra.mkdir();Image.new('RGBA',(1920,2080)).save(extra/(role+'.png'))
                    companions=repo/'assets/companion-animations'/role;companions.mkdir(parents=True)
                    for action in authoring.ALTERNATE+('jumping',):(companions/(action+'.png')).write_bytes(b'old-alternate')
                quiet=repo/'assets/companion-quiet'/role;quiet.mkdir(parents=True)
                for sheet in ('idle','sleeping','sleeping-alternate'):(quiet/(sheet+'.png')).write_bytes(('original-'+sheet).encode())
                holidays=repo/'assets/holidays';holidays.mkdir(parents=True)
                entry={'character_id':role,'outfit_id':'halloween-2026','holiday_id':'halloween','name':'fixture','version':'1.0.3','ready':True,'definition':'outfit.json'}
                authoring.write_json(holidays/'catalog.json',{'schema_version':1,'outfits':[entry]})
                clip={'path':'frame.png','columns':8,'frame_width':480,'frame_height':520,'frame_count':8}
                authoring.write_json(holidays/'outfit.json',dict(entry,preview='preview.png',clips=[clip],gaze=clip,easter_egg=clip))
                for name in ('frame.png','preview.png'):(holidays/name).write_bytes(b'holiday')
                destination=root/'result'
                with patch.object(authoring,'check_png'),patch.object(authoring,'check_manifest'):
                    authoring.collect_role(repo,defaults,destination,role,'0.10.5-dev.3')
                final=authoring.read_json(destination/'pet.json')
                for group,action,sheet in (('animations','idle','idle'),('animations','sleeping','sleeping'),('alternate_animations','sleeping','sleeping-alternate')):
                    selected=final[group][action]
                    self.assertEqual((8,8,4 if action=='idle' else 2.5,True),(selected['columns'],selected['frames'],selected['fps'],selected['loop']))
                    self.assertEqual((quiet/(sheet+'.png')).read_bytes(),(destination/selected['file']).read_bytes())
                self.assertEqual(b'old',(destination/final['animations']['waving']['file']).read_bytes())
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
