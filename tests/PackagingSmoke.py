"""Build release archives from a fresh source tree with no local work directory."""
from pathlib import Path
import argparse
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
version = re.search(r'DisplayVersion="([^"]+)"', (PROJECT / 'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')).group(1)
arguments = argparse.ArgumentParser()
arguments.add_argument('--compiler', help='Optional Inno ISCC.exe for full fresh installer verification')
arguments.add_argument('--legacy-worker', type=Path, help='Existing older DeskPet.Update.exe to verify backward compatible packages')
options = arguments.parse_args()
root = PROJECT / 'work' / ('source-smoke-' + uuid.uuid4().hex)
checkout = root / 'DafeiyuDeskPet'
shutil.copytree(PROJECT, checkout, ignore=shutil.ignore_patterns(
    '.git', 'work', 'bin', 'obj', 'data', '__pycache__', '.superpowers', 'superpowers', 'research',
    'motion', 'motion-source', 'drawn-source*', 'PackDrawnMotion.py', 'PackXiaobei.py', 'MotionSourceExport.cs', 'SmoothMotionTests.cs'))
assert not (checkout/'assets/motion').exists(), 'Clean builds must not depend on retired motion artwork'
assert (checkout/'templates/high-fps-v2/pet.json').is_file(), 'Author template must be in the repository'
output = checkout / 'public-artifacts'
runtime = output / 'DafeiyuDeskPet'
result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                         '-File', str(checkout / 'Build.ps1'), '-Target', 'App', '-OutputDirectory', str(runtime)],
                        cwd=checkout, capture_output=True, timeout=60)
assert result.returncode == 0, result.stdout.decode('utf-8', errors='replace') + result.stderr.decode('utf-8', errors='replace')
print('PASS application compiles from the fresh public source tree')
# A reused portable folder must not leak previous executables or private loose files.
(runtime / '大肥鱼桌宠.exe').write_bytes(b'legacy binary, do not ship')
(runtime / 'private-key.txt').write_text('private sentinel', encoding='utf-8')
(runtime / 'data').mkdir()
(runtime / 'data/settings.json').write_text('private settings sentinel', encoding='utf-8')
for private_name in ['.env', '.env.local', 'credentials.json', 'release-signing.pfx']:
    (checkout / private_name).write_text('private source sentinel', encoding='utf-8')
(checkout / '.codex').mkdir(exist_ok=True)
(checkout / '.codex/auth.json').write_text('private auth sentinel', encoding='utf-8')
result = subprocess.run([sys.executable, str(checkout / 'Package.py'), '--output-directory', str(output)], cwd=checkout,
                        env=dict(os.environ,PYTHONIOENCODING='utf-8'),
                        capture_output=True, text=True, encoding='utf-8', timeout=180)
if result.returncode:
    print(result.stdout + result.stderr)
    raise AssertionError('fresh source packaging needs no local test artifacts')
print('PASS fresh source packaging needs no local test artifacts')
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
    for document in ['skin-format.md', 'dialogue-setup.md', 'conversation-agent.md', 'native-agent-design.md', 'third-party-notices.md', 'sources.md', 'installation.md', 'daily-use.md', 'voice-presets.md', 'release-naming.md', 'interaction-extension.md']:
        assert bundle.read('DafeiyuDeskPet/docs/' + document).decode('utf-8-sig').replace('\r\n','\n') == (checkout / 'docs' / document).read_text(encoding='utf-8-sig')
print('PASS runtime includes the exact daily-use and voice-preset guides')
for pet in ['Ebao', 'Dafeiyu', 'Marfu', 'Xiaobei']:
    archive = output / (pet + '-HD-Skin-' + version + '.zip')
    assert archive.is_file(), 'versioned ASCII skin archive missing: ' + archive.name
    with ZipFile(archive) as bundle:
        assert bundle.testzip() is None and 'pet.json' in bundle.namelist()
        skin=json.loads(bundle.read('pet.json'))
        assert skin['version']==2 and len(skin['animations'])==17 and skin['gaze']['frames']==16
        assert skin['system_prompt'] and skin['special_action']
        expected_counts=[6,8,8,4,5,8,6,6,6]+[4]*8
        action_names=['idle','running-right','running-left','waving','jumping','failed','waiting','running','review','lifted','landing','petting','poked','feeding','happy','sleeping','special']
        assert [skin['animations'][name]['frames'] for name in action_names]==([8]*17 if pet=='Xiaobei' else expected_counts)
        for record in skin['animations'].values():
            assert record['columns']==8 and 1<=record['frames']<=8 and record['file'] in bundle.namelist()
        assert skin['gaze']['file'] in bundle.namelist() and skin['preview'] in bundle.namelist()
