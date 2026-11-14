"""Read-only shipping verification against pinned releases and immutable source."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid
import zipfile

REPO='Eason4869/deskpet'
COMMIT='5fdc3b0264ad1b011413ce96d10fa89c68bc3de4'
MAIN='e4b133f57afb569e074bf9c2e1b225029b35ca26'
TAG='v1.0.4-dev.1'
TAG_OBJECT='d96a88cde68e30884632c25b0b3b01f19a48b453'
RELEASE=445245209
OLD_RELEASE=439150182
HERE=Path(__file__).resolve().parent
p=argparse.ArgumentParser()
p.add_argument('--phase',choices=('draft','public'),required=True)
p.add_argument('--source',type=Path,required=True)
p.add_argument('--reports',type=Path,required=True)
a=p.parse_args()
a.source=a.source.resolve();a.reports=a.reports.resolve();a.reports.mkdir(parents=True,exist_ok=True)
owned=Path(os.environ['RUNNER_TEMP']).resolve()/('dsp-'+uuid.uuid4().hex[:8])
owned.mkdir();assets=owned/'assets';assets.mkdir()

def api(path):
    result=subprocess.run(['gh','api','repos/'+REPO+'/'+path],check=True,capture_output=True,timeout=90)
    return json.loads(result.stdout)

def write_report(name,data):
    (a.reports/name).write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def hash_file(path):
    with path.open('rb') as stream:return hashlib.file_digest(stream,'sha256').hexdigest()

def download(asset,directory):
    name=asset['name'];assert name==Path(name).name and '/' not in name and '\\' not in name
    path=directory/name
    with path.open('xb') as output:
        subprocess.run(['gh','api','repos/'+REPO+'/releases/assets/'+str(asset['id']),'-H','Accept: application/octet-stream'],stdout=output,check=True,timeout=360)
    assert path.stat().st_size==asset['size'],(name,'download size mismatch')
    if asset.get('digest'):assert asset['digest']=='sha256:'+hash_file(path),(name,'download digest mismatch')
    return path

def boundaries(stage):
    main=api('git/ref/heads/main');dev=api('git/ref/heads/dev');tag=api('git/ref/tags/'+TAG)
    assert main['object']['sha']==MAIN and dev['object']['sha']==COMMIT
    assert tag['object']['type']=='tag' and tag['object']['sha']==TAG_OBJECT
    tag_object=api('git/tags/'+TAG_OBJECT)
    assert tag_object['object']['type']=='commit' and tag_object['object']['sha']==COMMIT
    latest=api('releases/latest')
    assert latest['id']==OLD_RELEASE and latest['tag_name']=='v1.0.3' and not latest['draft'] and not latest['prerelease']
    write_report('boundaries-'+stage+'.json',dict(main=MAIN,dev=COMMIT,tag=TAG,tag_object=TAG_OBJECT,peeled_commit=COMMIT,latest_id=latest['id'],latest_tag=latest['tag_name']))

head=subprocess.check_output(['git','-C',str(a.source),'rev-parse','HEAD'],text=True).strip()
assert head==COMMIT,(head,'immutable source checkout differs')
boundaries('before')
release=api('releases/'+str(RELEASE))
assert release['id']==RELEASE and release['tag_name']==TAG and release['target_commitish']==COMMIT and release['prerelease']
assert release['draft']==(a.phase=='draft')
assert len(release['assets'])==17 and len({item['name'] for item in release['assets']})==17
write_report('release.json',release)
if a.phase=='public':
    subprocess.run([sys.executable,str(HERE/'verify-public-release.py'),'--release',str(a.reports/'release.json'),'--commit',COMMIT,'--output',str(a.reports/'public-verification.json')],check=True,timeout=600)
else:
    names={item['name']:item for item in release['assets']}
    for name in ('DeskPet-Windows-1.0.4-dev.1.zip','SHA256.txt','mac-upload-manifest.json'):download(names[name],assets)
    package=assets/'DeskPet-Windows-1.0.4-dev.1.zip'
    assert package.stat().st_size<=300*1024*1024
    subprocess.run([sys.executable,str(HERE/'verify-draft-release.py'),'--release',str(a.reports/'release.json'),'--assets',str(assets),'--commit',COMMIT,'--output',str(a.reports/'draft-verification.json')],check=True,timeout=120)
    # Inspect metadata only; never launch or Assembly.Load the shipping app.
    current_app=owned/'shipping-app.exe'
    with zipfile.ZipFile(package) as archive:
        entries=[entry for entry in archive.infolist() if entry.filename=='DafeiyuDeskPet/桌面伙伴.exe']
        assert len(entries)==1 and not entries[0].is_dir() and 0<entries[0].file_size<=400*1024*1024
        with archive.open(entries[0]) as source,current_app.open('xb') as output:
            while True:
                chunk=source.read(1024*1024)
                if not chunk:break
                output.write(chunk)
        assert current_app.stat().st_size==entries[0].file_size
    identity_script='[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$p=$env:DESKPET_VERIFY_APP;[pscustomobject]@{product_version=[Diagnostics.FileVersionInfo]::GetVersionInfo($p).ProductVersion;assembly_version=[Reflection.AssemblyName]::GetAssemblyName($p).Version.ToString()}|ConvertTo-Json -Compress'
    probe=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-Command',identity_script],env=dict(os.environ,DESKPET_VERIFY_APP=str(current_app)),capture_output=True,check=True,timeout=30,creationflags=subprocess.CREATE_NO_WINDOW)
    identity=json.loads(probe.stdout.decode('utf-8-sig'))
    assert identity['product_version']=='1.0.4-dev.1+'+COMMIT and identity['assembly_version']=='1.0.4.0',identity
    identity.update(sha256=hash_file(current_app),bytes=current_app.stat().st_size)
    write_report('shipping-app-identity.json',identity)
    origin=json.loads((HERE/'verified-origin.json').read_text(encoding='utf-8'))
    assert origin['release_id']==OLD_RELEASE and origin['tag']=='v1.0.3'
    old=api('releases/'+str(OLD_RELEASE));assert old['tag_name']=='v1.0.3' and not old['draft'] and not old['prerelease']
    candidates=[item for item in old['assets'] if item['name']=='DeskPet-Windows-1.0.3.zip'];assert len(candidates)==1
    oldzip=download(candidates[0],assets)
    assert hash_file(oldzip)==origin['zip_sha256']=='bfcc5d02ce431e753f5ff42d7f03a5292f4562a48d438f0c39517c4081384858'
    binaries=owned/'old';binaries.mkdir()
    assert {item['name'] for item in origin['files']}=={'桌面伙伴.exe','桌面伙伴.exe.config','DeskPet.Update.exe','DeskPet.Update.exe.config'}
    with zipfile.ZipFile(oldzip) as archive:
        for item in origin['files']:
            name='DafeiyuDeskPet/'+item['name'];entries=[entry for entry in archive.infolist() if entry.filename==name]
            assert len(entries)==1 and entries[0].file_size==item['bytes'] and not entries[0].is_dir()
            path=binaries/item['name']
            with archive.open(entries[0]) as source,path.open('xb') as output:
                while True:
                    chunk=source.read(1024*1024)
                    if not chunk:break
                    output.write(chunk)
            assert path.stat().st_size==item['bytes'] and hash_file(path)==item['sha256']
    write_report('verified-old-origin.json',origin)
    command=[sys.executable,'-X','utf8',str(a.source/'tests/LegacyUpdateSmoke.py'),'--worker',str(binaries/'DeskPet.Update.exe'),'--published-app',str(binaries/'桌面伙伴.exe'),'--package',str(package),'--work-directory',str(owned/'legacyfinal')]
    with (a.reports/'legacy-upgrade.log').open('w',encoding='utf-8') as log:
        result=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,timeout=240)
    write_report('legacy-upgrade-result.json',dict(exit_code=result.returncode,release_id=RELEASE,commit=COMMIT,shipping_zip_sha256=hash_file(package),shipping_zip_bytes=package.stat().st_size,old_zip_sha256=origin['zip_sha256']))
    assert result.returncode==0,'Published 1.0.3 client/helper handoff failed; see legacy-upgrade.log'
boundaries('after')
print('PASS '+a.phase+' shipping verification against immutable '+COMMIT)
