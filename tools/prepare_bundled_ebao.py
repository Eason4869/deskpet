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
                                      check_manifest, safe_path, collect_role, collect_reviewed_role, write_json, write_archive, manifest_references)


BUNDLE_SCHEMA = 2
BUNDLE_COVERAGE = 'defaults'


def default_references(manifest):
    references = manifest_references(manifest) | {'companions.json'}
    if any(name.startswith('holidays/') for name in references):
        raise ValueError('Default coverage must not borrow wardrobe files')
    return references


def checked_entries(archive, root):
    entries = archive.infolist()
    if not entries or len(entries) > 1024 or len({i.filename.casefold() for i in entries}) != len(entries):
        raise ValueError('Invalid bundled inventory')
    if sum(i.file_size for i in entries) > 512 * 1024 * 1024:
        raise ValueError('Oversized bundled source')
    for entry in entries:
        safe_path(root, entry.filename)
        if entry.is_dir() or not 0 <= entry.file_size <= 25 * 1024 * 1024 or (entry.external_attr >> 16) & 0xf000 == 0xa000:
            raise ValueError('Invalid bundled entry')
    return entries


def semantic_bytes(name, data):
    if name not in ('pet.json', 'companions.json'):
        return data
    value = json.loads(data.decode('utf-8-sig'))
    for key in (('pack_version',) if name == 'pet.json' else ('version', 'min_app_version')):
        value.pop(key, None)
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')


def check_identity(pin, manifest, identity):
    if (manifest.get('id'), manifest.get('pack_version'), identity.get('id'), identity.get('version'), identity.get('min_app_version')) != (
            'e-bao', pin['pack_version'], 'e-bao', pin['pack_version'], pin['min_app_version']):
        raise ValueError('Bundled source identity does not match pin')


