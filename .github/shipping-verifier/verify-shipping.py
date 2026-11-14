"""Read-only shipping verification against pinned releases and immutable source."""
import argparse
import hashlib
import json
import plistlib
import os
from pathlib import Path
import subprocess
import sys
import uuid
import zipfile

REPO='Eason4869/deskpet'
COMMIT='112aeb97dc6bf846b0bb0a2878c0973b406f319a'
MAIN='e4b133f57afb569e074bf9c2e1b225029b35ca26'
TAG='v1.0.4-dev.2'
TAG_OBJECT='0e4f75bd9ae47532f20d6508f6e1e90b68168031'
RELEASE=445402349
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
    endpoint='repos/'+REPO+'/'+path
    result=subprocess.run(['gh','api',endpoint],capture_output=True,timeout=90)
    if result.returncode:
        # Standard gh errors only: never enable HTTP debug or print headers/env.
        error=result.stderr.decode('utf-8',errors='replace').strip()
        for key in ('GH_TOKEN','GITHUB_TOKEN'):
            secret=os.environ.get(key)
            if secret:error=error.replace(secret,'[REDACTED]')
        error=error[:1500]
        print('GitHub GET '+endpoint+' failed (exit '+str(result.returncode)+'): '+error,file=sys.stderr,flush=True)
        result.check_returncode()
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
    for name in ('DeskPet-Windows-1.0.4-dev.2.zip','SHA256.txt','mac-upload-manifest.json'):download(names[name],assets)
    package=assets/'DeskPet-Windows-1.0.4-dev.2.zip'
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
    assert identity['product_version']=='1.0.4-dev.2+'+COMMIT and identity['assembly_version']=='1.0.4.0',identity
    identity.update(sha256=hash_file(current_app),bytes=current_app.stat().st_size)
    write_report('shipping-app-identity.json',identity)
    full_downloads=[]
    assert names['DeskPet-Windows-App-1.0.4-dev.2.exe']['digest']=='sha256:'+identity['sha256']
    installer=download(names['DeskPet-Windows-Setup-1.0.4-dev.2.exe'],assets)
    with installer.open('rb') as stream:assert stream.read(2)==b'MZ'
    installer_probe='[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$v=[Diagnostics.FileVersionInfo]::GetVersionInfo($env:DESKPET_VERIFY_INSTALLER);[pscustomobject]@{product_version=$v.ProductVersion;file_version=$v.FileVersion}|ConvertTo-Json -Compress'
    probe=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-Command',installer_probe],env=dict(os.environ,DESKPET_VERIFY_INSTALLER=str(installer)),capture_output=True,check=True,timeout=30,creationflags=subprocess.CREATE_NO_WINDOW)
    installer_identity=json.loads(probe.stdout.decode('utf-8-sig'))
    assert installer_identity==dict(product_version='1.0.4-dev.2',file_version='1.0.4.0'),installer_identity
    import winreg
    app_key=r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B63C162-076E-4B2F-BDCC-2572664875A9}_is1'
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,app_key):pass
    except FileNotFoundError:pass
    else:raise AssertionError('Refusing installer verification over an existing installation')
    install=owned/'shipping-install'
    assert install.resolve().parent==owned.resolve() and not install.exists()
    uninstaller=install/'unins000.exe'
    command=[str(installer),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',
        '/DIR='+str(install),'/GROUP=DeskPetShippingVerification-'+owned.name,
        '/EXPECTEDBUILD=1.0.4-dev.2+'+COMMIT,'/LOG='+str(a.reports/'shipping-install.log')]
    try:
        result=subprocess.run(command,timeout=120,creationflags=subprocess.CREATE_NO_WINDOW)
        assert result.returncode==0,('Shipping installer build guard or installation failed',result.returncode)
        installed=[]
        with zipfile.ZipFile(package) as archive:
            for item in archive.infolist():
                if item.is_dir():continue
                prefix='DafeiyuDeskPet/'
                assert item.filename.startswith(prefix)
                relative=Path(item.filename[len(prefix):])
                assert not relative.is_absolute() and '..' not in relative.parts
                target=(install/relative).resolve()
                assert target.is_relative_to(install.resolve()) and target.is_file()
                hasher=hashlib.sha256()
                with archive.open(item) as source:
                    for chunk in iter(lambda:source.read(1024*1024),b''):hasher.update(chunk)
                assert target.stat().st_size==item.file_size and hash_file(target)==hasher.hexdigest(),item.filename
                installed.append(dict(name=str(relative),sha256=hasher.hexdigest(),bytes=item.file_size))
        assert hash_file(install/'桌面伙伴.exe')==identity['sha256']
        write_report('shipping-installer-identity.json',dict(commit=COMMIT,installer=installer_identity,expected_build_guard='passed',installed_files=installed))
    finally:
        if uninstaller.exists():
            assert uninstaller.resolve().parent==install.resolve()
            cleanup=subprocess.run([str(uninstaller),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG='+str(a.reports/'shipping-uninstall.log')],timeout=120,creationflags=subprocess.CREATE_NO_WINDOW)
            assert cleanup.returncode==0,('Shipping verification uninstall failed',cleanup.returncode)
    full_downloads.append(dict(name=installer.name,sha256=hash_file(installer),bytes=installer.stat().st_size))
    with zipfile.ZipFile(package) as archive:assert archive.testzip() is None
    for arch in ('arm64','x86_64'):
        name='DeskPet-macOS-1.0.4-dev.2-'+arch+'-preview.zip'
        path=download(names[name],assets)
        with zipfile.ZipFile(path) as archive:
            assert archive.testzip() is None,(name,'CRC failure')
            entries=archive.namelist()
            builds=[entry for entry in entries if entry.endswith('/Contents/Resources/build-info.json')]
            plists=[entry for entry in entries if entry.endswith('/Contents/Info.plist')]
            assert len(builds)==len(plists)==1
            build=json.loads(archive.read(builds[0]))
            assert build==dict(version='1.0.4-dev.2',architecture=arch,mode='preview',commit=COMMIT),build
            info=plistlib.loads(archive.read(plists[0]))
            assert info['CFBundleVersion']==info['CFBundleShortVersionString']=='1.0.4'
            full_downloads.append(dict(name=name,sha256=hash_file(path),bytes=path.stat().st_size,build=build))
    source_archive=download(names['DeskPet-Source-1.0.4-dev.2.zip'],assets)
    with zipfile.ZipFile(source_archive) as archive:
        original=(a.source/'tests/AutoUpdateSmoke.cs').read_text(encoding='utf-8-sig')
        published=archive.read('DafeiyuDeskPet/tests/AutoUpdateSmoke.cs').decode('utf-8-sig').replace('\r\n','\n')
        assert published==original,'Source archive must retain the original immutable tagged test'
        assert not any('/work/' in name or '/.superpowers/' in name or '/.github/release-fixtures/' in name for name in archive.namelist())
        fixture_sha=hashlib.sha256(published.encode()).hexdigest()
    full_downloads.append(dict(name=source_archive.name,sha256=hash_file(source_archive),bytes=source_archive.stat().st_size,original_tagged_fixture_sha256=fixture_sha))
    write_report('full-shipping-downloads.json',dict(commit=COMMIT,archives=full_downloads,windows_zip_crc='passed'))
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
