"""Verify release guards in owned Git repositories without any GitHub writes."""
from pathlib import Path
import json
import shutil
import subprocess
import sys
import tempfile
import unittest

PROJECT=Path(__file__).resolve().parents[1]

class WindowsReleaseTests(unittest.TestCase):
    def test_tag_branch_cleanliness_and_latest_guards(self):
        with tempfile.TemporaryDirectory(dir=PROJECT/'work') as temp:
            root=Path(temp)
            (root/'src').mkdir();(root/'tools').mkdir()
            for name in ['release_assets.py','verify_windows_release.py']:
                shutil.copy2(PROJECT/'tools'/name,root/'tools'/name)
            (root/'.gitignore').write_text('work/\n__pycache__/\n',encoding='utf-8')
            def git(*args):
                return subprocess.check_output(['git',*args],cwd=root,stderr=subprocess.STDOUT,text=True).strip()
            def commit(version,assembly='0.9.4.0'):
                (root/'src/ReleaseInfo.cs').write_text('DisplayVersion="'+version+'"; AssemblyVersion="'+assembly+'";',encoding='utf-8')
                git('add','.');git('commit','-qm','Owned release fixture')
                return git('rev-parse','HEAD')
            def verify(tag,allowed,latest=None):
                command=[sys.executable,'tools/verify_windows_release.py','--tag',tag,'--output','work/result.json']
                if latest:command+=['--latest',latest]
                result=subprocess.run(command,cwd=root,capture_output=True,text=True)
                self.assertEqual(result.returncode==0,allowed,result.stderr)
                return json.loads((root/'work/result.json').read_text()) if allowed else None
            git('init','-q');git('config','user.name','DeskPet test');git('config','user.email','fixture@example.invalid')
            stable=commit('0.9.4');git('tag','v0.9.4');git('update-ref','refs/remotes/origin/main',stable)
            self.assertEqual(verify('v0.9.4',True,'v0.9.3')['make_latest'],'true')
            verify('v0.9.4',False,'v0.9.4');verify('v0.9.4',False,'v0.9.5')
            verify('v0.9.3',False)
            (root/'dirty.txt').write_text('dirty');verify('v0.9.4',False);(root/'dirty.txt').unlink()
            preview=commit('0.9.5-dev.2','0.9.5.0');git('tag','v0.9.5-dev.2');git('update-ref','refs/remotes/origin/dev',preview)
            metadata=verify('v0.9.5-dev.2',True,'v0.9.4')
            self.assertTrue(metadata['prerelease']);self.assertEqual(metadata['make_latest'],'false')
            git('update-ref','refs/remotes/origin/dev',stable);verify('v0.9.5-dev.2',False)
            broken=commit('0.9.6','0.9.5.0');git('tag','v0.9.6');git('update-ref','refs/remotes/origin/main',broken)
            verify('v0.9.6',False)

if __name__=='__main__':unittest.main()
