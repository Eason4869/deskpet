"""Run the production Inno version guard against small owned PE fixtures."""
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import uuid

PROJECT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--compiler',type=Path,required=True)
args=parser.parse_args()
root=PROJECT/'work'/('installer-version-'+uuid.uuid4().hex)
root.mkdir()
csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
startup=subprocess.STARTUPINFO()
startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow=0

def run(command):
    return subprocess.run([str(x) for x in command],cwd=root,capture_output=True,timeout=90,startupinfo=startup)

def compile_pe(path,version):
    source=path.with_suffix('.cs')
    source.write_text('[assembly:System.Reflection.AssemblyVersion("0.9.4.0")]'
                      '[assembly:System.Reflection.AssemblyFileVersion("0.9.4.0")]'
                      '[assembly:System.Reflection.AssemblyInformationalVersion("'+version+'+'+'a'*40+'")]'
                      'class Fixture {static void Main(){}}',encoding='utf-8-sig')
    result=run([csc,'/nologo','/target:winexe','/out:'+str(path),source])
    assert result.returncode==0,result.stdout

installers={}
for target in ['0.9.4-dev.2','0.9.4']:
    payload=root/('payload-'+target+'.exe')
    compile_pe(payload,target)
    script=root/('setup-'+target+'.iss')
    script.write_text(
        '#define ProductVersion "'+target+'"\n[Setup]\n'
        '#define InformationalVersion "'+target+'+'+'a'*40+'"\n'
        'AppName=DeskPet Version Guard Fixture\nAppVersion='+target+'\n'
        'AppId=DeskPet-Version-Test-'+uuid.uuid4().hex+'\n'
        'DefaultDirName={localappdata}\\DeskPetVersionFixture\n'
        'PrivilegesRequired=lowest\nUninstallable=no\nDisableProgramGroupPage=yes\n'
        'OutputDir='+str(root)+'\nOutputBaseFilename=installer-'+target+'\n'
        '[Files]\nSource: "'+str(payload)+'"; DestDir: "{app}"; DestName: "桌面伙伴.exe"; Flags: ignoreversion\n'
        '[Code]\n#include "'+str(PROJECT/'installer/VersionGuard.iss')+'"\n',
        encoding='utf-8-sig')
    result=run([args.compiler,'/Qp',script])
    assert result.returncode==0,result.stdout.decode('utf-8',errors='replace')
    installers[target]=root/('installer-'+target+'.exe')

cases=[
    ('0.9.4','0.9.4-dev.2',False),
    ('0.9.4-dev.10','0.9.4-dev.2',False),
    ('0.9.4-dev.1','0.9.4-dev.2',True),
    ('0.9.4-dev.10','0.9.4',True),
    ('0.9.4-dev.2','0.9.4-dev.2',True),
    ('broken','0.9.4',False),
]
for index,(installed,target,allowed) in enumerate(cases):
    directory=root/('case-'+str(index))
    directory.mkdir()
    executable=directory/'桌面伙伴.exe'
    compile_pe(executable,installed)
    before=hashlib.sha256(executable.read_bytes()).digest()
    result=run([installers[target],'/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/DIR='+str(directory),'/LOG='+str(root/('case-'+str(index)+'.log'))])
    assert (result.returncode==0)==allowed,(installed,target,result.returncode)
    if not allowed:
        assert hashlib.sha256(executable.read_bytes()).digest()==before,'Rejected installer modified program'
    print('PASS installer guard: '+installed+' -> '+target+' = '+str(allowed),flush=True)
print('PASS production Inno version guard: '+str(len(cases))+' cases')
directory=root/'expected-build';directory.mkdir()
executable=directory/'桌面伙伴.exe'
compile_pe(executable,'0.9.4-dev.1')
before=hashlib.sha256(executable.read_bytes()).digest()
result=run([installers['0.9.4'], '/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',
            '/DIR='+str(directory),'/EXPECTEDBUILD=0.9.4+'+'b'*40])
assert result.returncode!=0 and hashlib.sha256(executable.read_bytes()).digest()==before
print('PASS mismatched installer commit is rejected before overwriting the old program')
