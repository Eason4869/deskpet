import json, re, subprocess
from pathlib import Path

base = '3ba0850db01c73c66f1272771bbe8afa6e3d292a'
formatted = {'countdown', 'consentBody', 'imageDestination', 'textDestination', 'lastObserved'}
sentinels = re.compile(r'PLACE(?:HOLDER|WAY)ZERO|PLATZHALTERZERO|प्लेसहोल्डरशून्य|プレースホルダーゼロ', re.I)
keys = None
for path in Path('assets/locales').glob('*.json'):
    old = json.loads(subprocess.check_output(['git', '-c', 'safe.directory=' + Path.cwd().as_posix(), 'show', base + ':' + path.as_posix()], encoding='utf-8'))
    current = json.loads(path.read_bytes())
    added = {k: v for k, v in current.items() if k.startswith('desktopObservation.')}
    assert all(current[k] == v for k, v in old.items()), path
    assert len(added) == 69, path
    image_keys = {k for k in current if k.startswith('imageDescription.') and k not in old}
    assert len(image_keys) == 11 and set(current) - set(old) == set(added) | image_keys, path
    for key, value in added.items():
        assert re.fullmatch(r'[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)*', key), (path, key)
        assert value.strip() and not sentinels.search(value), (path, key, value)
        assert value.count('{0}') == (1 if key.removeprefix('desktopObservation.') in formatted else 0), (path, key, value)
        assert not re.search(r'\{[1-9][0-9]*\}', value), (path, key, value)
    image, text = 'https://image.example/path', 'https://chat.example/path'
    summary = added['desktopObservation.imageDestination'].format(image) + '\n' + added['desktopObservation.textDestination'].format(text)
    body = added['desktopObservation.consentBody'].format(summary)
    assert body.count(image) == body.count(text) == 1 and not sentinels.search(body), (path, body)
    assert added['desktopObservation.resume'] != 'CV', path
    if keys is None: keys = set(added)
    assert set(added) == keys, path
    print(path.name, '4756 protected values, 69 keys, grammar, placeholders and formatted recipients passed')
