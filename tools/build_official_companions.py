"""Build complete, lossless official companion packages from repository sources.

Windows authoring invokes the existing pixel-verified SkinPackExport by default.
An explicit --defaults directory accepts that export on another build host.
Native client validation is a separate publication gate; this tool never publishes.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

from PIL import Image

POLICY = json.loads((Path(__file__).resolve().parents[1] / 'assets/official/companion-package-policy.json').read_text(encoding='utf-8'))

IDS = ('e-bao', 'dafeiyu', 'marfu', 'xiaobei', 'gugugaga')
BASE = ('idle', 'running-right', 'running-left', 'waving', 'jumping', 'failed', 'waiting', 'running', 'review')
INTERACTIONS = ('lifted', 'landing', 'petting', 'poked', 'feeding', 'happy', 'sleeping', 'special')
ALTERNATE = ('lifted', 'landing', 'feeding', 'happy', 'sleeping', 'special', 'special2', 'stumble')
EXTRA = ('stumble', 'special2')
VERSION = re.compile(r'([0-9]+)\.([0-9]+)\.([0-9]+)(?:(?:\.([0-9]+))|(?:-dev(?:\.([1-9][0-9]*))?))?')


def safe_path(root, name):
    if not isinstance(name, str) or not name or len(name) > 200 or '\\' in name or ':' in name or any(p in ('', '.', '..') or p.endswith(('.', ' ')) or any(ord(c) < 32 or c in '<>"|?*' for c in p) or re.fullmatch(r'(?i)(?:con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\..*)?', p) for p in name.split('/')):
        raise ValueError('Invalid package path: ' + str(name))
    root = Path(root).resolve()
    path = root.joinpath(*name.split('/'))
    resolved = path.resolve()
    if not resolved.is_relative_to(root) or resolved != path:
        raise ValueError('Package path escapes its root or uses a link: ' + name)
    return path


def read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('Duplicate JSON field: ' + key)
            result[key] = value
        return result
    if path.stat().st_size > 1024 * 1024:
        raise ValueError('JSON exceeds 1 MiB: ' + str(path))
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def package_name(role, material_version):
    return 'DeskPet-Companion-' + role + '-' + material_version + '.zip'


def payload_digest(root, references):
    """v1 semantic payload: exclude only fields formerly rewritten per app release."""
    digest = hashlib.sha256()
    for name in sorted(references):
        data = safe_path(root, name).read_bytes()
        if name in ('pet.json', 'companions.json'):
            value = read_json(safe_path(root, name))
            for key in (('pack_version',) if name == 'pet.json' else ('version', 'min_app_version')):
                value.pop(key, None)
            data = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')
        digest.update(name.encode('utf-8') + b'\0' + str(len(data)).encode('ascii') + b'\0' + hashlib.sha256(data).digest())
    return digest.hexdigest()


def verify_material_record(record, actual):
    for field in ('pack_version', 'min_app_version'):
        if not VERSION.fullmatch(record[field]):
            raise ValueError('Invalid material version record')
    if record['payload_sha256'] != actual:
        raise ValueError('Official material changed; review and advance its material version record before building')


def material_order(version):
    match = VERSION.fullmatch(version)
    if match is None:
        raise ValueError('Invalid material version')
    major, minor, patch, revision, preview = match.groups()
    return (int(major), int(minor), int(patch), int(revision or 0), int(preview is None), int(preview or 0))


def verify_material_transition(previous, current):
    changed = (previous['payload_sha256'], previous['min_app_version']) != (current['payload_sha256'], current['min_app_version'])
    order = (material_order(current['pack_version']) > material_order(previous['pack_version'])) - (material_order(current['pack_version']) < material_order(previous['pack_version']))
    if (changed and order <= 0) or (not changed and order != 0):
        raise ValueError('Changed material requires a higher version; unchanged material must keep its version')


def verify_registry_history(repository, registry):
    if subprocess.check_output(['git', 'rev-parse', '--is-shallow-repository'], cwd=repository, text=True).strip() != 'false':
        raise ValueError('Material history validation requires fetch-depth: 0')
    path = 'assets/official/companion-versions.json'
    commits = subprocess.check_output(['git', 'log', '-2', '--format=%H', '--', path], cwd=repository, text=True).splitlines()
    for commit in commits:
        previous = json.loads(subprocess.check_output(['git', 'show', commit + ':' + path], cwd=repository))
        if previous == registry:
            continue
        for role in IDS:
            verify_material_transition(previous['companions'][role], registry['companions'][role])
        break


def create_output(repository, output, version):
    match = VERSION.fullmatch(version)
    if len(version) > 64 or match is None or any(int(part) > 65534 for part in match.groups()[:4] if part is not None) or (match.group(5) is not None and int(match.group(5)) > 2147483647):
        raise ValueError('Supply a canonical unprefixed release version.')
    repository, output = Path(repository).resolve(), Path(output).resolve()
    if output == repository or repository.is_relative_to(output) or output.exists():
        raise ValueError('Output must be a new owned directory, separate from the repository root.')
    output.mkdir(parents=True, exist_ok=False)
    return output


def copy_reference(source, destination, name):
    original = safe_path(source, name)
    target = safe_path(destination, name)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(original, target)
    if original.read_bytes() != target.read_bytes():
        raise ValueError('Source byte mismatch: ' + name)


def manifest_references(manifest):
    references = {'pet.json', manifest['preview'], manifest['gaze']['file']}
    if manifest.get('avatar'):
        references.add(manifest['avatar'])
    for group in ('animations', 'extra_animations', 'alternate_animations'):
        references.update(clip['file'] for clip in manifest.get(group, {}).values())
    return references


def check_png(path, expected_size=None):
    if path.stat().st_size > 25 * 1024 * 1024:
        raise ValueError('PNG exceeds official package limit: ' + str(path))
    with Image.open(path) as image:
        if image.format != 'PNG' or image.mode != 'RGBA' or (expected_size is not None and image.size != expected_size):
            raise ValueError('PNG must preserve full RGBA geometry: ' + str(path))
        image.verify()
    with Image.open(path) as image:
        image.load()  # Force inflate, in addition to PNG structure/CRC verification.


def check_manifest(root, manifest, role, version):
    if manifest.get('version') != 2 or manifest.get('id') != role or manifest.get('pack_version') != version or manifest.get('format') != 'illustrated-atlases-v1' or (manifest.get('cell_width'), manifest.get('cell_height')) != (480, 520):
        raise ValueError('Invalid full companion identity or dimensions: ' + role)
    for key in ('name', 'description', 'system_prompt', 'special_action', 'special_action2'):
        if not isinstance(manifest.get(key), str) or not manifest[key].strip():
            raise ValueError('Missing profile field: ' + key)
    for key in ('speech', 'live'):
        values = manifest.get('voice_presets', {}).get(key)
        if not isinstance(values, list) or not values or any(not isinstance(x, str) or not x.strip() for x in values):
            raise ValueError('Missing voice presets: ' + key)
    expected = {'animations': set(BASE + INTERACTIONS), 'extra_animations': set(EXTRA),
                'alternate_animations': set(ALTERNATE + (('jumping',) if role in ('e-bao', 'gugugaga') else ()))}
    for group, names in expected.items():
        if set(manifest.get(group, {})) != names:
            raise ValueError('Incomplete action set: ' + role + '/' + group)
        for action, clip in manifest[group].items():
            count, columns, fps, loop = clip['frames'], clip['columns'], clip['fps'], clip['loop']
            hold = clip.get('hold_last_seconds', 0)
            if type(count) is not int or not 1 <= count <= 128 or columns != 8 or type(fps) not in (int, float) or not math.isfinite(fps) or not 1 <= fps <= 60 or type(loop) is not bool or type(hold) not in (int, float) or not math.isfinite(hold) or not 0 <= hold <= 2 or (hold and (action != 'jumping' or loop)):
                raise ValueError('Invalid action timing: ' + action)
            check_png(safe_path(root, clip['file']), (3840, 520 * math.ceil(count / 8)))
    gaze = manifest['gaze']
    if gaze['columns'] != 4 or gaze['frames'] != 16 or gaze['interpolation'] != 'none' or gaze['order'] != 'clockwise-from-up':
        raise ValueError('Invalid gaze semantics')
    check_png(safe_path(root, gaze['file']), (1920, 2080))
    check_png(safe_path(root, manifest['preview']), (480, 520))
    check_png(safe_path(root, manifest['avatar']))
    normal_jump = manifest['animations']['jumping']
    alternate_jump = manifest['alternate_animations'].get('jumping')
    if alternate_jump and abs(normal_jump['frames'] / normal_jump['fps'] + normal_jump.get('hold_last_seconds', 0) - alternate_jump['frames'] / alternate_jump['fps'] - alternate_jump.get('hold_last_seconds', 0)) > 1e-8:
        raise ValueError('Jump variants must complete at the same time')


def collect_role(repository, defaults, destination, role, version):
    source = repository / 'assets/bundled-skins' / role if role in IDS[3:] else defaults / role
    manifest = read_json(source / 'pet.json')
    destination.mkdir()
    for name in sorted(manifest_references(manifest)):
        copy_reference(source, destination, name)
    with Image.open(repository / 'assets/avatars' / (role + '.png')) as original:
        avatar = original.convert('RGBA')
        avatar.save(destination / 'avatar.png')
        with Image.open(destination / 'avatar.png') as restored:
            if restored.tobytes() != avatar.tobytes():
                raise ValueError('Avatar conversion changed pixels')
    if role != 'gugugaga':
        (destination / 'extra').mkdir(exist_ok=True)
        with Image.open(repository / 'assets/additional' / (role + '.png')) as original:
            if original.mode != 'RGBA' or original.size != (1920, 2080):
                raise ValueError('Unexpected additional atlas geometry: ' + role)
            for row, action in enumerate(EXTRA):
                sheet = Image.new('RGBA', (3840, 520))
                cells = []
                for frame in range(8):
                    cell = original.crop((frame % 4 * 480, (row * 2 + frame // 4) * 520, (frame % 4 + 1) * 480, (row * 2 + frame // 4 + 1) * 520))
                    sheet.paste(cell, (frame * 480, 0))
                    cells.append(cell.tobytes())
                sheet.save(destination / 'extra' / (action + '.png'))
                with Image.open(destination / 'extra' / (action + '.png')) as restored:
                    for frame, pixels in enumerate(cells):
                        if restored.crop((frame * 480, 0, (frame + 1) * 480, 520)).tobytes() != pixels:
                            raise ValueError('Additional atlas changed pixels')
        (destination / 'alternate').mkdir(exist_ok=True)
        for action in ALTERNATE + (('jumping',) if role == 'e-bao' else ()):
            shutil.copyfile(repository / 'assets/companion-animations' / role / (action + '.png'), destination / 'alternate' / (action + '.png'))
        manifest['special_action2'] = {'e-bao': '比心鼓励', 'dafeiyu': '泡泡环游', 'marfu': '荧光咒语', 'xiaobei': '全息键盘'}[role]
        manifest['extra_animations'] = {action: {'file': 'extra/' + action + '.png', 'columns': 8, 'frames': 8, 'fps': 8, 'loop': False} for action in EXTRA}
        manifest['alternate_animations'] = {action: {'file': 'alternate/' + action + '.png', 'columns': 8, 'frames': 8, 'fps': 8, 'loop': action in ('lifted', 'sleeping')} for action in ALTERNATE + (('jumping',) if role == 'e-bao' else ())}
    manifest.update(id=role, pack_version=version, avatar='avatar.png')
    if role in IDS[:3]:
        manifest['special_action'] = {'e-bao': '傲娇扭头', 'dafeiyu': '摇尾欢呼', 'marfu': '挥杖流光'}[role]
        for action in ALTERNATE[:6]:
            manifest['animations'][action]['fps'] = 8
        manifest['animations']['jumping']['fps'] = 1 / .13
        manifest['animations']['jumping']['hold_last_seconds'] = .35 if role == 'e-bao' else .05
    if not manifest.get('voice_presets'):
        manifest['voice_presets'] = {'speech': ['苏打', 'ash'] if role == 'e-bao' else ['茉莉', 'coral'] if role == 'marfu' else ['冰糖', 'nova'], 'live': ['Puck'] if role == 'e-bao' else ['Kore'] if role == 'marfu' else ['Aoede']}
    # Final reviewed overlays win over legacy export counts and 8-FPS defaults.
    # Retain each manifest's established path and all unrelated clip metadata.
    for group, action, sheet in (('animations', 'idle', 'idle'), ('animations', 'sleeping', 'sleeping'), ('alternate_animations', 'sleeping', 'sleeping-alternate')):
        clip = manifest[group][action]
        original = repository / 'assets/companion-quiet' / role / (sheet + '.png')
        target = safe_path(destination, clip['file'])
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(original, target)
        if target.read_bytes() != original.read_bytes():
            raise ValueError('Quiet overlay changed source bytes')
        clip.update(columns=8, frames=8, fps=4 if action == 'idle' else 2.5, loop=True)
    write_json(destination / 'pet.json', manifest)
    identity = {'schema_version': 1, 'id': role, 'name': manifest['name'], 'version': version, 'min_app_version': version}
    write_json(destination / 'companions.json', identity)
    references = manifest_references(manifest) | {'companions.json', 'holidays/catalog.json'}
    entries = collect_holidays(repository, destination, role, references)
    check_manifest(destination, manifest, role, version)
    return identity, entries, references


def collect_holidays(repository, destination, role, references):
    holiday_source = repository / 'assets/holidays'
    catalog = read_json(holiday_source / 'catalog.json')
    entries = [entry for entry in catalog['outfits'] if entry['character_id'] == role]
    if not entries or any(entry.get('ready') is not True for entry in entries) or len({x['outfit_id'] for x in entries}) != len(entries):
        raise ValueError('Every published outfit must be complete and ready: ' + role)
    for entry in entries:
        definition = read_json(safe_path(holiday_source, entry['definition']))
        for field in ('character_id', 'outfit_id', 'holiday_id', 'name', 'version'):
            if definition.get(field) != entry[field]:
                raise ValueError('Outfit does not match catalog')
        names = {entry['definition'], definition['preview']}
        for clip in definition['clips'] + [definition['gaze'], definition['easter_egg']]:
            names.add(clip['path'])
            check_png(safe_path(holiday_source, clip['path']), (clip['columns'] * clip['frame_width'], math.ceil(clip['frame_count'] / clip['columns']) * clip['frame_height']))
        check_png(safe_path(holiday_source, definition['preview']), (480, 520))
        for name in sorted(names):
            copy_reference(holiday_source, destination / 'holidays', name)
            references.add('holidays/' + name)
    write_json(destination / 'holidays/catalog.json', {'schema_version': catalog['schema_version'], 'outfits': entries})
    return entries


def collect_reviewed_role(repository, reviewed, destination, role, version):
    source = safe_path(reviewed, role)
    manifest = read_json(source / 'pet.json')
    if manifest.get('id') != role:
        raise ValueError('Reviewed role identity mismatch')
    references = manifest_references(manifest) | {'companions.json', 'holidays/catalog.json'}
    for name in sorted(manifest_references(manifest)):
        copy_reference(source, destination, name)
    manifest.update(id=role, pack_version=version)
    write_json(destination / 'pet.json', manifest)
    identity = {'schema_version': 1, 'id': role, 'name': manifest['name'], 'version': version, 'min_app_version': version}
    write_json(destination / 'companions.json', identity)
    entries = collect_holidays(repository, destination, role, references)
    check_manifest(destination, manifest, role, version)
    return identity, entries, references


def write_archive(source, references, path):
    files = {file.relative_to(source).as_posix() for file in source.rglob('*') if file.is_file()}
    if files != set(references) or len(files) > POLICY['maximum_entries']:
        raise ValueError('Package contains missing or unreferenced files')
    paths = [(name, safe_path(source, name)) for name in sorted(files)]
    unpacked = sum(file.stat().st_size for _, file in paths)
    if unpacked > POLICY['maximum_expanded_bytes']:
        raise ValueError('Official package exceeds unpacked size limit')
    with zipfile.ZipFile(path, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, file in paths:
            entry = zipfile.ZipInfo(name, (2026, 10, 22, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            entry.create_system = 3
            archive.writestr(entry, file.read_bytes(), compresslevel=6)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError('ZIP verification failed')
        for name, file in paths:
            if archive.read(name) != file.read_bytes():
                raise ValueError('ZIP changed source bytes: ' + name)
    return unpacked


def build(repository, output, version, defaults=None, complete_materials=None):
    repository = Path(repository).resolve()
    output = create_output(repository, output, version)
    reviewed = Path(complete_materials).resolve() if complete_materials else (repository / 'assets/complete-defaults' if defaults is None and (repository / 'assets/complete-defaults').is_dir() else None)
    if reviewed is not None:
        defaults = reviewed
    elif defaults is None:
        defaults = output / 'defaults'
        subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(repository / 'Build.ps1'), '-Target', 'SkinPackExport', '-OutputDirectory', str(output / 'export-tool'), '-TestArguments', str(defaults)], cwd=repository, check=True, timeout=300)
    else:
        defaults = Path(defaults).resolve()
    materials, artifacts = output / 'materials', output / 'artifacts'
    materials.mkdir()
    artifacts.mkdir()
    registry = read_json(repository / 'assets/official/companion-versions.json')
    if registry.get('schema_version') != 1 or registry.get('payload_algorithm') != 'official-payload-v1' or set(registry['companions']) != set(IDS):
        raise ValueError('Invalid official material registry')
    verify_registry_history(repository, registry)
    descriptors, measurements = [], []
    for role in IDS:
        source = materials / role
        record = registry['companions'][role]
        material_version = record['pack_version']
        identity, outfits, references = (collect_reviewed_role if reviewed is not None else collect_role)(repository, defaults, source, role, material_version)
        actual_payload = payload_digest(source, references)
        verify_material_record(record, actual_payload)
        identity['min_app_version'] = record['min_app_version']
        write_json(source / 'companions.json', identity)
        name = package_name(role, material_version)
        path = artifacts / name
        unpacked = write_archive(source, references, path)
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        descriptors.append({'id': role, 'name': identity['name'], 'version': material_version, 'min_app_version': identity['min_app_version'], 'asset_name': name, 'size': path.stat().st_size, 'sha256': digest, 'outfits': [{'outfit_id': entry['outfit_id'], 'version': entry['version']} for entry in outfits]})
        measurements.append({'id': role, 'release_version': version, 'pack_version': material_version, 'min_app_version': identity['min_app_version'], 'payload_sha256': actual_payload, 'entries': len(references), 'zip_bytes': path.stat().st_size, 'unpacked_bytes': unpacked, 'sha256': digest})
    write_json(artifacts / ('DeskPet-Companions-' + version + '.json'), {'schema_version': 1, 'companions': descriptors})
    write_json(output / 'measurements.json', {'status': 'authored-native-validation-required', 'roles': measurements})
    return measurements


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, help='New owned output directory; must not already exist')
    parser.add_argument('--version')
    parser.add_argument('--check-material-history', action='store_true')
    parser.add_argument('--defaults', type=Path, help='Explicit pixel-verified SkinPackExport output; otherwise build it now')
    parser.add_argument('--complete-materials', type=Path, help='Reviewed complete role folders; preserve default PNG bytes')
    args = parser.parse_args()
    if args.check_material_history:
        verify_registry_history(args.repository, read_json(args.repository / 'assets/official/companion-versions.json'))
        print('Official material history verified')
        return
    if args.output is None or args.version is None:
        parser.error('--output and --version are required for authoring')
    print(json.dumps(build(args.repository, args.output, args.version, args.defaults, args.complete_materials), indent=2))


if __name__ == '__main__':
    main()
