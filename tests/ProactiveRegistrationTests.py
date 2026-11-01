"""Guard executable core/UI test paths; behavior still runs separately in CI.

Real foreground third-party application compatibility remains manual acceptance.
"""
from pathlib import Path
import fnmatch
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
WINDOWS_UI = ('DesktopObservationHostTests', 'DesktopObservationUiTests', 'ProactiveConversationUiTests')
AVALONIA_UI = {
    '--proactive-host-only': 'RunHost',
    '--proactive-admission-only': 'RunAdmission',
    '--proactive-reply-only': 'RunReply',
    '--proactive-settings-only': 'RunSettings',
}
UI_PROJECT = 'desktop/DeskPet.Desktop.Tests/DeskPet.Desktop.Tests.csproj'
UI_DLL = 'desktop/DeskPet.Desktop.Tests/bin/Release/net10.0/DeskPet.Desktop.Tests.dll'
UI_RUNNER = 'desktop/DeskPet.Desktop.Tests/Program.cs'
UI_CHECKS = 'desktop/DeskPet.Desktop.Tests/ProactiveConversationChecks.cs'
COMPOSER_CHECKS = 'desktop/DeskPet.Desktop.Tests/ComposerLayoutUiChecks.cs'
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


def csharp_code(text):
    tokens = r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/'
    return re.sub(tokens, lambda m: '' if m[0].startswith(('//', '/*')) else m[0], text, flags=re.S)


