"""Check S12 runner/CI registration; this does not replace behavioral acceptance."""
from pathlib import Path
import re
import sys
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
LOGIC = ('AgentJobContractTests', 'AgentJobStoreTests', 'AgentJobLogTests',
         'AgentJobManagerTests', 'AgentJobLifecycleTests', 'AgentJobViewStateTests', 'AgentJobToolTests')
WINDOWS = ('AgentJobIntegrationTests', 'AgentJobSessionTests',
           'AgentJobProcessTests', 'AgentJobSandboxTests')


def source(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')


def method_body(text, name):
    match = re.search(r'static int ' + re.escape(name) + r'\([^)]*\)\s*\{', text)
    if match is None:
        return ''
    start = match.end()
    depth = 1
    for end in range(start, len(text)):
        depth += (text[end] == '{') - (text[end] == '}')
        if depth == 0:
            return text[start:end]
    raise AssertionError('Unclosed runner method: ' + name)


class RegistrationTests(unittest.TestCase):
    def test_avalonia_persistent_bindings_run_in_default_and_focused_routes(self):
        runner=source('desktop/DeskPet.Desktop.Tests/Program.cs')
        self.assertIn('--work-binding-ui-only',runner)
        self.assertIn('assertions+=WorkBindingUiChecks.Run',runner)
        self.assertIn('--work-binding-ui=',runner)
        fixture=source('desktop/DeskPet.Desktop.Tests/WorkBindingUiChecks.cs')
        self.assertIn('application restart restores the exact persisted',fixture)
        self.assertIn('ordinary encrypted history',fixture)
        self.assertIn('second host cannot bind',fixture)

    def test_wpf_lifecycle_route_keeps_independent_host_fixtures(self):
        build=source('Build.ps1')
        self.assertIn("'AgentJobLifecycleUiTests'",build)
        self.assertIn('tests/AgentJobExitHost.cs',build)
        self.assertIn("'AgentJobLifecycleUiTests'",source('.github/workflows/check.yml'))
        fixture=source('tests/AgentJobLifecycleUiTests.cs')
        self.assertLess(fixture.index('AgentJobExitHost.Run(args[1],args[2])'),fixture.index('app=new Application'))
        for mode in ('normal','tray','update','kill','held'):
            self.assertIn('"'+mode+'"',fixture)
        self.assertNotIn('AgentJobLifecycleUiTests.cs',source('desktop/DeskPet.Tests/DeskPet.Tests.csproj'))

    def test_wpf_project_gates_are_registered_without_portable_ui_claims(self):
        self.assertIn("'AgentJobProjectTests'",source('Build.ps1'))
        self.assertIn("'AgentJobProjectTests'",self.windows_ci)
        self.assertNotIn('AgentJobProjectTests.cs',source('desktop/DeskPet.Tests/DeskPet.Tests.csproj'))

    def test_wpf_job_ui_and_real_browser_acceptance_remain_reachable(self):
        self.assertIn("'AgentJobUiTests'",source('Build.ps1'))
        self.assertIn("'AgentJobUiTests'",self.windows_ci)
        self.assertIn("-Target AgentJobUiTests -TestArguments '--native-preview'",self.windows_ci)
        self.assertNotIn('AgentJobUiTests.cs',source('desktop/DeskPet.Tests/DeskPet.Tests.csproj'))
        self.assertNotIn('--native-preview',self.mac_ci)

    def assertRegex(self, text, pattern, msg=None):
        # Keep a registration failure readable instead of dumping whole workflows.
        self.assertIsNotNone(re.search(pattern, text), msg or pattern)

    @classmethod
    def setUpClass(cls):
        cls.runner = source('desktop/DeskPet.Tests/PortableTests.cs')
        cls.windows_ci = source('.github/workflows/check.yml')
        cls.mac_ci = source('.github/workflows/macos.yml')

    def test_framework_targets_and_shared_compile_inputs_include_every_suite(self):
        targets = set(re.findall(r"'([^']+)'", source('Build.ps1').split('[string]$Target', 1)[0]))
        project = ET.fromstring(source('desktop/DeskPet.Tests/DeskPet.Tests.csproj'))
        inputs = {Path(path).name for node in project.findall('.//Compile')
                  for path in node.get('Include', '').split(';')}
        for suite in LOGIC + WINDOWS:
            with self.subTest(suite=suite):
                self.assertIn(suite, targets)
                self.assertIn(suite + '.cs', inputs)
                self.assertRegex(source('tests/' + suite + '.cs'), r'public static int Main\(')
        self.assertIn('AgentJobTestSupport.cs', inputs)

    def test_fixture_dispatch_precedes_regular_argument_parsing(self):
        self.assertLess(self.runner.index('AgentJobProcessTests.RunFixture(args)'),
                        self.runner.index('string report=null'))

    def test_logic_aggregate_is_complete_and_never_invokes_windows_suites(self):
        self.assertRegex(self.runner, r'args\[0\]=="--agent-jobs-only"\)return RunAgentJobLogic\(\)')
        body = method_body(self.runner, 'RunAgentJobLogic')
        registered = set(re.findall(r'(AgentJob\w+Tests)\.(?:Main|RunAll)\(', body))
        self.assertEqual(set(LOGIC), registered)
        self.assertIn('SecretProtection.Configure(protector)', body)

    def test_default_runs_logic_and_windows_integration_without_native_process_suites(self):
        body = method_body(self.runner, 'Run')
        for suite in LOGIC:
            self.assertRegex(body, re.escape(suite) + r'\.(?:Main|RunAll)')
        self.assertRegex(body, r'if\s*\(Environment.OSVersion.Platform==PlatformID.Win32NT\)\s*result\s*\|=\s*AgentJobIntegrationTests.Main\(\)')
        for suite in WINDOWS[1:]:
            self.assertNotIn(suite + '.', body)

    def test_dedicated_windows_routes_fail_explicitly_on_unsupported_platforms(self):
        for flag, method, suites in (
                ('--agent-job-integration-only', 'RunAgentJobIntegration', WINDOWS[:1]),
                ('--agent-jobs-native-only', 'RunAgentJobNative', WINDOWS[1:])):
            with self.subTest(flag=flag):
                self.assertRegex(self.runner, r'args\[0\]=="' + flag + r'"\)return ' + method + r'\(\)')
                body = method_body(self.runner, method)
                self.assertRegex(body, r'if\s*\(Environment.OSVersion.Platform!=PlatformID.Win32NT\)\s*\{[^}]*return 2;')
                self.assertIn('SecretProtection.Configure(protector)', body)
                self.assertEqual(set(suites), set(re.findall(r'(AgentJob\w+Tests)\.(?:Main|RunAll)\(', body)))

    def test_existing_focused_aliases_remain_available(self):
        for flag in ('--agent-job-tools-only', '--agent-job-session-only',
                     '--agent-job-contract-only', '--agent-job-store-only',
                     '--agent-job-manager-only', '--agent-job-view-state-only', '--agent-job-lifecycle-only', '--agent-job-log-only',
                     '--agent-jobs-resources-only'):
            self.assertRegex(self.runner, r'args\[0\]=="' + flag + '"')

    def test_windows_ci_runs_each_new_route_once_and_keeps_framework_targets(self):
        for suite in WINDOWS + ('AgentJobToolTests',):
            self.assertIn("'" + suite + "'", self.windows_ci)
        for flag in ('--agent-jobs-only', '--agent-job-integration-only', '--agent-jobs-native-only'):
            self.assertEqual(1, len(re.findall(r'DeskPet.Tests.dll ' + flag + r'\b', self.windows_ci)), flag)
        self.assertNotRegex(self.windows_ci, r'DeskPet.Tests.dll --agent-job-session-only\b')
        self.assertNotRegex(self.windows_ci, r'DeskPet.Tests.dll --agent-job-tools-only\b')

    def test_both_ci_platforms_execute_registration_and_preserve_job_evidence(self):
        for ci in (self.windows_ci, self.mac_ci):
            self.assertRegex(ci, r'python3? tests/AgentJobRegistrationTests.py')
        self.assertRegex(self.windows_ci, r'(?s)if: always\(\).*?uses: actions/upload-artifact@v4.*?work/agent-jobs-validation/ci/')
        self.assertIn('Start-Transcript -Path work/agent-jobs-validation/ci/framework.log', self.windows_ci)
        self.assertIn('Start-Transcript -Path work/agent-jobs-validation/ci/shared.log', self.windows_ci)
        self.assertIn('finally { Stop-Transcript }', self.windows_ci)
        self.assertRegex(self.mac_ci, r'DeskPet.Tests.dll --agent-jobs-only.*?tee "\$REPORT_ROOT/agent-jobs-logic.log"')
        self.assertRegex(self.mac_ci, r'(?s)if: always\(\).*?uses: actions/upload-artifact@v4.*?work/macos-reports-\$\{\{ matrix.arch \}\}/')

    def test_macos_ci_does_not_claim_windows_process_acceptance(self):
        for flag in ('--agent-jobs-native-only', '--agent-job-integration-only',
                     '--agent-job-session-only', '--agent-jobs-resources-only'):
            self.assertNotIn(flag, self.mac_ci)
        self.assertIn('UnsupportedPlatformNeverExposesJobTools', source('tests/AgentJobToolTests.cs'))


if __name__ == '__main__':
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(RegistrationTests)
    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=2).run(suite)
    raise SystemExit(0 if result.wasSuccessful() else 1)
