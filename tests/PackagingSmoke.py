"""Build release archives from a fresh source tree with no local work directory."""
from pathlib import Path
import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
from zipfile import ZipFile

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(PROJECT))
from tools.release_assets import installer_asset_name
from tools.localization.helper_resources import expected_locale_resources, validate_helper_resources
version = re.search(r'DisplayVersion="([^"]+)"', (PROJECT / 'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')).group(1)
arguments = argparse.ArgumentParser()
arguments.add_argument('--compiler', help='Optional Inno ISCC.exe for full fresh installer verification')
arguments.add_argument('--legacy-worker', type=Path, help='Existing older DeskPet.Update.exe to verify backward compatible packages')
options = arguments.parse_args()
# Keep this fresh-checkout fixture short enough for Framework's real MAX_PATH
# limit. Explicit overlong final-root rollback remains in OfficialCompanionTests.
root = PROJECT / 'work' / 'ps' / uuid.uuid4().hex[:12]
checkout = root / 'src'
shutil.copytree(PROJECT, checkout, ignore=shutil.ignore_patterns(
    '.git', 'work', 'bin', 'obj', 'data', '__pycache__', '.superpowers', 'superpowers', 'research',
    'motion', 'motion-source', 'drawn-source*', 'PackDrawnMotion.py', 'PackXiaobei.py', 'MotionSourceExport.cs', 'SmoothMotionTests.cs'))
assert not (checkout/'assets/motion').exists(), 'Clean builds must not depend on retired motion artwork'
assert (checkout/'templates/high-fps-v2/pet.json').is_file(), 'Author template must be in the repository'
output = checkout / 'public-artifacts'
runtime = output / 'DafeiyuDeskPet'
result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                         '-File', str(checkout / 'Build.ps1'), '-Target', 'App', '-OutputDirectory', str(runtime)],
                        cwd=checkout, capture_output=True, timeout=180)
assert result.returncode == 0, result.stdout.decode('utf-8', errors='replace') + result.stderr.decode('utf-8', errors='replace')
print('PASS application compiles from the fresh public source tree')
for executable in ['桌面伙伴.exe', 'DeskPet.Update.exe']:
    assert (runtime / executable).is_file(), (
        'Fresh build is missing ' + ascii(executable) + '; actual executables: ' +
        ascii(sorted(path.name for path in runtime.glob('*.exe'))) +
        '. Keep non-ASCII PowerShell scripts UTF-8 with BOM for Windows PowerShell 5.1.')
# A reused portable folder must not leak previous executables or private loose files.
(runtime / '大肥鱼桌宠.exe').write_bytes(b'legacy binary, do not ship')
(runtime / 'private-key.txt').write_text('private sentinel', encoding='utf-8')
(runtime / 'templates').mkdir()
(runtime / 'templates/private.md').write_text('private sentinel',encoding='utf-8')
(runtime / 'previews').mkdir()
(runtime / 'previews/obsolete.png').write_bytes(b'private sentinel')
(runtime / 'data').mkdir()
(runtime / 'data/settings.json').write_text('private settings sentinel', encoding='utf-8')
for private_name in ['.env', '.env.local', 'credentials.json', 'release-signing.pfx']:
    (checkout / private_name).write_text('private source sentinel', encoding='utf-8')
(checkout / '.codex').mkdir(exist_ok=True)
(checkout / '.codex/auth.json').write_text('private auth sentinel', encoding='utf-8')
result = subprocess.run([sys.executable, str(checkout / 'Package.py'), '--output-directory', str(output)], cwd=checkout,
                        env=dict(os.environ,PYTHONIOENCODING='utf-8'),
                        capture_output=True, text=True, encoding='utf-8', timeout=2400)
if result.returncode:
    print(result.stdout + result.stderr)
    raise AssertionError('fresh source packaging needs no local test artifacts')
