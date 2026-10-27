"""Exercise the real runtime staging boundary, including reused input folders."""
from pathlib import Path
import importlib.util
import sys
import tempfile
import unittest
import os
import re
import subprocess
import base64
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

class RuntimePackageTests(unittest.TestCase):
    def test_bilingual_documents_keep_offline_links_and_legacy_update_paths(self):
        from tools.runtime_package import BINARIES, collect_runtime_files, stage_runtime
        project=Path(__file__).resolve().parents[1]
        with tempfile.TemporaryDirectory(dir=project/'work') as folder:
            root=Path(folder);app=root/'app';app.mkdir()
            for name in BINARIES:
                (app/name).write_bytes(b'fixture')
            plan=collect_runtime_files(project,app)
            destinations={item['destination'] for item in plan}
            self.assertIn('docs/readme.en.md',destinations)
            self.assertIn('docs/changelog.en.md',destinations)
            self.assertIn('docs/im-channels.md',destinations)
            # The 0.10.4 updater only accepts the original root filenames and docs/*.md.
            self.assertNotIn('README.en.md',destinations)
            self.assertNotIn('CHANGELOG.en.md',destinations)
            stage=root/'runtime'
            stage_runtime(plan,stage)
            expected={
                'README.md':['docs/readme.en.md','docs/im-channels.md'],
                'docs/readme.en.md':['../README.md','changelog.en.md','configuration-guide.md','im-channels.md'],
                'CHANGELOG.md':['docs/changelog.en.md'],
                'docs/changelog.en.md':['../CHANGELOG.md','changelog.en.md','image-generation.md'],
            }
            for document,targets in expected.items():
                content=(stage/document).read_text(encoding='utf-8')
                for target in targets:
                    self.assertIn(']('+target+')',content,document)
                    self.assertTrue((stage/document).parent.joinpath(target).is_file(),(document,target))
            self.assertEqual({p.relative_to(stage).as_posix() for p in stage.rglob('*') if p.is_file()},destinations)

    @unittest.skipUnless(os.name=='nt','Framework artifact probe uses the Windows compiler')
    def test_onboarding_title_probe_uses_compiled_values(self):
        project=Path(__file__).resolve().parents[1]
        compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(dir=project/'work') as folder:
            root=Path(folder)
            self.assertTrue(root.resolve().is_relative_to((project/'work').resolve()))
            source=root/'Titles.cs'
            source.write_text('using System; namespace Dafeiyu { class Lesson { public string Title; } static class OnboardingLessons { internal static Lesson[] All { get { return new[] { new Lesson { Title="中文 {标题}" }, new Lesson { Title=String.Concat("开","始") } }; } } } }',encoding='utf-8-sig')
            assembly=root/'Fixture.dll';probe=root/'Probe.exe'
            for inputs in [ ['/target:library','/out:'+str(assembly),str(source)], ['/target:exe','/out:'+str(probe),str(project/'tools/localization/VerifyArtifacts.cs')] ]:
                subprocess.run([str(compiler),'/nologo','/codepage:65001']+inputs,check=True,capture_output=True)
            result=subprocess.run([str(probe),'--onboarding-titles',str(assembly)],capture_output=True)
            self.assertEqual(result.returncode,0,result.stderr.decode('utf8',errors='replace'))
            self.assertEqual(base64.b64decode(result.stdout).decode('utf8').split('\n'),['中文 {标题}','开始'])

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
            self.assertIn('docs/holiday-outfits.md',destinations)
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
