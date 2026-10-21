"""Prepare, validate, sign and package DeskPet on macOS. Never publishes a Release."""
from __future__ import annotations

from release_assets import version_key
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import stat
import struct
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
ARCH_CPUS = {0x01000007: 'x86_64', 0x0100000C: 'arm64'}
REQUIRED_NATIVE = ['DeskPet', 'libcoreclr.dylib', 'libAvaloniaNative.dylib', 'libDeskPetMac.dylib']
BUNDLE_ID = 'com.eason4869.deskpet'


class PackagingError(RuntimeError):
    pass


def version_number() -> str:
    text = (ROOT / 'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')
    return re.search(r'DisplayVersion="([^"]+)"', text).group(1)


def macho_architectures(path: Path) -> set[str]:
    with path.open('rb') as source:
        header = source.read(4096)
    if len(header) < 8:
        return set()
    magic = header[:4]
    if magic in (b'\xcf\xfa\xed\xfe', b'\xce\xfa\xed\xfe'):
        return {ARCH_CPUS.get(struct.unpack_from('<I', header, 4)[0], 'unsupported')}
    if magic in (b'\xfe\xed\xfa\xcf', b'\xfe\xed\xfa\xce'):
        return {ARCH_CPUS.get(struct.unpack_from('>I', header, 4)[0], 'unsupported')}
    if magic in (b'\xca\xfe\xba\xbe', b'\xca\xfe\xba\xbf', b'\xbe\xba\xfe\xca', b'\xbf\xba\xfe\xca'):
        endian = '>' if magic[0] == 0xCA else '<'
        count = struct.unpack_from(endian + 'I', header, 4)[0]
        stride = 32 if magic in (b'\xca\xfe\xba\xbf', b'\xbf\xba\xfe\xca') else 20
        if count > 32 or len(header) < 8 + count * stride:
            raise PackagingError(f'Invalid Mach-O architecture table: {path.name}')
        return {ARCH_CPUS.get(struct.unpack_from(endian + 'I', header, 8 + i * stride)[0], 'unsupported') for i in range(count)}
    return set()


def release_requirements(environment) -> tuple[str, str]:
    identity = environment.get('MACOS_SIGN_IDENTITY', '').strip()
    profile = environment.get('MACOS_NOTARY_PROFILE', '').strip()
    if not re.fullmatch(r'Developer ID Application: .+ \([A-Z0-9]{10}\)', identity) or not profile:
        raise PackagingError('Release requires a Developer ID Application identity and MACOS_NOTARY_PROFILE; preview is never a release fallback.')
    return identity, profile


def checked_files(directory: Path):
    for path in sorted(directory.rglob('*')):
        if path.is_symlink():
            raise PackagingError(f'Unexpected symbolic link in bundle inputs: {path.name}')
        if path.is_file():
            relative = path.relative_to(directory)
            if not stat.S_ISREG(path.stat().st_mode):
                raise PackagingError(f'Unsupported file: {relative}')
            if any(part.lower() in {'data', '.git', 'work', '.superpowers', '__pycache__'} for part in relative.parts) or path.name.lower() == 'settings.json' or path.name.startswith('.env') or path.suffix.lower() in {'.p12', '.pfx', '.pem', '.key'}:
                raise PackagingError(f'Potential private data in publish output: {relative}')
            yield path


def validate_publish(publish: Path, arch: str):
    if arch not in ARCH_CPUS.values():
        raise PackagingError('Unsupported architecture')
    files = list(checked_files(publish))
    for name in REQUIRED_NATIVE + ['DeskPet.dll', 'DeskPet.deps.json', 'DeskPet.runtimeconfig.json']:
        if not (publish / name).is_file():
            raise PackagingError(f'Missing published file: {name}')
    runtime = json.loads((publish / 'DeskPet.runtimeconfig.json').read_text(encoding='utf-8-sig')).get('runtimeOptions', {})
    if runtime.get('framework') or runtime.get('frameworks') or not runtime.get('includedFrameworks'):
        raise PackagingError('A self-contained .NET publish is required')
    native = {}
    for path in files:
        architectures = macho_architectures(path)
        if path.suffix == '.dylib' and not architectures:
            raise PackagingError(f'Native library has no valid Mach-O header: {path.name}')
        if architectures:
            if arch not in architectures:
                raise PackagingError(f'Wrong architecture in {path.name}: expected {arch}, got {sorted(architectures)}')
            native[path.relative_to(publish).as_posix()] = sorted(architectures)
    if 'DeskPet' not in native:
        raise PackagingError('Application executable has no Mach-O architecture')
    return native


