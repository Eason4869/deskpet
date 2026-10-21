import argparse
import uuid
from tools.runtime_package import collect_runtime_files, stage_runtime, validate_binaries
import csv
import io
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import re
from zipfile import ZipFile, ZIP_DEFLATED
from PIL import Image
from tools.release_assets import installer_asset_name
from tools.package_gugugaga import package as package_gugugaga

PROJECT=Path(__file__).resolve().parent
arguments=argparse.ArgumentParser(description='Create clean versioned DeskPet release archives.')
arguments.add_argument('--output-directory',type=Path,help='Release output root; its DafeiyuDeskPet subdirectory must contain the built app')
arguments.add_argument('--template-directory',type=Path,help='Completed v2 authoring template directory')
arguments.add_argument('--check-inputs',action='store_true',help='Inspect packaging inputs without copying or creating archives')
options=arguments.parse_args()
OUTPUT=(options.output_directory or PROJECT.parent/'outputs').resolve()
if OUTPUT==PROJECT:
    arguments.error('The output directory must be separate from the source root.')
bundled_template=PROJECT/'templates/high-fps-v2'
TEMPLATE=(options.template_directory or (bundled_template if bundled_template.is_dir() else PROJECT.parent/'high-fps-pet-template-v2')).resolve()
template_manifest=json.loads((TEMPLATE/'pet.json').read_text(encoding='utf-8-sig'))
assert template_manifest['version']==2 and len(template_manifest['animations'])==17, 'Generate the v2 template first.'
template_files=[p for p in sorted(TEMPLATE.rglob('*')) if p.is_file() and p.suffix.lower() in {'.png','.json','.svg','.md','.txt','.csv'}]
# Retired high-frame generations remain local; releases retain original runtime
# atlases, the low-frame bundled preset, public tests and authoring templates.
retired_source_tools={'PackDrawnMotion.py','PackXiaobei.py','tests/MotionSourceExport.cs','tests/SmoothMotionTests.cs'}
def include_source(file):
    relative=file.relative_to(PROJECT)
    if not file.is_file() or OUTPUT in file.resolve().parents:return False
    if any(part.lower() in {'work','bin','obj','data','outputs','research','__pycache__','.git','.superpowers','.codex','.agents','.aws','.ssh'} for part in relative.parts):return False
    if file.name.lower() in {'.env','credentials','credentials.json','auth.json','id_rsa','id_ed25519'} or file.name.lower().startswith('.env.'):return False
    name=relative.as_posix()
    if name.startswith(('docs/superpowers/','assets/drawn-source','assets/motion-source/','assets/motion/')) or name in retired_source_tools:return False
    if file.suffix.lower() in {'.log','.tmp','.pyc','.pdb','.pfx','.p12','.key'}:return False
    return True
source_files=[p for p in sorted(PROJECT.rglob('*')) if include_source(p)]
runtime_plan=collect_runtime_files(PROJECT,OUTPUT/'DafeiyuDeskPet')
if options.check_inputs:
    print(json.dumps({'runtime_files':runtime_plan,'source_files':len(source_files),'template_files':len(template_files),'archives_created':False},ensure_ascii=False,indent=2))
    raise SystemExit(0)
