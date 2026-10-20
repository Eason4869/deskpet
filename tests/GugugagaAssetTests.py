"""Verify source-sheet registration without generating or replacing character art."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('prepare_gugugaga', ROOT / 'tools/prepare_gugugaga.py')
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)


class RegistrationTests(unittest.TestCase):
    def source(self, root, rise=False, duplicate=False, cut=False):
        sheet = Image.new('RGBA', (400, 240))
        for i in range(8):
            x, y = (i % 4) * 100, (i // 4) * 120
            lift = 18 if rise and i in (3, 4) else 0
            color = (80 if duplicate else 70 + i * 15, 110, 160, 255)
            ImageDraw.Draw(sheet).rounded_rectangle((x + (0 if cut and i == 0 else 24), y + 20 - lift, x + 76, y + 105 - lift), radius=8, fill=color)
        source = root / 'source.png'
        sheet.save(source)
        return source

    def test_registers_complete_transparent_poses_and_clears_hidden_rgb(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            report = pipeline.pack_sheet(self.source(root), root / 'result.png')
            result = Image.open(root / 'result.png').convert('RGBA')
            self.assertEqual(result.size, (3840, 520))
            self.assertEqual(report['frames'], 8)
            for i in range(8):
                frame = result.crop((i * 480, 0, (i + 1) * 480, 520))
                self.assertIsNotNone(frame.getbbox())
                self.assertEqual(frame.getpixel((0, 0)), (0, 0, 0, 0))
                self.assertLessEqual(frame.getbbox()[3], 508)

    def test_shared_transform_preserves_airborne_height(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            report = pipeline.pack_sheet(self.source(root, rise=True), root / 'jump.png')
            bounds = report['output_bounds']
            self.assertGreater(bounds[0][3] - bounds[3][3], 45)
            self.assertEqual(bounds[0][2] - bounds[0][0], bounds[3][2] - bounds[3][0])
            self.assertEqual(bounds[0][3] - bounds[0][1], bounds[3][3] - bounds[3][1])

    def test_rejects_cropped_or_copied_poses(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaisesRegex(ValueError, 'boundary'):
                pipeline.pack_sheet(self.source(root, cut=True), root / 'cut.png')
            with self.assertRaisesRegex(ValueError, 'distinct'):
                pipeline.pack_sheet(self.source(root, duplicate=True), root / 'copy.png')

    def test_finds_empty_row_gutter_when_nominal_grid_crosses_art(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = self.source(root)
            source = Image.open(path).convert('RGBA')
            shifted = Image.new('RGBA', source.size)
            shifted.paste(source.crop((0, 0, 400, 120)))
            shifted.paste(source.crop((0, 120, 400, 240)), (0, 92))
            shifted.save(path)
            self.assertEqual(pipeline.pack_sheet(path, root / 'result.png')['frames'], 8)


if __name__ == '__main__':
    unittest.main()
