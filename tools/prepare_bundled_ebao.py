"""Prepare the pinned, read-on-demand embedded Ebao source. Never runs in the app."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

from build_official_companions import (read_json, payload_digest, verify_material_record,
                                      check_manifest, safe_path, collect_role, write_json, write_archive)


def prepare(repository, package, output):
    registry = read_json(repository / 'assets/official/companion-versions.json')['companions']['e-bao']
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='ebao-', dir=output) as temporary:
        root = Path(temporary)
        with zipfile.ZipFile(package) as archive:
            if len(archive.infolist()) > 1024 or len(set(archive.namelist())) != len(archive.infolist()):
                raise ValueError('Invalid bundled inventory')
            if sum(i.file_size for i in archive.infolist()) > 512 * 1024 * 1024:
                raise ValueError('Oversized bundled source')
            for entry in archive.infolist():
                target = safe_path(root, entry.filename)
                if entry.is_dir() or entry.file_size > 25 * 1024 * 1024:
                    raise ValueError('Invalid bundled entry')
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.read(entry))
        manifest = read_json(root / 'pet.json')
        identity = read_json(root / 'companions.json')
        if identity['id'] != 'e-bao' or identity['version'] != registry['pack_version'] or identity['min_app_version'] != registry['min_app_version']:
            raise ValueError('Bundled source identity does not match pin')
        files = {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
        verify_material_record(registry, payload_digest(root, files))
        check_manifest(root, manifest, 'e-bao', registry['pack_version'])
        inventory = {'version': registry['pack_version'], 'min_app_version': registry['min_app_version'],
                     'payload_sha256': registry['payload_sha256'], 'zip_sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
                     'files': [{'path': name, 'length': (root / name).stat().st_size,
                                'sha256': hashlib.sha256((root / name).read_bytes()).hexdigest()} for name in sorted(files)]}
        shutil.copyfile(package, output / 'e-bao.zip.new')
        (output / 'e-bao.inventory.json.new').write_text(json.dumps(inventory, separators=(',', ':')), encoding='utf-8')
        os.replace(output / 'e-bao.zip.new', output / 'e-bao.zip')
        os.replace(output / 'e-bao.inventory.json.new', output / 'e-bao.inventory.json')
    (output / 'inputs.txt').write_text(input_stamp(repository))
    return inventory


def input_stamp(repository):
    inputs = [repository / name for name in ('assets/official/companion-versions.json', 'assets/holidays/catalog.json',
              'assets/additional/e-bao.png', 'assets/avatars/e-bao.png',
              'src/PersonaProfiles.cs', 'src/TextResources.cs', 'Build.ps1', 'src/SpriteAlignment.cs', 'src/SpriteAtlas.cs', 'src/InteractionAtlas.cs',
              'src/SkinCatalog.cs', 'src/AdditionalInteractions.cs', 'src/CompanionQuietClips.cs', 'src/PetBusiness.cs', 'tests/SkinPackExport.cs', 'tools/build_official_companions.py', 'tools/prepare_bundled_ebao.py')]
    inputs += list((repository / 'assets').glob('e-bao-*.png'))
    inputs += [p for p in (repository / 'assets').rglob('*') if p.is_file() and 'e-bao' in p.parts]
    digest = hashlib.sha256(b''.join(str(p.relative_to(repository)).encode()+hashlib.sha256(p.read_bytes()).digest() for p in sorted(set(inputs))))
    catalog=read_json(repository/'assets/locales/zh_CN.json')
    for key in ('personaProfiles.defaultsUpdated.ebao', 'skinCatalog.skinDefinition.descriptionDescription'):
        digest.update(key.encode('utf-8')+b'\0'+catalog[key].encode('utf-8'))
    return digest.hexdigest()


def verify_prepared(repository, output):
    """Read-only production gate: no source export, extraction or cache mutation."""
    pin=read_json(repository/'assets/official/companion-versions.json')['companions']['e-bao']
    inventory=read_json(output/'e-bao.inventory.json')
    if (inventory['version'],inventory['min_app_version'],inventory['payload_sha256']) != (pin['pack_version'],pin['min_app_version'],pin['payload_sha256']):
        raise ValueError('Prepared Ebao no longer matches the material pin; prepare it again')
    package=output/'e-bao.zip'
    with package.open('rb') as stream:
        archive_hash=hashlib.sha256()
        for chunk in iter(lambda:stream.read(1024*1024),b''):
            archive_hash.update(chunk)
        if archive_hash.hexdigest()!=inventory['zip_sha256']:
            raise ValueError('Prepared Ebao archive changed')
    files={entry['path']:entry for entry in inventory['files']}
    digest=hashlib.sha256()
    with zipfile.ZipFile(package) as archive:
        if len(files)>1024 or len(files)!=len(inventory['files']) or len(archive.infolist())!=len(files) or set(archive.namelist())!=set(files):
            raise ValueError('Prepared Ebao inventory changed')
        for name in sorted(files):
            entry=files[name]
            if not 0<=entry['length']<=25*1024*1024 or archive.getinfo(name).file_size!=entry['length']:
                raise ValueError('Prepared Ebao entry length changed')
            data=archive.read(name)
            if hashlib.sha256(data).hexdigest()!=entry['sha256']:
                raise ValueError('Prepared Ebao entry changed')
            if name in ('pet.json','companions.json'):
                value=json.loads(data)
                if value.get('id')!='e-bao' or name=='companions.json' and value.get('min_app_version')!=pin['min_app_version']:
                    raise ValueError('Prepared Ebao identity or compatibility changed')
                version_key='pack_version' if name=='pet.json' else 'version'
                if value[version_key]!=pin['pack_version']:
                    raise ValueError('Prepared Ebao manifest version changed')
                for key in (('pack_version',) if name=='pet.json' else ('version','min_app_version')):
                    value.pop(key,None)
                data=json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',', ':'),allow_nan=False).encode('utf-8')
            digest.update(name.encode('utf-8')+b'\0'+str(len(data)).encode('ascii')+b'\0'+hashlib.sha256(data).digest())
    verify_material_record(pin,digest.hexdigest())


def ensure(repository, output):
    stamp=input_stamp(repository)
    marker = output / 'inputs.txt'
    if marker.exists() and marker.read_text() == stamp and (output / 'e-bao.zip').is_file() and (output / 'e-bao.inventory.json').is_file():
        verify_prepared(repository, output)
        return
    if os.name != 'nt':
        raise RuntimeError('Prepare pinned Ebao with --package <Windows-authored Ebao ZIP> before building on this platform.')
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='author-', dir=output) as temporary:
        temporary=Path(temporary)
        defaults=temporary/'defaults'
        subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(repository/'Build.ps1'),'-Target','SkinPackExport','-TestArguments',str(defaults)],cwd=repository,check=True)
        record=read_json(repository/'assets/official/companion-versions.json')['companions']['e-bao']
        source=temporary/'e-bao'
        identity, outfits, references=collect_role(repository,defaults,source,'e-bao',record['pack_version'])
        verify_material_record(record,payload_digest(source,references))
        identity['min_app_version']=record['min_app_version'];write_json(source/'companions.json',identity)
        package=temporary/'e-bao.zip';write_archive(source,references,package)
        prepare(repository,package,output)
    marker.write_text(stamp)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path)
    parser.add_argument('--verify', action='store_true', help='Read-only verification of a prepared bundle before compilation')
    parser.add_argument('--output', type=Path, default=Path('work/bundled-ebao'))
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    if args.verify:
        verify_prepared(repo,args.output.resolve())
        print('Verified prepared Ebao archive, inventory and material pin')
    elif args.package:
        result = prepare(repo, args.package.resolve(), args.output.resolve())
        print('Verified bundled Ebao ' + result['version'] + ': ' + str(len(result['files'])) + ' entries')
    else:
        ensure(repo, args.output.resolve())