OUTPUT.mkdir(parents=True,exist_ok=True)
APP=OUTPUT/'DafeiyuDeskPet'
release_source=(PROJECT/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')
version=re.search(r'DisplayVersion="([^"]+)"',release_source).group(1)
validate_binaries(PROJECT,APP)
RUNTIME=OUTPUT/('runtime-'+uuid.uuid4().hex)
stage_runtime(runtime_plan,RUNTIME)
(OUTPUT/'runtime-stage.json').write_text(json.dumps({'path':str(RUNTIME)}),encoding='utf-8')

# Supplemental builtin artwork is embedded in the executable. Check every
# authored interaction frame before distributing the runtime and source archives.
for pet in ['e-bao', 'dafeiyu', 'marfu']:
    with Image.open(PROJECT/'assets'/(pet+'-interactions.png')) as image:
        assert image.size == (1920,4160) and image.mode == 'RGBA', pet
        for row in range(8):
            for col in range(4):
                bbox=image.crop((col*480,row*520,(col+1)*480,(row+1)*520)).getbbox()
                assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<480 and bbox[3]<520, (pet,row,col,bbox)
    print('PASS interaction source validation: '+pet)

counts=[6,8,8,4,5,8,6,6,6,8,8]
for prefix in ['', 'e-bao-', 'marfu-']:
    with Image.open(PROJECT/'assets'/(prefix+'spritesheet.png')) as image:
        assert image.size==(3840,5720) and image.mode=='RGBA'
        for row,count in enumerate(counts):
            for col in range(8):
                frame=image.crop((col*480,row*520,(col+1)*480,(row+1)*520))
                bbox=frame.getbbox()
                if col<count:
                    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<480 and bbox[3]<520, (prefix,row,col,bbox)
                else:
                    assert bbox is None, (prefix,row,col,'unused cell')
    print('PASS HD source validation: '+(prefix or 'dafeiyu'))

runtime_files=[RUNTIME/entry['destination'] for entry in runtime_plan]
runtime_zip=OUTPUT/('DeskPet-Windows-'+version+'.zip')
with ZipFile(runtime_zip,'w',ZIP_DEFLATED,compresslevel=6) as bundle:
    for file in sorted(runtime_files): bundle.write(file,'DafeiyuDeskPet/'+file.relative_to(RUNTIME).as_posix())
source_zip=OUTPUT/('DeskPet-Source-'+version+'.zip')
with ZipFile(source_zip,'w',ZIP_DEFLATED,compresslevel=6) as bundle:
    for file in source_files:
        bundle.write(file,'DafeiyuDeskPet/'+file.relative_to(PROJECT).as_posix())
    for file in template_files:
        if file not in source_files:bundle.write(file,'DafeiyuDeskPet/templates/high-fps-v2/'+file.relative_to(TEMPLATE).as_posix())

template_zip=OUTPUT/('DeskPet-Skin-Template-v2-'+version+'.zip')
with ZipFile(template_zip,'w',ZIP_DEFLATED,compresslevel=6) as bundle:
    for file in template_files:bundle.write(file,'high-fps-v2/'+file.relative_to(TEMPLATE).as_posix())
    for file in sorted((PROJECT/'templates').glob('*')):
        if file.is_file() and file.name!='runtime-readme.md' and file.suffix.lower() in {'.png','.json','.svg','.md','.txt','.csv'}:
            bundle.write(file,'legacy-v1/'+file.name)

skin_export=PROJECT/'work/release-skin-export'
subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(PROJECT/'Build.ps1'),'-Target','SkinPackExport','-TestArguments',str(skin_export)],cwd=PROJECT,check=True,timeout=180)
skin_artifacts=[]
for pet,release_name in [('e-bao','Ebao'),('dafeiyu','Dafeiyu'),('marfu','Marfu'),('xiaobei','Xiaobei')]:
    folder=PROJECT/'assets/bundled-skins/xiaobei' if pet=='xiaobei' else skin_export/pet
    manifest=json.loads((folder/'pet.json').read_text(encoding='utf-8-sig'))
    assert manifest['version']==2 and len(manifest['animations'])==17
    assert manifest.get('system_prompt') and manifest.get('special_action'), 'Complete skins include persona and special action.'
    assert all(1<=action['frames']<=8 for action in manifest['animations'].values()), 'Release skins use at most eight original keyframes.'
    files={'pet.json',manifest['preview'],manifest['gaze']['file']} | {action['file'] for action in manifest['animations'].values()}
    for name in files:
        path=Path(name)
        assert not path.is_absolute() and '..' not in path.parts and (folder/path).is_file(), 'Skin source must be self-contained.'
    skin_zip=OUTPUT/(release_name+'-HD-Skin-'+version+'.zip');skin_artifacts.append(skin_zip)
    with ZipFile(skin_zip,'w',ZIP_DEFLATED,compresslevel=6) as bundle:
        for name in sorted(files):bundle.write(folder/name,name)
        bundle.write(PROJECT/'docs/skin-format.md','外观格式说明.md')

skin_artifacts.append(package_gugugaga(OUTPUT, release_version=version))

with ZipFile(runtime_zip) as bundle:
    assert bundle.testzip() is None
    assert not any('/data/' in name for name in bundle.namelist())
    assert not any(name.endswith(('.cs','.py','.ps1','.log','.gif','/verification.md')) or '/tests/' in name or '/work/' in name for name in bundle.namelist())
    assert not any('/templates/' in name or '/previews/' in name for name in bundle.namelist())
    assert set(bundle.namelist())=={'DafeiyuDeskPet/'+Path(entry['destination']).as_posix() for entry in runtime_plan}
    exe_name='DafeiyuDeskPet/桌面伙伴.exe'
    assert hashlib.sha256(bundle.read(exe_name)).digest()==hashlib.sha256((APP/'桌面伙伴.exe').read_bytes()).digest()
with ZipFile(source_zip) as bundle: assert bundle.testzip() is None
app_asset=OUTPUT/('DeskPet-Windows-App-'+version+'.exe')
shutil.copy2(APP/'桌面伙伴.exe',app_asset)
artifacts=[app_asset,runtime_zip,source_zip,template_zip]+skin_artifacts
setup=OUTPUT/installer_asset_name(version)
if setup.exists(): artifacts.append(setup)
(OUTPUT/'SHA256.txt').write_text('\n'.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+str(p.relative_to(OUTPUT)) for p in artifacts)+'\n',encoding='utf-8')
print('PASS archive integrity, executable identity and clean settings')
for file in artifacts: print(str(file)+' (%0.1f MB)'%(file.stat().st_size/(1024*1024)))