print('PASS fresh source packaging needs no local test artifacts')
app_asset = output / ('DeskPet-Windows-App-' + version + '.exe')
assert app_asset.is_file(), 'versioned standalone application release asset missing: ' + app_asset.name
assert app_asset.read_bytes() == (runtime / '桌面伙伴.exe').read_bytes(), 'standalone release asset must preserve the built application bytes'
print('PASS versioned standalone application matches the runtime executable')
with ZipFile(output / ('DeskPet-Windows-' + version + '.zip')) as bundle:
    assert not any(name.endswith('大肥鱼桌宠.exe') or 'private-key' in name or '/data/' in name for name in bundle.namelist())
    for name in ['桌面伙伴.exe', '桌面伙伴.exe.config', 'DeskPet.Update.exe', 'DeskPet.Update.exe.config']:
        assert bundle.read('DafeiyuDeskPet/' + name) == (runtime / name).read_bytes()
print('PASS portable archive excludes legacy binaries and private loose files')
print('PASS portable archive includes the exact application and independent update helper')
for worker in [runtime / 'DeskPet.Update.exe'] + ([options.legacy_worker.resolve()] if options.legacy_worker else []):
    subprocess.run([sys.executable, str(checkout / 'tests/LegacyUpdateSmoke.py'), '--worker', str(worker),
                    '--package', str(output / ('DeskPet-Windows-' + version + '.zip')),
                    '--work-directory', str(PROJECT/'work')], check=True, timeout=120)
print('PASS real update engine accepts the complete release archive and preserves personal data')
with ZipFile(output / ('DeskPet-Windows-' + version + '.zip')) as bundle:
    for document in ['skin-format.md', 'character-packs.md', 'dialogue-setup.md', 'conversation-agent.md', 'native-agent-design.md', 'third-party-notices.md', 'sources.md', 'installation.md', 'daily-use.md', 'voice-presets.md', 'release-naming.md', 'interaction-extension.md']:
        if document in {'native-agent-design.md','roadmap.md','release-naming.md'}: continue
        packaged=bundle.read('DafeiyuDeskPet/docs/' + document).decode('utf-8-sig').replace('\r\n','\n')
        original=(checkout/'docs'/document).read_text(encoding='utf-8-sig')
        assert re.sub(r'(!?\[[^\]]*\])\([^)]+\)',r'\1',packaged)==re.sub(r'(!?\[[^\]]*\])\([^)]+\)',r'\1',original), 'packaged guide text changed: '+document
print('PASS runtime includes the exact daily-use and voice-preset guides')
index_path=output/('DeskPet-Companions-'+version+'.json')
companion_index=json.loads(index_path.read_text(encoding='utf-8'))
package_policy=json.loads((checkout/'assets/official/companion-package-policy.json').read_text(encoding='utf-8'))
material_registry=json.loads((checkout/'assets/official/companion-versions.json').read_text(encoding='utf-8'))['companions']
assert companion_index['schema_version']==1
assert [entry['id'] for entry in companion_index['companions']]==['e-bao','dafeiyu','marfu','xiaobei','gugugaga']
for entry in companion_index['companions']:
    pet=entry['id']
    material=material_registry[pet]
    archive=output/('DeskPet-Companion-'+pet+'-'+material['pack_version']+'.zip')
    assert archive.is_file() and entry['asset_name']==archive.name
    assert entry['version']==material['pack_version'] and entry['min_app_version']==material['min_app_version']
    assert entry['size']==archive.stat().st_size and entry['sha256']==hashlib.sha256(archive.read_bytes()).hexdigest()
    assert archive.stat().st_size<=package_policy['maximum_archive_bytes']
    with ZipFile(archive) as bundle:
        assert bundle.testzip() is None and len(bundle.infolist())<=package_policy['maximum_entries']
        assert sum(item.file_size for item in bundle.infolist())<=package_policy['maximum_expanded_bytes']
        skin=json.loads(bundle.read('pet.json'))
        identity=json.loads(bundle.read('companions.json'))
        assert identity['id']==pet and identity['version']==material['pack_version'] and identity['min_app_version']==material['min_app_version'] and identity['name']==skin['name']
        assert skin['id']==pet and skin['pack_version']==material['pack_version']
        assert skin['version']==2 and len(skin['animations'])==17 and skin['gaze']['frames']==16
        assert skin['system_prompt'] and skin['special_action'] and skin['special_action2']
        assert skin['avatar'] in bundle.namelist() and skin['voice_presets']['speech'] and skin['voice_presets']['live']
        # The reviewed quiet source replaces idle and sleeping for every official role.
        expected_counts=[8,8,8,4,5,8,6,6,6]+[4]*6+[8,4]
        action_names=['idle','running-right','running-left','waving','jumping','failed','waiting','running','review','lifted','landing','petting','poked','feeding','happy','sleeping','special']
        actual_counts=[skin['animations'][name]['frames'] for name in action_names]
        assert actual_counts==([8]*17 if pet in ('xiaobei','gugugaga') else expected_counts), (pet,dict(zip(action_names,actual_counts)))
        assert [(skin[group][name]['frames'],skin[group][name]['fps']) for group,name in
                (('animations','idle'),('animations','sleeping'),('alternate_animations','sleeping'))]==[(8,4),(8,2.5),(8,2.5)], pet+' quiet timing'
        assert set(skin['extra_animations'])=={'stumble','special2'}
        assert len(skin['alternate_animations'])==(9 if pet in ('e-bao','gugugaga') else 8)
        for group in ('animations','extra_animations','alternate_animations'):
            for record in skin[group].values():
                assert record['columns']==8 and 1<=record['frames']<=8 and record['file'] in bundle.namelist()
        assert skin['gaze']['file'] in bundle.namelist() and skin['preview'] in bundle.namelist()
        catalog=json.loads(bundle.read('holidays/catalog.json'))
        authored=[item for item in json.loads((checkout/'assets/holidays/catalog.json').read_text(encoding='utf-8'))['outfits'] if item['character_id']==pet]
        assert catalog['outfits']==authored and all(item['ready'] for item in authored)
        assert entry['outfits']==[{'outfit_id':item['outfit_id'],'version':item['version']} for item in authored]
        for outfit in authored:
            definition=json.loads(bundle.read('holidays/'+outfit['definition']))
            for relative in [definition['preview']]+[clip['path'] for clip in definition['clips']+[definition['gaze'],definition['easter_egg']]]:
                assert bundle.read('holidays/'+relative)==(checkout/'assets/holidays'/relative).read_bytes(), 'HD outfit bytes changed'