with ZipFile(output/('DeskPet-Skin-Template-v2-'+version+'.zip')) as bundle:
    assert 'high-fps-v2/pet.json' in bundle.namelist() and 'high-fps-v2/动作表.csv' in bundle.namelist()
with ZipFile(output/('DeskPet-Windows-'+version+'.zip')) as bundle:
    assert 'DafeiyuDeskPet/docs/configuration-guide.md' in bundle.namelist()
    assert '118' in bundle.read('DafeiyuDeskPet/docs/configuration-guide.md').decode('utf-8-sig')
    assert 'DafeiyuDeskPet/templates/grid-guide.svg' in bundle.namelist()
    assert 'DafeiyuDeskPet/templates/high-fps-v2/pet.json' in bundle.namelist()
    assert not any('/pets/' in name or name.endswith('/verification.md') for name in bundle.namelist())
print('PASS four complete low-frame v2 skin packs and full author templates')
installer_recipe=(checkout/'installer/DeskPet.iss').read_text(encoding='utf-8-sig')
with ZipFile(output/('DeskPet-Windows-'+version+'.zip')) as bundle:
    for name in bundle.namelist():
        if name.startswith('DafeiyuDeskPet/docs/') and name.endswith('.md'):
            assert '\\docs\\'+Path(name).name+'"' in installer_recipe, 'installer documentation missing: '+name
print('PASS installer and portable package include the same public documentation')

manifest = (output / 'SHA256.txt').read_text(encoding='utf-8-sig').splitlines()
for line in manifest:
    digest, name = line.split('  ', 1)
    assert digest == hashlib.sha256((output / name).read_bytes()).hexdigest(), 'stale digest: ' + name
for pet in ['Ebao', 'Dafeiyu', 'Marfu', 'Xiaobei']:
    assert any(line.endswith('  ' + pet + '-HD-Skin-' + version + '.zip') for line in manifest)
print('PASS versioned ASCII skin names and manifest hashes match release artifacts')
result = subprocess.run(['powershell.exe', '-NoProfile', '-Command',
    '[Reflection.AssemblyName]::GetAssemblyName($env:DESKPET_TEST_HELPER).Version.ToString()'],
    env=dict(os.environ, DESKPET_TEST_HELPER=str(runtime / 'DeskPet.Update.exe')),
    capture_output=True, check=True)
assert result.stdout.decode('ascii').strip() == version + '.0'
assert (runtime / 'DeskPet.Update.exe').stat().st_size < 1024 * 1024, 'helper should not embed pet sprite assets'
print('PASS update helper has the declared version and excludes sprite resources')
result=subprocess.run(['powershell.exe','-NoProfile','-Command',
    '$a=[Reflection.Assembly]::LoadFile($env:DESKPET_TEST_APP); $a.GetManifestResourceNames()'],
    env=dict(os.environ,DESKPET_TEST_APP=str(runtime/'桌面伙伴.exe')),capture_output=True,check=True)
resources=result.stdout.decode('utf-8',errors='replace')
assert 'Dafeiyu.Motion.' not in resources and 'Dafeiyu.MotionManifest' not in resources
assert 'Dafeiyu.Bundled.Xiaobei.pet.json' in resources
print('PASS final application embeds low-frame Xiaobei and no retired motion fixtures')
readme = (runtime / 'README.md').read_text(encoding='utf-8-sig')
images = re.findall(r'<img\s+src="([^"]+)"|!\[[^\]]*\]\(([^)]+)\)', readme)
for alternatives in images:
    target = next(value for value in alternatives if value)
    assert (runtime / target).is_file(), 'installed README image missing: ' + target
    source_target = target.replace('previews/avatars/', 'assets/avatars/')
    assert (checkout / source_target).is_file(), 'repository README image missing: ' + source_target
    if target.startswith('previews/avatars/'):
        assert (runtime / target).read_bytes() == (checkout / source_target).read_bytes(), 'packaged avatar changed: ' + target
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
        assert bundle.read(name) == (root / name).read_bytes(), 'source archive differs: ' + name
print('PASS source archive contains the exact public source files')
if options.compiler:
    result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                             '-File', str(checkout / 'BuildInstaller.ps1'),
                             '-Compiler', str(Path(options.compiler).resolve()), '-OutputDirectory', str(output)],
                            cwd=checkout, capture_output=True, timeout=60)
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