def create_bundle(publish: Path, app: Path, arch: str, version: str, mode: str, icon: Path, *, commit='unknown'):
    publish, app = Path(publish).resolve(), Path(app).resolve()
    if app == publish or publish in app.parents:
        raise PackagingError('Bundle destination cannot be inside publish input')
    if app.exists():
        raise PackagingError(f'Bundle output already exists: {app}')
    if mode not in {'preview', 'release'}:
        raise PackagingError('Invalid mode or version')
    version_key(version)
    numeric_version = '.'.join(str(n) for n in version_key(version)[:3])
    native = validate_publish(publish, arch)
    icon_bytes = Path(icon).read_bytes()
    if len(icon_bytes) < 8 or icon_bytes[:4] != b'icns':
        raise PackagingError('A valid ICNS icon is required')
    contents = app / 'Contents'
    shutil.copytree(publish, contents / 'MacOS', ignore=shutil.ignore_patterns('*.pdb'))
    resources = contents / 'Resources'
    resources.mkdir()
    shutil.copy2(icon, resources / 'DeskPet.icns')
    for name in native:
        (contents / 'MacOS' / name).chmod(0o755)
    info = {
        'CFBundleIdentifier': BUNDLE_ID, 'CFBundleName': 'DeskPet',
        'CFBundleDisplayName': '桌面伙伴（预览）' if mode == 'preview' else '桌面伙伴',
        'CFBundleExecutable': 'DeskPet', 'CFBundleIconFile': 'DeskPet.icns',
        'CFBundleInfoDictionaryVersion': '6.0', 'CFBundlePackageType': 'APPL',
        'CFBundleVersion': numeric_version, 'CFBundleShortVersionString': numeric_version,
        'LSMinimumSystemVersion': '14.0', 'LSApplicationCategoryType': 'public.app-category.utilities',
        'LSUIElement': True, 'NSHighResolutionCapable': True,
        'NSMicrophoneUsageDescription': '仅在你主动开始听写或实时语音时使用麦克风；音频会发送给你配置的语音服务。',
    }
    (contents / 'Info.plist').write_bytes(plistlib.dumps(info, sort_keys=True))
    (contents / 'PkgInfo').write_bytes(b'APPL????')
    (resources / 'build-info.json').write_text(json.dumps({'version': version, 'architecture': arch, 'mode': mode, 'commit': commit}, indent=2), encoding='utf-8')
    return validate_bundle(app, arch, version, mode)


def validate_bundle(app: Path, arch: str, version: str, mode: str):
    contents = Path(app) / 'Contents'
    list(checked_files(contents))
    info = plistlib.loads((contents / 'Info.plist').read_bytes())
    numeric_version='.'.join(str(n) for n in version_key(version)[:3])
    if info.get('CFBundleVersion') != numeric_version or info.get('CFBundleShortVersionString') != numeric_version:
        raise PackagingError('Bundle version does not match requested version')
    required = {'CFBundleIdentifier': BUNDLE_ID, 'CFBundleExecutable': 'DeskPet', 'LSMinimumSystemVersion': '14.0', 'CFBundlePackageType': 'APPL'}
    if any(info.get(k) != v for k, v in required.items()) or not info.get('NSMicrophoneUsageDescription') or not info.get('NSHighResolutionCapable'):
        raise PackagingError('Bundle metadata is incomplete or changed')
    if mode == 'preview' and '预览' not in info.get('CFBundleDisplayName', ''):
        raise PackagingError('Unsigned preview must be visibly labelled')
    if not (contents / 'Resources/DeskPet.icns').is_file():
        raise PackagingError('Bundle icon is missing')
    metadata = json.loads((contents / 'Resources/build-info.json').read_text(encoding='utf-8'))
    if metadata.get('mode') != mode or metadata.get('version') != version or metadata.get('architecture') != arch:
        raise PackagingError('Build metadata is inconsistent')
    return {'version': version, 'architecture': arch, 'mode': mode, 'native_binaries': validate_publish(contents / 'MacOS', arch)}


def run(arguments, **kwargs):
    print('Running:', ' '.join(map(str, arguments)), flush=True)
    return subprocess.run([str(x) for x in arguments], check=True, **kwargs)


