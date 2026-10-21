"""Exercise the real runtime staging boundary, including reused input folders."""
from pathlib import Path
import importlib.util
import sys
import tempfile
import unittest
import os
import re
import subprocess
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

class RuntimePackageTests(unittest.TestCase):
    def test_only_explicit_runtime_files_reach_zip_and_inno_staging(self):
        module_path=Path(__file__).resolve().parents[1]/'tools/runtime_package.py'
        self.assertTrue(module_path.is_file(), 'missing common runtime packaging boundary')
        from tools.runtime_package import collect_runtime_files, stage_runtime, write_inno_files
        project=module_path.parents[1]
        with tempfile.TemporaryDirectory(dir=project/'work') as temp:
            root=Path(temp);app=root/'reused';app.mkdir()
            for name in ['桌面伙伴.exe','桌面伙伴.exe.config','DeskPet.Update.exe','DeskPet.Update.exe.config']:
                (app/name).write_bytes(b'fixture')
            for name in ['templates/private.md','previews/obsolete.png','data/settings.json']:
                p=app/name;p.parent.mkdir(exist_ok=True);p.write_text('private')
            plan=collect_runtime_files(project,app)
            destinations={item['destination'] for item in plan}
            self.assertFalse(any(p.startswith(('templates/','previews/','data/')) for p in destinations))
            self.assertIn('docs/third-party-notices.md',destinations)
            stage=root/'fresh'
            stage_runtime(plan,stage)
            write_inno_files(plan,stage,root/'files.iss')
            self.assertEqual({p.relative_to(stage).as_posix() for p in stage.rglob('*') if p.is_file()},destinations)
            self.assertNotIn('recursesubdirs',(root/'files.iss').read_text(encoding='utf-8'))
            self.assertEqual((app/'templates/private.md').read_text(),'private')
            with self.assertRaises((ValueError,FileExistsError)):
                stage_runtime(plan,stage)
            with self.assertRaises(ValueError):
                stage_runtime([{'source':app/'桌面伙伴.exe','destination':'../escaped.exe'}],root/'unsafe')

    @unittest.skipUnless(os.name=='nt','PE identity check uses Windows version resources')
    def test_stale_same_base_application_or_helper_cannot_be_packaged(self):
        from tools.runtime_package import validate_binaries
        project=Path(__file__).resolve().parents[1]
        version=re.search(r'DisplayVersion="([^"]+)"',(project/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig'))[1]
        assembly=re.search(r'AssemblyVersion="([^"]+)"',(project/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig'))[1]
        commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=project,text=True).strip()
        with tempfile.TemporaryDirectory(dir=project/'work') as temp:
            root=Path(temp)
            def compile(name,identity,numeric=assembly):
                source=root/'fixture.cs'
                source.write_text('[assembly:System.Reflection.AssemblyVersion("'+numeric+'")][assembly:System.Reflection.AssemblyInformationalVersion("'+identity+'") ] class Fixture{static void Main(){}}',encoding='utf-8-sig')
                subprocess.run([str(Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'),'/nologo','/out:'+str(root/name),str(source)],check=True,capture_output=True)
            for name in ['桌面伙伴.exe','DeskPet.Update.exe']:compile(name,version+'+'+commit)
            validate_binaries(project,root)
            for name in ['桌面伙伴.exe','DeskPet.Update.exe']:
                compile(name,version+'+'+'f'*40)
                with self.assertRaises(ValueError):validate_binaries(project,root)
                compile(name,version+'+'+commit,'0.0.1.0')
                with self.assertRaises(ValueError):validate_binaries(project,root)
                compile(name,version+'+'+commit)

    def test_retirement_rules_match_only_previously_shipped_paths(self):
        from tools.generate_retired_runtime import records,BASE
        project=Path(__file__).resolve().parents[1]
        if subprocess.run(['git','cat-file','-e',BASE],cwd=project,capture_output=True).returncode:
            self.skipTest('Historical regeneration requires a full Git checkout')
        records=dict(records())
        self.assertIn('previews/avatars/e-bao.svg',records)
        self.assertIn('templates/high-fps-v2/动作表.md',records)
        self.assertNotIn('templates/high-fps-v2/动作表.csv',records)
        self.assertTrue(all('/' not in name[len('previews/'):] or name.startswith('previews/avatars/') for name in records if name.startswith('previews/')))
        generated=(project/'src/RetiredRuntimeFiles.cs').read_text(encoding='utf-8')
        self.assertEqual(dict(re.findall(r'\{"([^"]+)","([a-f0-9]{64})"\}',generated)),records)

if __name__=='__main__':
    unittest.main()
