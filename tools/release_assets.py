"""Windows release filename contract shared by packaging and its smoke tests."""
import re


def version_key(version: str) -> tuple:
    if not isinstance(version, str) or len(version) > 64:
        raise ValueError('Invalid release version')
    value = version[1:] if version.startswith(('v', 'V')) else version
    match = re.fullmatch(r'([0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)(?:-(dev)(?:\.([1-9][0-9]*))?)?', value)
    if not match:
        raise ValueError('Invalid release version')
    parts = tuple(int(part) for part in match[1].split('.'))
    if any(part > 65534 for part in parts) or (match[2] and len(parts) != 3):
        raise ValueError('Invalid release version')
    sequence = int(match[3] or 0)
    if sequence > 2147483647:
        raise ValueError('Invalid release version')
    return parts + (0,) * (4-len(parts)) + (0 if match[2] else 1, sequence)


def release_metadata(version: str, tag: str, branch: str) -> dict:
    key = version_key(version)
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-dev\.[1-9][0-9]*)?', version):
        raise ValueError('New releases require X.Y.Z or X.Y.Z-dev.N')
    preview = key[4] == 0
    if tag != 'v'+version or branch != ('dev' if preview else 'main'):
        raise ValueError('Tag, branch and release channel disagree')
    return dict(version=version, tag=tag, branch=branch, prerelease=preview,
                make_latest='false' if preview else 'true')


def installer_asset_name(version: str) -> str:
    value = version[1:] if version.startswith(('v', 'V')) else version
    parts = version_key(version)[:4]
    prefix = 'DeskPet-Windows-Setup-' if parts >= (0, 6, 0) else 'DeskPet-Setup-'
    return prefix + value + '.exe'
