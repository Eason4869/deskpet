"""Validate the standalone updater's managed payload without counting locales as artwork."""
import hashlib


def expected_locale_resources(directory):
    resources = {}
    for path in directory.glob('*.json'):
        raw = path.read_bytes()
        resources['Dafeiyu.Locales.' + path.name] = (len(raw), hashlib.sha256(raw).hexdigest())
    return resources


def validate_helper_resources(file_size, manifest, expected):
    if type(file_size) is not int or file_size < 0 or not expected:
        raise ValueError('Invalid helper size or empty expected locale contract')
    actual = {}
    for row in manifest:
        name = row.get('name')
        if not isinstance(name, str) or name in actual:
            raise ValueError('Missing or duplicate helper resource name')
        length = row.get('length')
        if type(length) is not int or length < 0:
            raise ValueError('Invalid helper resource length: ' + name)
        actual[name] = (length, row.get('sha256'))
    if actual != expected:
        raise ValueError('Helper must embed exactly the reviewed locale bytes and no artwork or other managed resources')
    overhead = file_size - sum(length for length, _ in actual.values())
    if not 0 <= overhead < 1024 * 1024:
        raise ValueError('Helper code, PE metadata and unexplained bytes must remain below 1 MiB after subtracting verified locales')