def braced_body(text, opening):
    """Read one C# body, ignoring braces in strings."""
    depth = 0
    for token in re.finditer(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|[{}]', text[opening:]):
        if token[0] == '{':
            depth += 1
        elif token[0] == '}':
            depth -= 1
            if depth == 0:
                return text[opening + 1:opening + token.start()]
    return ''


def method_body(text, name):
    text = csharp_code(text)
    match = re.search(r'\b(?:public|internal)\s+static\s+int\s+' + re.escape(name) + r'\s*\([^)]*\)\s*{', text)
    return braced_body(text, match.end() - 1) if match else ''


def compiled_ui_source(path):
    """Support SDK implicit Compile items as well as explicit includes/removes."""
    project = ET.fromstring(source(UI_PROJECT))
    inherited = ET.fromstring(source('desktop/Directory.Build.props'))
    relative = str(Path(path).relative_to('desktop/DeskPet.Desktop.Tests')).replace('\\', '/')
    defaults = {'EnableDefaultItems': True, 'EnableDefaultCompileItems': True}
    for tree in (inherited, project):
        for prop in ('EnableDefaultItems', 'EnableDefaultCompileItems'):
            for item in tree.iter(prop):
                defaults[prop] = (item.text or '').lower() != 'false'
    compiled = project.get('Sdk') == 'Microsoft.NET.Sdk' and all(defaults.values())
    for item in project.iter('Compile'):
        for action in ('Include', 'Remove'):
            for pattern in item.get(action, '').replace('\\', '/').split(';'):
                if pattern and (fnmatch.fnmatchcase(relative, pattern) or
                                (pattern.startswith('**/') and fnmatch.fnmatchcase(relative, pattern[3:]))):
                    compiled = action == 'Include'
    return compiled


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

    def required_source(self, path):
        try:
            return csharp_code(source(path))
        except FileNotFoundError:
            self.fail(path + ' source is mandatory')

    def test_both_ci_workflows_execute_observation_locale_audit(self):
        fixture = source('tests/DesktopObservationLocaleAudit.py')
        self.assertIn("value.count('{0}')", fixture)
        self.assertIn('body.count(image) == body.count(text) == 1', fixture)
        for name, windows in (('check.yml', True), ('macos.yml', False)):
            workflow = source('.github/workflows/' + name)
            if not windows:
                workflow = workflow.split('\n  preview:\n', 1)[1]
            command = ('python ' if windows else 'python3 ') + 'tests/DesktopObservationLocaleAudit.py'
            self.assertTrue(checked_command(run_blocks(workflow), re.escape(command), windows), name)

    def test_windows_ui_targets_compile_entries_and_propagate_failures(self):
        build = source('Build.ps1')
        targets = re.search(r'ValidateSet\((.*?)\)', build)[1]
        for suite in WINDOWS_UI + ('ExplicitSettingsSaveTests',):
            with self.subTest(suite=suite):
                self.assertTrue("'" + suite + "'" in targets, suite + ' must be a Build target')
                fixture = self.required_source('tests/' + suite + '.cs')
                body = method_body(fixture, 'Main')
                self.assertIn('new Application', body)
                self.assertRegex(body, r'(?s)catch\s*\(Exception \w+\)\s*{[^{}]*return 1;')
        # The core guard follows the same generic source/compiler/execution path.
        for flag in ('--image-picker', '--owner-draft'):
            fixture = self.required_source('tests/ExplicitSettingsSaveTests.cs')
            matches = list(re.finditer(r'if\s*\(args.Contains\("' + flag + r'"\)\)\s*{', fixture))
            branches = [braced_body(fixture, match.end() - 1) for match in matches]
            self.assertTrue(any('Check(' in body and 'return 0;' in body for body in branches),
                            flag + ' must execute assertions, not just prepare settings')

    def test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks(self):
        runner = self.required_source(UI_RUNNER)
        fixture = self.required_source(UI_CHECKS)
        for path in (UI_RUNNER, UI_CHECKS,
                     'desktop/DeskPet.Desktop.Tests/WorkBindingUiChecks.cs',
                     'desktop/DeskPet.Desktop.Tests/FeatureNavigationUiChecks.cs'):
            with self.subTest(path=path):
                self.required_source(path)
                self.assertTrue(compiled_ui_source(path), path + ' must be compiled')
        match = re.search(r'if\s*\(args.Any\(arg=>arg.StartsWith\("--proactive-"\)\)\)\s*{', runner)
        self.assertIsNotNone(match, 'focused proactive dispatcher is mandatory')
        branch = braced_body(runner, match.end() - 1)
        self.assertRegex(branch, r'catch\(Exception \w+\)\{[^{}]*Environment.ExitCode\s*=\s*1;')
        self.assertRegex(branch, r'return;\s*$')
        self.assertNotIn('return;', branch.split('int count=', 1)[0],
                         'focused dispatcher cannot return before running checks')
        for flag, method in AVALONIA_UI.items():
            with self.subTest(flag=flag):
                self.assertRegex(branch, r'args.Contains\("' + flag + r'"\)\s*\?\s*ProactiveConversationChecks\.' + method + r'\(')
                body = method_body(fixture, method)
                self.assertIn('Check(', body)
                self.assertIn('throw new Exception', body)
                self.assertRegex(body, r'return checks;\s*$')
                self.assertNotRegex(body, r'^\s*return\b')
        for flag, suite in (('--work-binding-ui=', 'WorkBindingUiChecks'),
                            ('--feature-navigation-ui=', 'FeatureNavigationUiChecks')):
            match = re.search(r'if\s*\(args.Any\([^;\n]*StartsWith\("' + flag + r'"\)[^;\n]*?\)\s*{', runner)
            self.assertIsNotNone(match, flag + ' needs a real dispatcher')
            self.assertIn(suite + '.Run(', braced_body(runner, match.end() - 1))
            body = method_body(self.required_source('desktop/DeskPet.Desktop.Tests/' + suite + '.cs'), 'Run')
            self.assertIn('Check(', body)
            self.assertIn('throw new Exception', body)

    def test_windows_ci_executes_ui_targets_and_explicit_image_modes(self):
        workflow = source('.github/workflows/check.yml')
        blocks = run_blocks(workflow)
        baseline = re.search(r'(?s)foreach \(\$target in @\((.*?)\)\) \{(.*?)if \(\$failedTargets.Count', workflow)
        self.assertIsNotNone(baseline, 'default Windows target execution is mandatory')
        self.assertIn("'ExplicitSettingsSaveTests'", baseline[1])
        self.assertIn('& .\\Build.ps1 -Target $target', baseline[2])
        self.assertIn('if ($LASTEXITCODE -ne 0) { throw', baseline[2])
        for suite in WINDOWS_UI:
            with self.subTest(suite=suite):
                self.assertTrue(checked_command(blocks,
                    r'powershell -NoProfile -ExecutionPolicy Bypass -File \.\\Build\.ps1 -Target ' + suite, True))
        # The existing default suite builds this executable; reuse it for the new modes.
        for flag in ('--image-picker', '--owner-draft'):
            command = '& work/ExplicitSettingsSaveTests.exe ' + flag
            self.assertTrue(checked_command(blocks, re.escape(command), True))
            self.assertLess(baseline.start(), workflow.index(command))

    def test_both_ci_workflows_execute_built_avalonia_ui_flags(self):
        for name, windows in (('check.yml', True), ('macos.yml', False)):
            workflow = source('.github/workflows/' + name)
            if not windows:
                self.assertIn('\n  preview:\n', workflow)
                workflow = workflow.split('\n  preview:\n', 1)[1]
            blocks = run_blocks(workflow)
            build = 'dotnet build ' + UI_PROJECT + ' -c Release'
            for flag in AVALONIA_UI:
                with self.subTest(workflow=name, flag=flag):
                    command = 'dotnet ' + UI_DLL + ' ' + flag
                    self.assertTrue(checked_command(blocks, re.escape(command) + r'(?: \| tee .*?)?', windows))
                    self.assertLess(workflow.index(build), workflow.index(command))
            # Locale routes call the same WorkBindingUiChecks.Run as --work-binding-ui-only.
            work = ('dotnet ' + UI_DLL + " ('--work-binding-ui='+$locale)" if windows else
                    'dotnet ' + UI_DLL + ' --work-binding-ui="$locale"')
            navigation = ('dotnet ' + UI_DLL + " ('--feature-navigation-ui='+$locale)" if windows else
                          'dotnet ' + UI_DLL + ' --feature-navigation-ui=zh_CN')
            for command in (work, navigation):
                self.assertTrue(checked_command(blocks, re.escape(command) + r'(?: \| tee .*?)?', windows))
                self.assertLess(workflow.index(build), workflow.index(command))
            self.assertIn("foreach ($locale in @('zh_CN'," if windows else 'for locale in zh_CN ', workflow)
            if not windows:
                self.assertIn('- arch: arm64', workflow)
                self.assertIn('- arch: x86_64', workflow)
                self.assertIn('runs-on: ${{ matrix.runner }}', workflow)

    def test_composer_geometry_compiles_and_runs_first_in_the_default_ui_suite(self):
        fixture = self.required_source(COMPOSER_CHECKS)
        self.assertTrue(compiled_ui_source(COMPOSER_CHECKS), 'composer fixture must be SDK-compiled')
        body = method_body(fixture, 'Run')
        self.assertRegex(body, r'if\s*\(!value\)\s*throw new Exception')
        self.assertRegex(body, r'Check\(s\.Model\.Width\s*>\s*0[^;]*s\.Model\.Right\s*<=\s*s\.Fallback\.Left')
        self.assertRegex(body, r'Check\(s\.Width\s*!=\s*390[^;]*s\.Model\.Width\s*>=\s*60')
        self.assertRegex(body, r'Check\(samples\[1\]\.Model\.Width\s*<\s*samples\[0\]\.Model\.Width')
        self.assertIn('return assertions;', body)
        self.assertNotRegex(body, r'^\s*return\b')
        runner = self.required_source(UI_RUNNER)
        outer = re.search(r'\btry\s*{', runner[runner.index('Directory.CreateDirectory(directory);'):])
        offset = runner.index('Directory.CreateDirectory(directory);')
        self.assertIsNotNone(outer)
        default = braced_body(runner, offset + outer.end() - 1)
        # Drop nested branch bodies: a focused-only call cannot register a default check.
        top = []
        depth = 0
        for token in re.finditer(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|[{}]|[^{}"]+', default):
            if token[0] == '{':
                depth += 1
            elif token[0] == '}':
                depth -= 1
            elif depth == 0:
                top.append(token[0])
        top = ''.join(top)
        call = re.search(r'\bassertions\s*\+=\s*ComposerLayoutUiChecks\.Run\s*\(', top)
        self.assertIsNotNone(call, 'default UI must accumulate real composer assertions')
        self.assertNotRegex(top[:call.start()], r'\breturn\s*;')
        first = re.search(r'\bassertions\s*\+=', top)
        self.assertEqual(first.start(), call.start(), 'geometry must run before other default UI checks')
        self.assertRegex(runner[offset + outer.end() + len(default):], r'^}\s*finally\s*{',
                         'default assertion exceptions must escape rather than be swallowed')

    def test_both_ci_workflows_build_and_execute_default_ui_with_failure_propagation(self):
        for name, windows in (('check.yml', True), ('macos.yml', False)):
            with self.subTest(workflow=name):
                workflow = source('.github/workflows/' + name)
                if not windows:
                    workflow = workflow.split('\n  preview:\n', 1)[1]
                blocks = run_blocks(workflow)
                build = r'dotnet build ' + re.escape(UI_PROJECT) + r' -c Release(?: [^|]*)?'
                if windows:
                    candidates = []
                    for block in blocks:
                        text = '\n'.join(block)
                        alias = re.search(r"\$(\w+)\s*=\s*'" + re.escape(UI_DLL) + "'", text)
                        if alias is None:
                            continue
                        loop = re.search(r'foreach\s*\(\$(\w+)\s+in\s+@\(([^)]*)\)\)\s*{', text)
                        if loop is None or not re.search(r"(?:^|,)\s*''\s*(?:,|$)", loop[2]):
                            continue
                        body = braced_body(text, loop.end() - 1)
                        var, dll = re.escape(loop[1]), re.escape(alias[1])
                        dispatch = r'if\s*\(\$' + var + r'\)\s*{\s*dotnet \$' + dll + r' \$' + var + r'\s*}\s*else\s*{\s*dotnet \$' + dll + r'\s*}'
                        execution = re.search(dispatch, body)
                        if execution is not None and re.match(r"\s*if \(\$LASTEXITCODE(?: -ne 0)?\) \{ throw ", body[execution.end():]):
                            candidates.append((block, loop.start(), text))
                    self.assertTrue(candidates, 'Windows must execute the empty-argument default UI and propagate failure')
                    self.assertTrue(any(checked_command([block], build, True) and
                                        re.search(build, text).start() < at for block, at, text in candidates),
                                    'checked UI build must precede default execution')
                else:
                    command = r'dotnet ' + re.escape(UI_DLL) + r'(?: 2>&1)?(?: \| tee [^;]+)?'
                    self.assertTrue(any(checked_command([block], command, False) and
                                        checked_command([block], build, False) and
                                        next(i for i, line in enumerate(block) if re.fullmatch(build, line)) <
                                        next(i for i, line in enumerate(block) if re.fullmatch(command, line))
                                        for block in blocks), 'macOS must build then execute the default UI with pipefail')

    def test_composer_guard_rejects_missing_source_compile_runner_and_default_ci(self):
        self.test_composer_geometry_compiles_and_runs_first_in_the_default_ui_suite()
        self.test_both_ci_workflows_build_and_execute_default_ui_with_failure_propagation()
        source_check = 'test_composer_geometry_compiles_and_runs_first_in_the_default_ui_suite'
        ci_check = 'test_both_ci_workflows_build_and_execute_default_ui_with_failure_propagation'
        mutations = [
            (COMPOSER_CHECKS, None, None, source_check),
            (UI_PROJECT, r'</Project>', '<ItemGroup><Compile Remove="ComposerLayoutUiChecks.cs" /></ItemGroup></Project>', source_check),
            (UI_PROJECT, r'</Project>', '<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>', source_check),
            (COMPOSER_CHECKS, r'if\s*\(!value\)\s*throw new Exception', 'if (false) throw new Exception', source_check),
            (COMPOSER_CHECKS, r's\.Model\.Width\s*>\s*0', 'true', source_check),
            (UI_RUNNER, r'assertions\s*\+=\s*ComposerLayoutUiChecks\.Run\([^;]+;', 'assertions += 0;', source_check),
            (UI_RUNNER, r'assertions\s*\+=\s*ComposerLayoutUiChecks\.Run\([^;]+;', '// ComposerLayoutUiChecks.Run(directory);', source_check),
            (UI_RUNNER, r'assertions\s*\+=\s*(ComposerLayoutUiChecks\.Run\([^;]+;)', r'\1', source_check),
            (UI_RUNNER, r'(assertions\s*\+=\s*ComposerLayoutUiChecks\.Run\([^;]+;)', r'if(args.Contains("--focused")){\1}', source_check),
            (UI_RUNNER, r'(assertions\s*\+=\s*ComposerLayoutUiChecks\.Run\([^;]+;)', r'return;\1', source_check),
            ('.github/workflows/check.yml', r"@\('', '--data-management-ui-only'", "@('--data-management-ui-only'", ci_check),
            ('.github/workflows/check.yml', r'else\s*{\s*dotnet \$testDll\s*}', 'else { dotnet $testDll --proactive-host-only }', ci_check),
            ('.github/workflows/check.yml', r"if \(\$LASTEXITCODE\) { throw \('Desktop acceptance failed: ' \+ \$arguments\) }", "Write-Host 'ignored'", ci_check),
            ('.github/workflows/check.yml', r'dotnet build ' + re.escape(UI_PROJECT) + r'[^\n]+', '# removed UI build', ci_check),
            ('.github/workflows/macos.yml', r'dotnet ' + re.escape(UI_DLL) + r' \| tee "\$REPORT_ROOT/headless-ui.log"', 'dotnet ' + UI_DLL + ' --proactive-host-only | tee "$REPORT_ROOT/headless-ui.log"', ci_check),
            ('.github/workflows/macos.yml', r'dotnet build ' + re.escape(UI_PROJECT) + r' -c Release[^\n]+', '# removed UI build', ci_check),
            ('.github/workflows/macos.yml', r'set -euo pipefail', 'set -eu', ci_check),
        ]
        read = source
        for path, pattern, replacement, check in mutations:
            with self.subTest(path=path, mutation=pattern):
                original = read(path)
                if pattern is not None:
                    changed, count = re.subn(pattern, replacement, original)
                    self.assertGreater(count, 0, 'mutation must change an actual input')
                def mutated(candidate):
                    if candidate != path:
                        return read(candidate)
                    if pattern is None:
                        raise FileNotFoundError(candidate)
                    return changed
                result = unittest.TestResult()
                with patch(__name__ + '.source', side_effect=mutated):
                    RegistrationTests(check).run(result)
                self.assertFalse(result.wasSuccessful(), 'composer registration omission escaped: ' + str(pattern))
                self.assertFalse(result.errors, 'mutation must fail an assertion rather than crash')
        original = read(COMPOSER_CHECKS)
        body = method_body(original, 'Run')
        self.assertTrue(body)
        with patch(__name__ + '.source', side_effect=lambda path:
                   original.replace(body, 'return 0;') if path == COMPOSER_CHECKS else read(path)):
            result = unittest.TestResult()
            RegistrationTests(source_check).run(result)
        self.assertFalse(result.wasSuccessful(), 'constant-success composer stub must fail')
        self.assertFalse(result.errors)

    def test_ui_guard_rejects_compile_dispatch_failure_and_ci_omissions(self):
        self.required_source(UI_CHECKS)
        mutations = (
            ('.github/workflows/check.yml', 'python tests/DesktopObservationLocaleAudit.py', '# python tests/DesktopObservationLocaleAudit.py',
             'test_both_ci_workflows_execute_observation_locale_audit'),
            ('.github/workflows/check.yml', "if ($LASTEXITCODE) { throw 'Observation locale audit failed' }", "if ($LASTEXITCODE) { Write-Host 'ignored' }",
             'test_both_ci_workflows_execute_observation_locale_audit'),
            ('.github/workflows/macos.yml', 'python3 tests/DesktopObservationLocaleAudit.py', '# python3 tests/DesktopObservationLocaleAudit.py',
             'test_both_ci_workflows_execute_observation_locale_audit'),
            (UI_PROJECT, '</Project>', '<ItemGroup><Compile Remove="ProactiveConversationChecks.cs" /></ItemGroup></Project>',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            (UI_PROJECT, '</Project>', '<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            (UI_RUNNER, 'ProactiveConversationChecks.RunHost()', '0',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            (UI_RUNNER, 'args.Contains("--proactive-host-only")?ProactiveConversationChecks.RunHost()',
             'false?ProactiveConversationChecks.RunHost()',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            (UI_RUNNER, 'Environment.ExitCode=1;', 'Environment.ExitCode=0;',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            (UI_RUNNER, 'int count=args.Contains("--proactive-host-only")', 'return;int count=args.Contains("--proactive-host-only")',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            (UI_RUNNER, 'WorkBindingUiChecks.Run(', 'RemovedWorkBindingChecks.Run(',
             'test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks'),
            ('Build.ps1', "'DesktopObservationHostTests',", '',
             'test_windows_ui_targets_compile_entries_and_propagate_failures'),
            ('.github/workflows/check.yml', '-Target DesktopObservationUiTests', '-Target CoreTests',
             'test_windows_ci_executes_ui_targets_and_explicit_image_modes'),
            ('.github/workflows/check.yml', '-Target DesktopObservationHostTests',
             '-Target DesktopObservationHostTests -TestArguments --compile-only',
             'test_windows_ci_executes_ui_targets_and_explicit_image_modes'),
            ('.github/workflows/check.yml', "if ($LASTEXITCODE) { throw 'DesktopObservationUiTests failed' }",
             "Write-Output 'failure swallowed'",
             'test_windows_ci_executes_ui_targets_and_explicit_image_modes'),
            ('.github/workflows/check.yml', "'ExplicitSettingsSaveTests',", '',
             'test_windows_ci_executes_ui_targets_and_explicit_image_modes'),
            ('.github/workflows/check.yml', 'ExplicitSettingsSaveTests.exe --owner-draft', 'ExplicitSettingsSaveTests.exe',
             'test_windows_ci_executes_ui_targets_and_explicit_image_modes'),
            ('.github/workflows/check.yml', UI_DLL + ' --proactive-settings-only', UI_DLL + ' --not-a-suite',
             'test_both_ci_workflows_execute_built_avalonia_ui_flags'),
            ('.github/workflows/macos.yml', UI_DLL + ' --proactive-reply-only', UI_DLL + ' --not-a-suite',
             'test_both_ci_workflows_execute_built_avalonia_ui_flags'),
            ('.github/workflows/macos.yml', '--feature-navigation-ui=zh_CN', '--feature-navigation-ui=en_US',
             'test_both_ci_workflows_execute_built_avalonia_ui_flags'),
            ('.github/workflows/macos.yml', '- arch: x86_64', '- arch: removed',
             'test_both_ci_workflows_execute_built_avalonia_ui_flags'),
        )
        read = source
        for path, before, after, check in mutations:
            with self.subTest(path=path, mutation=before):
                original = read(path)
                self.assertIn(before, original)
                def mutated(candidate):
                    return original.replace(before, after) if candidate == path else read(candidate)
                result = unittest.TestResult()
                with patch(__name__ + '.source', side_effect=mutated):
                    RegistrationTests(check).run(result)
                self.assertFalse(result.wasSuccessful(), 'UI registration omission escaped: ' + before)
                self.assertFalse(result.errors, 'mutation must fail a contract assertion, not crash')
        # A real check class replaced by a constant-success stub must also fail.
        original = read(UI_CHECKS)
        body = method_body(original, 'RunReply')
        self.assertTrue(body)
        with patch(__name__ + '.source', side_effect=lambda path:
                   original.replace(body, 'return 0;') if path == UI_CHECKS else read(path)):
            result = unittest.TestResult()
            RegistrationTests('test_avalonia_ui_sources_compile_and_dispatch_real_failing_checks').run(result)
        self.assertFalse(result.wasSuccessful())
        self.assertFalse(result.errors)

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