def create_disk_image(source: Path, destination: Path, mode: str):
    arguments = ['hdiutil', 'create', '-volname', 'DeskPet Preview' if mode == 'preview' else 'DeskPet',
                 '-srcfolder', source, '-format', 'UDZO', '-ov', destination]
    for attempt in range(3):
        try:
            response = run(arguments, capture_output=True, text=True, timeout=300)
            if response.stdout:
                print(response.stdout, end='', flush=True)
            return
        except subprocess.CalledProcessError as error:
            details = (error.stdout or '') + (error.stderr or '')
            print(details, file=sys.stderr, flush=True)
            if attempt == 2 or not re.search(r'Resource busy|Resource temporarily unavailable', details, re.IGNORECASE):
                raise
            delay = 2 ** (attempt + 1)
            print(f'hdiutil create is temporarily busy; retry {attempt + 2}/3 in {delay}s', flush=True)
            time.sleep(delay)


def create_icon(output: Path):
    iconset = output / 'DeskPet.iconset'
    iconset.mkdir()
    for size in [16, 32, 128, 256, 512]:
        for multiplier in [1, 2]:
            pixels = size * multiplier
            name = f'icon_{size}x{size}' + ('@2x' if multiplier == 2 else '') + '.png'
            run(['sips', '-s', 'format', 'png', '-z', pixels, pixels, ROOT / 'assets/deskpet-logo.png', '--out', iconset / name], stdout=subprocess.DEVNULL)
    icon = output / 'DeskPet.icns'
    run(['iconutil', '-c', 'icns', iconset, '-o', icon])
    return icon


def sign_bundle(app: Path, arch: str, version: str, mode: str):
    identity = release_requirements(os.environ)[0] if mode == 'release' else '-'
    report = validate_bundle(app, arch, version, mode)
    # codesign treats .dll files in Contents/MacOS as nested code as well.
    # Sign every payload file before apphost; ditto preserves the generic
    # file signatures in extended attributes when producing DMG and ZIP.
    payload = app / 'Contents/MacOS'
    files = [path.relative_to(payload).as_posix() for path in checked_files(payload)]
    for name in sorted(files, key=lambda n: (n == 'DeskPet', -n.count('/'), n)):
        args = ['codesign', '--force', '--sign', identity]
        if mode == 'release':
            args += ['--timestamp']
            if name in report['native_binaries']:
                args += ['--options', 'runtime']
        if name == 'DeskPet':
            args += ['--entitlements', ROOT / 'installer/macos/DeskPet.entitlements']
        run(args + [app / 'Contents/MacOS' / name])
    # Ad-hoc signatures have no Team ID. Hardened library validation would
    # reject the separately signed .NET runtime before managed startup.
    args = ['codesign', '--force', '--sign', identity, '--entitlements', ROOT / 'installer/macos/DeskPet.entitlements']
    if mode == 'release':
        args += ['--timestamp', '--options', 'runtime']
    run(args + [app])
    run(['codesign', '--verify', '--deep', '--strict', '--verbose=2', app])


def notarize(path: Path, profile: str, report: Path):
    # The profile name is not a password; credentials stay inside the Keychain.
    response = subprocess.run(['xcrun', 'notarytool', 'submit', str(path), '--keychain-profile', profile, '--wait', '--output-format', 'json'], capture_output=True, text=True, timeout=1800)
    try:
        data = json.loads(response.stdout)
    except json.JSONDecodeError:
        raise PackagingError('notarytool returned no valid JSON; inspect the local Apple tool log') from None
    report.write_text(json.dumps(data, indent=2), encoding='utf-8')
    if response.returncode != 0 or data.get('status') != 'Accepted':
        raise PackagingError(f'Notarization was not Accepted (submission {data.get("id", "unknown")}); see {report.name}')