with ZipFile(output/('DeskPet-Skin-Template-v2-'+version+'.zip')) as bundle:
    assert 'high-fps-v2/pet.json' in bundle.namelist() and 'high-fps-v2/动作表.csv' in bundle.namelist()
    assert 'legacy-v1/pet.json' in bundle.namelist() and 'legacy-v1/spritesheet.png' in bundle.namelist()
with ZipFile(output/('DeskPet-Windows-'+version+'.zip')) as bundle:
    assert 'DafeiyuDeskPet/docs/configuration-guide.md' in bundle.namelist()
    guide = bundle.read('DafeiyuDeskPet/docs/configuration-guide.md').decode('utf-8-sig')
    # Read the actual compiled titles, including embedded resource resolution.
    # Source-literal regexes cannot follow TextResources.Get and miss dynamic values.
    probe=root/'GuideTitles.exe'
    compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(compiler),'/nologo','/codepage:65001','/target:exe','/out:'+str(probe),str(checkout/'tools/localization/VerifyArtifacts.cs')],capture_output=True,check=True,timeout=30)
    result=subprocess.run([str(probe),'--onboarding-titles',str(runtime/'桌面伙伴.exe')],capture_output=True,timeout=30)
    if result.returncode != 0:
        raise RuntimeError('Compiled onboarding-title probe failed: ' + (result.stdout + result.stderr).decode('utf-8', errors='replace'))
    lesson_titles=base64.b64decode(result.stdout).decode('utf8').split('\n')
    assert lesson_titles and all(lesson_titles), 'application lesson titles must be extracted from the actual compiled application'
    assert str(len(lesson_titles)) + ' 个小步骤' in guide, 'packaged guide count must match current application lessons'
    guide_titles = re.findall(r'^### \d+\. (.+)$', guide.replace('\r\n', '\n'), re.MULTILINE)
    assert guide_titles == lesson_titles, 'packaged guide must retain every current lesson title in order'
    assert not any('/templates/' in name or '/previews/' in name for name in bundle.namelist())
    assert not any('/pets/' in name or name.endswith('/verification.md') for name in bundle.namelist())