def file_hash(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def prepare(repository, package, output):
    pin = read_json(repository / 'assets/official/companion-versions.json')['companions']['e-bao']
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='ebao-', dir=output) as temporary:
        root = Path(temporary)
        with zipfile.ZipFile(package) as archive:
            for entry in checked_entries(archive, root):
                target = safe_path(root, entry.filename)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.read(entry))
        manifest = read_json(root / 'pet.json')
        check_identity(pin, manifest, read_json(root / 'companions.json'))
        complete = {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
        # Derivation is allowed only from the complete, unchanged canonical source.
        verify_material_record(pin, payload_digest(root, complete))
        check_manifest(root, manifest, 'e-bao', pin['pack_version'])
        files = default_references(manifest)
        if not files <= complete:
            raise ValueError('Incomplete default manifest closure')
        subset = root / 'defaults'
        subset.mkdir()
        for name in files:
            target = safe_path(subset, name); target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(root / name, target)
        derived = root / 'derived.zip'
        write_archive(subset, files, derived)
        inventory = {'schema_version': BUNDLE_SCHEMA, 'coverage': BUNDLE_COVERAGE,
                     'version': pin['pack_version'], 'min_app_version': pin['min_app_version'],
                     'payload_sha256': pin['payload_sha256'], 'canonical_zip_sha256': file_hash(package),
                     'zip_sha256': file_hash(derived),
                     'files': [{'path': name, 'length': (root / name).stat().st_size,
                                'sha256': file_hash(root / name)} for name in sorted(files)]}
        # The full source is a BUILD proof, never an embedded/package asset or an
        # installed receipt. Retaining it lets the read-only gate recheck the pin.
        shutil.copyfile(package, output / 'e-bao.canonical.zip.new')
        shutil.copyfile(derived, output / 'e-bao.zip.new')
        (output / 'e-bao.inventory.json.new').write_text(json.dumps(inventory, separators=(',', ':')), encoding='utf-8')
        os.replace(output / 'e-bao.canonical.zip.new', output / 'e-bao.canonical.zip')
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
    digest = hashlib.sha256(('bundle-schema-'+str(BUNDLE_SCHEMA)+'-'+BUNDLE_COVERAGE).encode()+b''.join(str(p.relative_to(repository)).encode()+hashlib.sha256(p.read_bytes()).digest() for p in sorted(set(inputs))))
    catalog=read_json(repository/'assets/locales/zh_CN.json')
    for key in ('personaProfiles.defaultsUpdated.ebao', 'skinCatalog.skinDefinition.descriptionDescription'):
        digest.update(key.encode('utf-8')+b'\0'+catalog[key].encode('utf-8'))
    return digest.hexdigest()


def verify_prepared(repository, output):
    """Read-only full-source proof and exact default-coverage gate; no extraction."""
    pin = read_json(repository / 'assets/official/companion-versions.json')['companions']['e-bao']
    inventory = read_json(output / 'e-bao.inventory.json')
    if inventory.get('schema_version') != BUNDLE_SCHEMA or inventory.get('coverage') != BUNDLE_COVERAGE:
        raise ValueError('Prepared Ebao coverage is stale; prepare it again')
    if (inventory['version'], inventory['min_app_version'], inventory['payload_sha256']) != (pin['pack_version'], pin['min_app_version'], pin['payload_sha256']):
        raise ValueError('Prepared Ebao no longer matches the material pin; prepare it again')
    canonical = output / 'e-bao.canonical.zip'
    if file_hash(canonical) != inventory['canonical_zip_sha256'] or file_hash(output / 'e-bao.zip') != inventory['zip_sha256']:
        raise ValueError('Prepared Ebao archive changed')
    with zipfile.ZipFile(canonical) as source, zipfile.ZipFile(output / 'e-bao.zip') as derived:
        source_entries = checked_entries(source, output)
        digest = hashlib.sha256()
        for entry in sorted(source_entries, key=lambda i: i.filename):
            data = semantic_bytes(entry.filename, source.read(entry))
            digest.update(entry.filename.encode('utf-8') + b'\0' + str(len(data)).encode('ascii') + b'\0' + hashlib.sha256(data).digest())
        verify_material_record(pin, digest.hexdigest())
        manifest = json.loads(source.read('pet.json').decode('utf-8-sig'))
        check_identity(pin, manifest, json.loads(source.read('companions.json').decode('utf-8-sig')))
        expected = default_references(manifest)
        entries = checked_entries(derived, output)
        declared = {entry['path']: entry for entry in inventory['files']}
        if len(declared) != len(inventory['files']) or set(declared) != expected or {i.filename for i in entries} != expected:
            raise ValueError('Prepared Ebao does not cover the exact default manifest closure')
        for entry in entries:
            data = derived.read(entry); record = declared[entry.filename]
            if len(data) != record['length'] or hashlib.sha256(data).hexdigest() != record['sha256'] or data != source.read(entry.filename):
                raise ValueError('Prepared default bytes differ from the pinned canonical source')


def ensure(repository, output):
    stamp=input_stamp(repository)
    marker = output / 'inputs.txt'
    if marker.exists() and marker.read_text() == stamp and (output / 'e-bao.zip').is_file() and (output / 'e-bao.inventory.json').is_file():
        verify_prepared(repository, output)
        return
    canonical = output / 'e-bao.canonical.zip'
    if canonical.is_file():
        try:
            prepare(repository, canonical, output)
            return
        except ValueError:
            pass  # A changed material pin needs a new canonical authoring input.
    if os.name != 'nt':
        raise RuntimeError('Prepare pinned Ebao with --package <Windows-authored Ebao ZIP> before building on this platform.')
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='author-', dir=output) as temporary:
        temporary=Path(temporary)
        record=read_json(repository/'assets/official/companion-versions.json')['companions']['e-bao']
        source=temporary/'e-bao'
        reviewed=repository/'assets/complete-defaults'
        if reviewed.is_dir():
            identity, outfits, references=collect_reviewed_role(repository,reviewed,source,'e-bao',record['pack_version'])
        else:
            defaults=temporary/'defaults'
            subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(repository/'Build.ps1'),'-Target','SkinPackExport','-TestArguments',str(defaults)],cwd=repository,check=True)
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
        print('Verified default Ebao coverage and complete canonical build proof')
    elif args.package:
        result = prepare(repo, args.package.resolve(), args.output.resolve())
        print('Verified bundled Ebao ' + result['version'] + ': ' + str(len(result['files'])) + ' entries')
    else:
        ensure(repo, args.output.resolve())
