import importlib.util
from pathlib import Path
import tempfile
import unittest
from PIL import Image

SPEC=importlib.util.spec_from_file_location('holiday_validator',Path(__file__).resolve().parents[1]/'tools/validate_holiday_assets.py')
validator=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validator)

class ValidationTests(unittest.TestCase):
    def test_revised_quiet_source_timing_preserves_all_eight_frames(self):
        for role in validator.CHARACTERS:
            for action, variant in (('idle', 'default'), ('sleeping', 'default'), ('sleeping', 'alternate')):
                with self.subTest(role=role, action=action, variant=variant):
                    self.assertEqual((8, 4.0 if action == 'idle' else 2.5, True), validator.source_semantics(role, action, variant))
    @classmethod
    def setUpClass(cls):
        (validator.ROOT/'work').mkdir(exist_ok=True)
    def test_paths_cannot_escape_or_alias_root(self):
        with tempfile.TemporaryDirectory(dir=validator.ROOT/'work') as directory:
            root=Path(directory)
            for path in ('../a.png','/a.png','C:/a.png','x\\a.png','a/../b.png','a//b.png'):
                with self.subTest(path=path),self.assertRaises(ValueError):validator.asset_path(root,path)
            self.assertEqual(validator.asset_path(root,'halloween/e-bao/idle.png'),root/'halloween/e-bao/idle.png')
    def test_nonempty_transparent_frames_and_padding(self):
        with tempfile.TemporaryDirectory(dir=validator.ROOT/'work') as directory:
            path=Path(directory)/'clip.png'
            image=Image.new('RGBA',(16,8));image.putpixel((3,3),(255,0,0,255));image.save(path)
            self.assertEqual(validator.check_png(path,8,8,2,1),1)
            with self.assertRaisesRegex(ValueError,'empty'):validator.check_png(path,8,8,2,2)
            image.putpixel((12,3),(255,0,0,255));image.save(path)
            with self.assertRaisesRegex(ValueError,'padding'):validator.check_png(path,8,8,2,1)
    def test_opaque_wrong_size_and_corrupt_png_fail(self):
        with tempfile.TemporaryDirectory(dir=validator.ROOT/'work') as directory:
            path=Path(directory)/'clip.png';Image.new('RGBA',(8,8),'red').save(path)
            with self.assertRaisesRegex(ValueError,'transparent'):validator.check_png(path,8,8,1,1)
            with self.assertRaisesRegex(ValueError,'dimensions'):validator.check_png(path,9,8,1,1)
            path.write_bytes(b'not PNG')
            with self.assertRaises(ValueError):validator.check_png(path,8,8,1,1)
    def test_required_matrix_is_character_specific(self):
        self.assertEqual(len(validator.required_clips('e-bao')),28)
        self.assertEqual(len(validator.required_clips('gugugaga')),28)
        self.assertEqual(len(validator.required_clips('dafeiyu')),27)
        self.assertIn(('jumping','alternate'),validator.required_clips('e-bao'))
        self.assertNotIn(('jumping','alternate'),validator.required_clips('marfu'))

if __name__=='__main__':unittest.main()