print('PASS five complete full-HD default/holiday companion packages, verified index and author templates')
with ZipFile(output/('DeskPet-Windows-'+version+'.zip')) as bundle:
    from tools.runtime_package import collect_runtime_files
    assert set(bundle.namelist()) == {'DafeiyuDeskPet/'+record['destination'] for record in collect_runtime_files(checkout,runtime)}
print('PASS portable and installer share the same explicit runtime contract')

manifest = (output / 'SHA256.txt').read_text(encoding='utf-8-sig').splitlines()
manifest_names = [line.split('  ', 1)[1] for line in manifest]
assert manifest_names.count(app_asset.name) == 1, 'standalone application must appear exactly once in the release manifest'
assert all('/' not in name and '\\' not in name for name in manifest_names), 'release manifest must contain flat uploadable asset names'
material_assets={entry['asset_name'] for entry in companion_index['companions']}
assert all(version in name for name in manifest_names if name not in material_assets), 'application assets must retain the complete release version'
assert set(manifest_names) == {path.name for path in output.iterdir() if path.is_file() and (path.suffix in {'.exe', '.zip'} or path.name=='DeskPet-Companions-'+version+'.json')}, 'manifest must cover every release asset'
for line in manifest:
    digest, name = line.split('  ', 1)
    assert digest == hashlib.sha256((output / name).read_bytes()).hexdigest(), 'stale digest: ' + name
for pet in ['e-bao', 'dafeiyu', 'marfu', 'xiaobei', 'gugugaga']:
    assert any(line.endswith('  DeskPet-Companion-' + pet + '-' + material_registry[pet]['pack_version'] + '.zip') for line in manifest)
assert any(line.endswith('  DeskPet-Companions-' + version + '.json') for line in manifest)
print('PASS versioned ASCII skin names and manifest hashes match release artifacts')
result = subprocess.run(['powershell.exe', '-NoProfile', '-Command',
    '[Reflection.AssemblyName]::GetAssemblyName($env:DESKPET_TEST_HELPER).Version.ToString()'],
    env=dict(os.environ, DESKPET_TEST_HELPER=str(runtime / 'DeskPet.Update.exe')),
    capture_output=True, check=True)
assert result.stdout.decode('ascii').strip() == version.split('-')[0] + '.0'
helper_manifest_script = '''
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::LoadFile($env:DESKPET_TEST_HELPER)
$manifest=@(foreach($name in $a.GetManifestResourceNames()) {
    $stream=$a.GetManifestResourceStream($name);$sha=[Security.Cryptography.SHA256]::Create()
    try { [pscustomobject]@{name=$name;length=$stream.Length;sha256=([BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant())} }
    finally { $sha.Dispose();$stream.Dispose() }
})
ConvertTo-Json -InputObject $manifest -Compress
'''
result = subprocess.run(['powershell.exe', '-NoProfile', '-Command', helper_manifest_script],
    env=dict(os.environ, DESKPET_TEST_HELPER=str(runtime / 'DeskPet.Update.exe')),
    capture_output=True, check=True)
validate_helper_resources((runtime / 'DeskPet.Update.exe').stat().st_size,
    json.loads(result.stdout.decode('utf-8-sig')), expected_locale_resources(checkout / 'assets/locales'))
print('PASS update helper has the declared version, exact locale hashes, no sprite resources and less than 1 MiB non-locale overhead')
result=subprocess.run(['powershell.exe','-NoProfile','-Command',
    '$a=[Reflection.Assembly]::LoadFile($env:DESKPET_TEST_APP); $a.GetManifestResourceNames()'],
    env=dict(os.environ,DESKPET_TEST_APP=str(runtime/'桌面伙伴.exe')),capture_output=True,check=True)
resources=result.stdout.decode('utf-8',errors='replace')
assert 'Dafeiyu.Motion.' not in resources and 'Dafeiyu.MotionManifest' not in resources
assert 'Dafeiyu.EbaoAtlas' in resources and 'Dafeiyu.Holidays.halloween.e-bao.outfit.json' in resources
assert 'Dafeiyu.Atlas' not in resources and 'Dafeiyu.MarfuAtlas' not in resources
assert 'Dafeiyu.Bundled.Xiaobei.' not in resources and 'Dafeiyu.Bundled.Gugugaga.' not in resources
for role in ('dafeiyu','marfu','xiaobei','gugugaga'):
    assert 'Dafeiyu.Avatar.'+role in resources
    assert 'Dafeiyu.Companion.'+role+'.' not in resources and 'Dafeiyu.Additional.'+role not in resources
    assert 'Dafeiyu.Holidays.halloween.'+role+'.' not in resources
