"""Guard executable core-test paths; behavioral suites still run separately in CI.

UI registration belongs to the UI integration task. Real foreground third-party
application compatibility remains controlled manual acceptance, outside CI.
"""
from pathlib import Path
import re
import sys
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
CORE = {
    'ImageModelResolverTests': '--image-model-resolver-only',
    'ImageInputRouterTests': '--image-input-router-only',
    'DesktopObservationCaptureTests': '--desktop-observation-only',
    'ProactiveConversationTests': '--proactive-conversation-only',
    'DesktopObservationPolicyTests': '--desktop-observation-policy-only',
    'DesktopObservationServiceTests': '--desktop-observation-policy-only',
}
PORTABLE_PROJECT = 'desktop/DeskPet.Tests/DeskPet.Tests.csproj'
PORTABLE_DLL = 'desktop/DeskPet.Tests/bin/Release/net10.0/DeskPet.Tests.dll'
MAC_PROJECT = 'desktop/DeskPet.Platform.Mac/Tests/DeskPet.Platform.Mac.Tests.csproj'
MAC_DLL = 'desktop/DeskPet.Platform.Mac/Tests/bin/Release/net10.0/DeskPet.Platform.Mac.Tests.dll'


def source(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')


def run_blocks(workflow):
    """Read literal run blocks, excluding comments that cannot execute commands."""
    lines = workflow.splitlines()
    blocks = []
    for index, line in enumerate(lines):
        match = re.fullmatch(r'( *)run: \|', line)
        if match is None:
            continue
        indent = len(match[1])
        body = []
        for following in lines[index + 1:]:
            if following.strip() and len(following) - len(following.lstrip()) <= indent:
                break
            if following.strip() and not following.lstrip().startswith('#'):
                body.append(following.strip())
        blocks.append(body)
    return blocks


def checked_command(blocks, command, windows):
    for block in blocks:
        for index, line in enumerate(block):
            if not re.fullmatch(command, line):
                continue
            if windows:
                if index + 1 < len(block) and re.search(
                        r'if \(\$LASTEXITCODE(?: -ne 0)?\) \{ throw ', block[index + 1]):
                    return True
            elif 'set -euo pipefail' in block[:index]:
                return True
    return False


def focused_route(runner, flag, suite):
    # A flag in help text or a branch returning zero cannot register a suite.
    runner = re.sub(r'(?m)^\s*//[^\n]*', '', runner)
    condition = re.search(r'if\s*\([^\n)]*args\[0\]\s*==\s*"' + re.escape(flag) + r'"[^)]*\)', runner)
    if condition is None:
        return False
    branch = re.split(r'\bif\s*\(', runner[condition.end():], maxsplit=1)[0]
    return re.search(r'\breturn\s+[^;]*?\b' + re.escape(suite) + r'\.Main\s*\(', branch) is not None


class RegistrationTests(unittest.TestCase):
    def test_windows_targets_compile_the_test_entry_and_execute_the_result(self):
        build = source('Build.ps1')
        targets = re.search(r'ValidateSet\((.*?)\)', build)[1]
        for suite in CORE:
            with self.subTest(suite=suite):
                self.assertIn("'" + suite + "'", targets)
                self.assertTrue((ROOT / 'tests' / (suite + '.cs')).is_file(), suite + ' source is mandatory')
                self.assertRegex(source('tests/' + suite + '.cs'), r'public\s+static\s+int\s+Main\s*\(')
        # Follow target -> source -> compiler entry point -> actual process result.
        self.assertIn("('tests\\' + $Target + '.cs')", build)
        self.assertIn("('/main:Dafeiyu.Tests.' + $Target)", build)
        compile_at = build.index("& $compiler ('@' + $responseFile)")
        execute_at = build.index('& $destination @TestArguments', compile_at)
        self.assertIn("$TestArguments -contains '--compile-only'", build[compile_at:execute_at])
        self.assertRegex(build[execute_at:], r'(?s)& \$destination @TestArguments\s+if \(\$LASTEXITCODE -ne 0\) \{ throw')

    def test_portable_focused_flags_return_compiled_test_results(self):
        project = ET.fromstring(source(PORTABLE_PROJECT))
        compiled = {str((ROOT / 'desktop/DeskPet.Tests' / name).resolve())
                    for item in project.iter('Compile') for name in item.get('Include', '').split(';')}
        self.assertEqual('Dafeiyu.Tests.PortableTests', project.findtext('./PropertyGroup/StartupObject'))
        runner = source('desktop/DeskPet.Tests/PortableTests.cs')
        for suite, flag in CORE.items():
            with self.subTest(suite=suite):
                self.assertIn(str((ROOT / 'tests' / (suite + '.cs')).resolve()), compiled)
                self.assertTrue(focused_route(runner, flag, suite), flag + ' must return ' + suite + '.Main')

    def test_capture_keeps_platform_specific_native_work_out_of_portable_tests(self):
        project = ET.fromstring(source(PORTABLE_PROJECT))
        self.assertIn('DESKTOP_OBSERVATION_PORTABLE', project.findtext('./PropertyGroup/DefineConstants'))
        fixture = source('tests/DesktopObservationCaptureTests.cs')
        self.assertRegex(fixture, r'#if !DESKTOP_OBSERVATION_PORTABLE\s+Native\(\);\s+#endif')
        self.assertNotIn('DesktopObservationCompatibilityTests', source(PORTABLE_PROJECT))
        self.assertNotIn('DesktopObservationCompatibilityTests', source('desktop/DeskPet.Tests/PortableTests.cs'))

    def test_ci_runs_the_registration_guard_with_failure_propagation(self):
        for name, windows in (('check.yml', True), ('macos.yml', False)):
            with self.subTest(workflow=name):
                blocks = run_blocks(source('.github/workflows/' + name))
                self.assertTrue(checked_command(blocks, r'python3? tests/ProactiveRegistrationTests\.py(?: .*?)?', windows))

    def test_windows_ci_executes_every_native_target_without_compile_only(self):
        blocks = run_blocks(source('.github/workflows/check.yml'))
        for suite in CORE:
            with self.subTest(suite=suite):
                command = r'powershell -NoProfile -ExecutionPolicy Bypass -File \.\\Build\.ps1 -Target ' + suite
                self.assertTrue(checked_command(blocks, command, True), suite + ' needs an executed Windows target')

    def test_both_ci_workflows_execute_built_portable_focused_flags(self):
        for name, windows in (('check.yml', True), ('macos.yml', False)):
            workflow = source('.github/workflows/' + name)
            blocks = run_blocks(workflow)
            for suite, flag in CORE.items():
                with self.subTest(workflow=name, suite=suite):
                    command = r'dotnet ' + re.escape(PORTABLE_DLL + ' ' + flag) + r'(?: 2>&1)?(?: \| tee .*?)?'
                    self.assertTrue(checked_command(blocks, command, windows), flag + ' must execute and propagate failures')
                    build = 'dotnet build ' + PORTABLE_PROJECT + ' -c Release'
                    self.assertIn(build, workflow)
                    self.assertLess(workflow.index(build), workflow.index('dotnet ' + PORTABLE_DLL + ' ' + flag))

    def test_macos_managed_observation_has_a_focused_failure_propagating_route(self):
        runner = source('desktop/DeskPet.Platform.Mac/Tests/Program.cs')
        branch = runner.split('if (args.Contains("--observation-only"))', 1)[1].split('var checks', 1)[0]
        self.assertIn('DesktopObservationCaptureTests.Run().GetAwaiter().GetResult()', branch)
        self.assertIn('Environment.ExitCode = 1', branch)
        self.assertIn('return;', branch)
        project = ET.fromstring(source(MAC_PROJECT))
        self.assertNotEqual('false', project.findtext('./PropertyGroup/EnableDefaultCompileItems'))
        self.assertTrue((ROOT / 'desktop/DeskPet.Platform.Mac/Tests/DesktopObservationCaptureTests.cs').is_file())
        workflow = source('.github/workflows/macos.yml')
        command = r'dotnet ' + re.escape(MAC_DLL + ' --observation-only') + r' 2>&1 \| tee .*'
        self.assertTrue(checked_command(run_blocks(workflow), command, False))
        self.assertLess(workflow.index('dotnet build ' + MAC_PROJECT), workflow.index('dotnet ' + MAC_DLL + ' --observation-only'))

    def test_macos_native_observation_test_is_built_and_executed(self):
        workflow = source('.github/workflows/macos.yml')
        self.assertTrue(checked_command(run_blocks(workflow), r'bash desktop/DeskPet\.Platform\.Mac/native/self-test\.sh .*', False))
        native = source('desktop/DeskPet.Platform.Mac/native/self-test.sh')
        self.assertIn('bash build.sh "$arch"', native)
        self.assertIn('native_library_self_test.m -o build/native_library_self_test', native)
        self.assertIn('subprocess.run([sys.argv[1], "--audio-smoke"], check=True, timeout=60)', native)
        self.assertIn('-dynamiclib DeskPetMac.m DeskPetObservation.m', source('desktop/DeskPet.Platform.Mac/native/build.sh'))
        self.assertRegex(source('desktop/DeskPet.Platform.Mac/native/native_library_self_test.m'), r'Check\(dp_observation_self_test\(\) == 1,')
        # No foreground third-party app capture is eligible for unattended CI.
        for name in ('check.yml', 'macos.yml'):
            self.assertNotIn('DesktopObservationCompatibilityTests', '\n'.join('\n'.join(b) for b in run_blocks(source('.github/workflows/' + name))))

    def test_guard_rejects_comment_only_compilation_only_and_swallowed_results(self):
        command = r'powershell -NoProfile -ExecutionPolicy Bypass -File \.\\Build\.ps1 -Target ImageModelResolverTests'
        live = 'powershell -NoProfile -ExecutionPolicy Bypass -File .\\Build.ps1 -Target ImageModelResolverTests'
        checked = "if ($LASTEXITCODE) { throw 'failed' }"
        self.assertTrue(checked_command([[live, checked]], command, True))
        self.assertFalse(checked_command([[live + ' -TestArguments --compile-only', checked]], command, True))
        self.assertFalse(checked_command([[live]], command, True))
        self.assertFalse(checked_command(run_blocks('run: |\n  # ' + live + '\n  ' + checked), command, True))
        portable = 'dotnet test.dll --focused | tee report.log'
        self.assertFalse(checked_command([['set -eu', portable]], re.escape(portable), False))
        self.assertTrue(checked_command([['set -euo pipefail', portable]], re.escape(portable), False))
        good = 'if(args.Length==1&&args[0]=="--focused")return Suite.Main();'
        self.assertTrue(focused_route(good, '--focused', 'Suite'))
        self.assertFalse(focused_route(good.replace('Suite.Main()', '0'), '--focused', 'Suite'))
        self.assertFalse(focused_route('// ' + good, '--focused', 'Suite'))

    def test_guard_catches_broken_source_to_runner_and_ci_paths(self):
        # Mutate real inputs in memory: these mistakes must fail the repo checks,
        # even though a suite name can still appear elsewhere in the source.
        mutations = (
            (PORTABLE_PROJECT, '../../tests/ImageModelResolverTests.cs', '../../tests/NotRegistered.cs',
             'test_portable_focused_flags_return_compiled_test_results'),
            ('desktop/DeskPet.Tests/PortableTests.cs', 'return ImageModelResolverTests.Main();', 'return 0;',
             'test_portable_focused_flags_return_compiled_test_results'),
            ('.github/workflows/check.yml', '-Target ImageModelResolverTests', '-Target CoreTests',
             'test_windows_ci_executes_every_native_target_without_compile_only'),
            ('.github/workflows/macos.yml', PORTABLE_DLL + ' --image-model-resolver-only', PORTABLE_DLL + ' --not-a-suite',
             'test_both_ci_workflows_execute_built_portable_focused_flags'),
        )
        read = source
        for path, before, after, check in mutations:
            with self.subTest(path=path, check=check):
                original = read(path)
                self.assertIn(before, original)
                def mutated(candidate):
                    return original.replace(before, after) if candidate == path else read(candidate)
                result = unittest.TestResult()
                with patch(__name__ + '.source', side_effect=mutated):
                    RegistrationTests(check).run(result)
                self.assertFalse(result.wasSuccessful(), 'registration omission escaped: ' + path)
                self.assertFalse(result.errors, 'mutation must fail a contract assertion, not crash')


if __name__ == '__main__':
    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=2).run(
        unittest.defaultTestLoader.loadTestsFromTestCase(RegistrationTests))
    raise SystemExit(0 if result.wasSuccessful() else 1)
