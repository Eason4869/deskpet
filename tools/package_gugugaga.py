"""Create and verify the reviewable Guga HD skin archive from production assets."""
import hashlib
import json
import re
from pathlib import Path
import zipfile
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]


def package(output_directory=None, release_version=None):
    bundle = ROOT / 'assets/bundled-skins/gugugaga'
    extras = bundle / 'variants'
    manifest = json.loads((bundle / 'pet.json').read_text(encoding='utf-8'))
    version = release_version or manifest['pack_version']
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-dev\.[1-9][0-9]*)?', version):
        raise ValueError('Skin pack needs X.Y.Z or X.Y.Z-dev.N')
    files = {Path('pet.json'): bundle / 'pet.json', Path('provenance.json'): bundle / 'provenance.json',
             Path('avatar.png'): bundle / 'avatar.png'}
    for spec in list(manifest['animations'].values()) + [manifest['gaze']]:
        files[Path(spec['file'])] = bundle / spec['file']
    files[Path(manifest['preview'])] = bundle / manifest['preview']
    for source in extras.glob('*.png'): files[Path('variants') / source.name] = source
    pairs = {}
    for action in ['lifted', 'landing', 'feeding', 'happy', 'sleeping', 'special', 'special2', 'stumble', 'jumping']:
        pairs[action] = {'first': manifest['animations'][action]['file'] if action in manifest['animations'] else f'variants/{action}-original.png',
            'second': f'variants/{action}.png', 'frames': 8, 'fps': 8, 'loop': action in ('lifted', 'sleeping')}
    metadata = json.dumps({'version': 1, 'purpose': 'Human-readable pair index; runtime declarations are in pet.json', 'pairs': pairs}, ensure_ascii=False, indent=2)
    readme = f'''咕嘎 · gugugaga HD Skin {manifest['pack_version']}
配套 DeskPet 发行标识：{version}

1. 新版 DeskPet：在设置的“导入 / 更新角色包”中直接选择此 ZIP。同包 ID gugugaga 自动更新，只显示一个伙伴。
   旧版应用：先解压，再从外观导入选择根目录 pet.json；仅支持标准外观，需要一次升级引擎才能启用全部字段。
2. 标准 v2 包包含 17 类动作各 8 帧、16 方向视线，单帧 480×520。
3. avatar.png 是角色包自己的应用头像，pet.json 同时声明普通聊天人格、音色候选和试听台词。
4. variants/ 及 variants.json 完整保留 9 类双套动作素材：提起、落地、喂食、开心、睡觉、摇摆、转圈、踉跄、顶肚跳。每套均为 8 帧。
5. 新版从 pet.json 的 extra_animations / alternate_animations 加载双套扩展，无需按角色编写代码。variants.json 仅便于人工浏览。
   后续发布同 id 的新素材包即可更新，已有个人人格、音色及聊天记录优先保留。安装包内置同角色时，已安装资源包优先，不重复显示。
6. 共 240 个有效姿态、31 张 PNG（含头像和预览）。身体转圈与鼠标视线是不同素材。
7. 普通聊天人格为呆萌、好奇、贪吃、有点小脾气的小企鹅；不覆盖工作模式提示词。未附音轨，不预设付费音色。
8. 本包是待评审的首版二创素材，不属于游戏官方资源。精确生成提示与素材校验值见包内 provenance.json；资源格式见仓库 docs/character-packs.md。
'''
    output = Path(output_directory or ROOT / 'work/delivery') / f'gugugaga-HD-Skin-{version}.zip'
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for relative, source in sorted(files.items()): archive.write(source, relative.as_posix())
        archive.writestr('variants.json', metadata.encode('utf-8'))
        archive.writestr('README.txt', readme.encode('utf-8'))
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() is not None: raise ValueError('archive CRC mismatch')
        if len([p for p in archive.namelist() if p.endswith('.png')]) != 31: raise ValueError('expected 31 PNG assets')
        for relative, source in files.items():
            if archive.read(relative.as_posix()) != source.read_bytes(): raise ValueError('archive bytes changed')
            if source.suffix == '.png':
                with archive.open(relative.as_posix()) as stream:
                    image = Image.open(stream); image.load()
                    if image.mode != 'RGBA': raise ValueError('PNG requires RGBA: ' + str(relative))
        for spec in list(manifest['animations'].values()) + [manifest['gaze']]:
            im = Image.open(bundle / spec['file'])
            if im.size != (spec['columns'] * 480, ((spec['frames'] + spec['columns'] - 1) // spec['columns']) * 520): raise ValueError('manifest dimensions mismatch')
    report = {'file': str(output.relative_to(ROOT) if output.is_relative_to(ROOT) else output), 'bytes': output.stat().st_size,
        'sha256': hashlib.sha256(output.read_bytes()).hexdigest(), 'png': 31, 'poses': 240, 'crc_and_bytes_verified': True}
    output.with_suffix('.verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return output


if __name__ == '__main__':
    package()