def verify_release_evidence(path: Path, version: str):
    evidence = json.loads(path.read_text(encoding='utf-8'))
    if evidence.get('version') != version:
        raise PackagingError('Release evidence version mismatch')
    requirements = ['native_tests', 'ui_smoke', 'microphone_permissions', 'audio_and_live', 'multi_display', 'login_items', 'upgrade_preserves_data']
    for arch in ['arm64', 'x86_64']:
        checks = evidence.get('architectures', {}).get(arch, {})
        for name in requirements:
            check = checks.get(name, {})
            if check.get('passed') is not True or not isinstance(check.get('evidence'), str) or not check['evidence'].strip():
                raise PackagingError(f'Release is blocked by missing verified evidence: {arch}/{name}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish-dir', type=Path, required=True)
    parser.add_argument('--output-directory', type=Path, required=True)
    parser.add_argument('--arch', choices=['arm64', 'x86_64'], required=True)
    parser.add_argument('--mode', choices=['preview', 'release'], default='preview')
    parser.add_argument('--icon', type=Path)
    parser.add_argument('--prepare-only', action='store_true', help='Cross-platform layout check only; produces no distributable archive')
    parser.add_argument('--release-evidence', type=Path)
    parser.add_argument('--commit', default='unknown')
    args = parser.parse_args()
    version = version_number()
    if args.mode == 'release':
        release_requirements(os.environ)
        if not args.release_evidence:
            raise PackagingError('Release requires verified evidence for both architectures')
        verify_release_evidence(args.release_evidence, version)
    if not args.prepare_only and sys.platform != 'darwin':
        raise PackagingError('Signing, archives and DMG validation require macOS')
    output = args.output_directory.resolve()
    if output.exists() and any(output.iterdir()):
        raise PackagingError('Output directory must be empty; existing artifacts are preserved')
    output.mkdir(parents=True, exist_ok=True)
    icon = args.icon or create_icon(output)
    app = output / 'DeskPet.app'
    report = create_bundle(args.publish_dir, app, args.arch, version, args.mode, icon, commit=args.commit)
    if args.prepare_only:
        report['validation'] = 'layout-only; not signed, executed or distributable'
        (output / 'layout-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report, indent=2))
        return
    sign_bundle(app, args.arch, version, args.mode)
    profile = os.environ.get('MACOS_NOTARY_PROFILE', '')
    if args.mode == 'release':
        notary_zip = output / 'notary-input.zip'
        run(['ditto', '-c', '-k', '--keepParent', app, notary_zip])
        notarize(notary_zip, profile, output / 'app-notarization.json')
        run(['xcrun', 'stapler', 'staple', app])
        run(['xcrun', 'stapler', 'validate', app])
        run(['spctl', '--assess', '--type', 'execute', '--verbose=4', app])
        notary_zip.unlink()
    suffix = '-preview' if args.mode == 'preview' else ''
    stem = f'DeskPet-macOS-{version}-{args.arch}{suffix}'
    archive = output / (stem + '.zip')
    run(['ditto', '-c', '-k', '--keepParent', app, archive])
    zip_root = output / 'extracted-zip'
    zip_root.mkdir()
    run(['ditto', '-x', '-k', archive, zip_root])
    validate_bundle(zip_root / 'DeskPet.app', args.arch, version, args.mode)
    run(['codesign', '--verify', '--deep', '--strict', zip_root / 'DeskPet.app'])
    image_root = output / 'disk-image'
    image_root.mkdir()
    run(['ditto', app, image_root / 'DeskPet.app'])
    (image_root / 'Applications').symlink_to('/Applications', target_is_directory=True)
    (image_root / '安装说明.txt').write_text('将 DeskPet.app 拖入 Applications，再从应用程序打开。\n' + ('这是未经 Apple Developer ID 签名或公证的预览，尚未正式发布。\n' if args.mode == 'preview' else ''), encoding='utf-8')
    dmg = output / (stem + '.dmg')
    create_disk_image(image_root, dmg, args.mode)
    if args.mode == 'release':
        identity, _ = release_requirements(os.environ)
        run(['codesign', '--sign', identity, '--timestamp', dmg])
        notarize(dmg, profile, output / 'dmg-notarization.json')
        run(['xcrun', 'stapler', 'staple', dmg])
        run(['xcrun', 'stapler', 'validate', dmg])
    run(['hdiutil', 'verify', dmg])
    mount = output / 'mounted-image'
    mount.mkdir()
    run(['hdiutil', 'attach', '-readonly', '-nobrowse', '-mountpoint', mount, dmg])
    try:
        validate_bundle(mount / 'DeskPet.app', args.arch, version, args.mode)
        if not (mount / 'Applications').is_symlink() or os.readlink(mount / 'Applications') != '/Applications':
            raise PackagingError('Disk image Applications link is invalid')
        run(['codesign', '--verify', '--deep', '--strict', mount / 'DeskPet.app'])
    finally:
        run(['hdiutil', 'detach', mount])
    report.update({'signature': 'Developer ID' if args.mode == 'release' else 'ad-hoc (not Developer ID)',
                   'archives_verified': ['zip', 'dmg'],
                   'notarized': args.mode == 'release', 'runtime_verified': False,
                   'artifacts': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [archive, dmg]}})
    (output / 'SHA256SUMS.txt').write_text(''.join(f'{digest}  {name}\n' for name, digest in report['artifacts'].items()), encoding='ascii')
    (output / 'build-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (PackagingError, subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError, ValueError) as error:
        print(f'Packaging failed: {error}', file=sys.stderr)
        sys.exit(1)