print('PASS final application embeds one complete Ebao, five avatars and no other full-role artwork')
staged_runtime=Path(json.loads((output/'runtime-stage.json').read_text(encoding='utf-8'))['path'])
readme = (staged_runtime / 'README.md').read_text(encoding='utf-8-sig')
images = re.findall(r'<img\s+src="([^"]+)"|!\[[^\]]*\]\(([^)]+)\)', readme)
for alternatives in images:
    target = next(value for value in alternatives if value)
    assert (staged_runtime / target).is_file(), 'installed README image missing: ' + target
    source_target = target.replace('previews/avatars/', 'assets/avatars/')
    assert (checkout / source_target).is_file(), 'repository README image missing: ' + source_target
    if target.startswith('previews/avatars/'):
        assert (staged_runtime / target).read_bytes() == (checkout / source_target).read_bytes(), 'packaged avatar changed: ' + target
    with ZipFile(output / ('DeskPet-Windows-' + version + '.zip')) as bundle:
        assert 'DafeiyuDeskPet/' + target in bundle.namelist(), 'portable README image missing: ' + target
print('PASS README images resolve in the repository and installed runtime')
with ZipFile(output / ('DeskPet-Source-' + version + '.zip')) as bundle:
    assert not any(Path(name).name in {'.env', '.env.local', 'credentials.json', 'release-signing.pfx', 'auth.json'} for name in bundle.namelist()), 'private source files must not ship'
    assert not any(part in {'work', 'data', '.git', '.superpowers', 'superpowers', 'public-artifacts', 'outputs'}
                   for name in bundle.namelist() for part in Path(name).parts)
with ZipFile(output/('DeskPet-Source-'+version+'.zip')) as bundle:
    assert not any('/assets/motion/' in name or '/assets/drawn-source' in name or '/assets/motion-source/' in name or name.endswith(('/PackDrawnMotion.py','/PackXiaobei.py','/MotionSourceExport.cs','/SmoothMotionTests.cs')) for name in bundle.namelist())
    assert 'DafeiyuDeskPet/tests/SkinPackExport.cs' in bundle.namelist()
    assert 'DafeiyuDeskPet/templates/high-fps-v2/pet.json' in bundle.namelist()
print('PASS source archive excludes retired generations, private and local test files')
with ZipFile(output / ('DeskPet-Source-' + version + '.zip')) as bundle:
    for name in bundle.namelist():
        relative = Path(name).relative_to('DafeiyuDeskPet')
        assert bundle.read(name) == (checkout / relative).read_bytes(), 'source archive differs: ' + name
print('PASS source archive contains the exact public source files')
if options.compiler:
    result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                             '-File', str(checkout / 'BuildInstaller.ps1'),
                             '-Compiler', str(Path(options.compiler).resolve()), '-OutputDirectory', str(output)],
                            cwd=checkout, capture_output=True, timeout=180)
    assert result.returncode == 0, result.stdout.decode('utf-8', errors='replace') + result.stderr.decode('utf-8', errors='replace')
    print('PASS installer builds from the fresh public source tree')
    manifest = (output / 'SHA256.txt').read_text(encoding='utf-8-sig').splitlines()
    version = re.search(r'DisplayVersion="([^"]+)"', (checkout / 'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')).group(1)
    installer_name = installer_asset_name(version)
    assert len([line for line in manifest if line.endswith('  ' + installer_name)]) == 1, 'installer must appear exactly once in the final manifest'
    for line in manifest:
        digest, name = line.split('  ', 1)
        assert digest == hashlib.sha256((output / name).read_bytes()).hexdigest(), 'stale digest: ' + name
    print('PASS installer build leaves a complete matching SHA256 manifest')
print('PASS packaging smoke completed all enabled checks')
