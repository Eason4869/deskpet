"""Keep image workflow acceptance in the default local and CI regressions."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SHARED = ('ImageTaskCenterLifecycleTests', 'ImageGenerationTaskStoreTests',
          'ImageGenerationTaskManagerTests', 'ImageGenerationMotionTests',
          'GeneratedImageGroupTests', 'ImageGalleryManagementTests',
          'ImageGalleryExportTests', 'ImageGenerationIntegrationTests')


class RegistrationTests(unittest.TestCase):
    def test_full_portable_runs_every_shared_image_suite(self):
        runner = (ROOT / 'desktop/DeskPet.Tests/PortableTests.cs').read_text(encoding='utf-8-sig')
        full = runner.split('static int Run()', 1)[1]
        for suite in SHARED:
            self.assertIn(suite + '.Main', full, suite)

    def test_portable_summary_counts_each_named_image_suite(self):
        runner = (ROOT / 'desktop/DeskPet.Tests/PortableTests.cs').read_text(encoding='utf-8-sig')
        pattern = re.search(r'Regex.Match\(line,"([^"]+)"', runner)[1]
        for suite in SHARED:
            source = (ROOT / 'tests' / (suite + '.cs')).read_text(encoding='utf-8-sig')
            self.assertIn(suite + ': ', source, 'stable summary: ' + suite)
            self.assertIsNotNone(re.match(pattern, suite + ': 7 passed', re.I), suite)

    def test_windows_ci_registers_new_targets_and_installer_preflight(self):
        ci = (ROOT / '.github/workflows/check.yml').read_text(encoding='utf-8-sig')
        for suite in SHARED + ('ImageTaskCenterUiTests',):
            self.assertIn("'" + suite + "'", ci, suite)
        self.assertIn('BuildInstaller.ps1 -OutputDirectory', ci)
        self.assertIn('work/dev3-ci-package', ci)

    def test_avalonia_full_and_separate_native_quit_processes(self):
        program = (ROOT / 'desktop/DeskPet.Desktop.Tests/Program.cs').read_text(encoding='utf-8-sig')
        self.assertIn('assertions += ImageTaskCenterUiChecks.Run(', program)
        for name in ('macos.yml', 'check.yml'):
            ci = (ROOT / '.github/workflows' / name).read_text(encoding='utf-8-sig')
            for flag in ('--image-writer-exit-ui-only', '--image-held-exit-ui-only', '--image-exit-ui-only'):
                self.assertIn(flag, ci, name + ': ' + flag)


if __name__ == '__main__':
    unittest.main()
