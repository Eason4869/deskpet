"""A diagnostic wrapper must preserve failing native process results."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1]

@unittest.skipUnless(os.name=='nt','Windows PowerShell and Framework compiler required')
class RunnerTests(unittest.TestCase):
    def test_diagnostics_never_turn_a_failed_audit_into_success(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'work') as folder:
            root=Path(folder)
            compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            for code in (7,0):
                source=root/'fixture.cs';program=root/'fixture.exe';output=root/str(code)
                source.write_text('class Fixture{static int Main(){System.Console.Error.WriteLine("fixture diagnostic");return '+str(code)+';}}',encoding='utf-8')
                subprocess.run([str(compiler),'/nologo','/out:'+str(program),str(source)],capture_output=True,check=True)
                result=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'tools/run_wpf_language_audit.ps1'),'-Executable',str(program),'-Locale','hi_IN','-OutputDirectory',str(output)],capture_output=True)
                self.assertEqual(result.returncode==0,code==0)
                metadata=json.loads((output/'process.json').read_text(encoding='utf-8-sig'))
                self.assertEqual(metadata['exit_code'],code)
                self.assertEqual([run['theme'] for run in metadata['runs']],['light','dark'] if code==0 else ['light'])
                self.assertEqual(metadata['exit_hex'],f'{code:08X}')
                self.assertIn('fixture diagnostic',(output/'process.log').read_text(encoding='utf-16'))

    def test_navigation_preserves_both_theme_runs_and_native_failure(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'work') as folder:
            root=Path(folder)
            compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            for failed_theme in ('none','light','dark'):
                source=root/'navigation.cs';program=root/'navigation.exe';output=root/failed_theme
                source.write_text('class Fixture{static int Main(string[] args){System.Console.WriteLine(System.String.Join("|",args));foreach(var arg in args)if(arg=="--theme='+failed_theme+'")return unchecked((int)0xC0000409);return 0;}}',encoding='utf-8')
                subprocess.run([str(compiler),'/nologo','/out:'+str(program),str(source)],capture_output=True,check=True)
                result=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'tools/run_wpf_navigation_check.ps1'),'-Executable',str(program),'-Locale','ko_KR','-OutputDirectory',str(output)],capture_output=True)
                self.assertEqual(result.returncode==0,failed_theme=='none')
                metadata=json.loads((output/'process.json').read_text(encoding='utf-8-sig'))
                expected=['light'] if failed_theme=='light' else ['light','dark']
                self.assertEqual([run['theme'] for run in metadata['runs']],expected)
                self.assertEqual(metadata['exit_hex'],'00000000' if failed_theme=='none' else 'C0000409')
                log=(output/'process.log').read_text(encoding='utf-16')
                for theme in expected:self.assertIn('--theme='+theme,log)
                self.assertIn('--layout-boundary',log)

if __name__=='__main__':unittest.main()
