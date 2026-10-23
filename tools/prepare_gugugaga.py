"""Pack imagegen poses with the bundled Pets connected-component extractor.

No synthesized frames, mirroring or per-pose resizing. Source rows must have
empty gutters; component groups, rather than equal cell crops, define poses.
"""
import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = Path.home() / '.codex/plugins/cache/openai-curated-remote/work-pets/0.1.6/skills/create-pet/scripts'


def extractor():
    spec = importlib.util.spec_from_file_location('pets_extract', SCRIPTS / 'extract_strip_frames.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def pack_sheet(source, output, columns=4, rows=2, frames=8, baseline=500, target_height=468):
    source, output = Path(source), Path(output)
    image = Image.open(source).convert('RGBA')
    if columns * rows != frames:
        raise ValueError('sheet dimensions / frame count mismatch')
    width, height = image.width, (image.height + rows - 1) // rows
    edges = [round(row * image.height / rows) for row in range(rows + 1)]
    # Reflow whole rows only after proving the row boundary cuts no artwork.
    alpha = image.getchannel('A').point(lambda a: 255 if a > 16 else 0)
    for boundary in range(1, rows):
        nominal = edges[boundary]
        candidates = sorted(range(max(edges[boundary - 1] + 2, nominal - height // 5),
            min(image.height - 1, nominal + height // 5)), key=lambda y: abs(y - nominal))
        empty = next((y for y in candidates if not alpha.crop((0, y - 1, width, y + 1)).getbbox()), None)
        if empty is None:
            raise ValueError('pose touches row boundary')
        edges[boundary] = empty
    height = max(edges[row + 1] - edges[row] for row in range(rows))
    # Generated sheets may place their second row a few pixels higher.
    # Register ROWS by their lowest observed ground contact, preserving all
    # within-row airborne displacement and using a single final scale.
    grounds = [alpha.crop((0, edges[row], width, edges[row + 1])).getbbox()[3] for row in range(rows)]
    shifts = [max(grounds) - ground for ground in grounds]
    strip = Image.new('RGBA', (width * rows, height + max(shifts)))
    for row in range(rows):
        strip.paste(image.crop((0, edges[row], width, edges[row + 1])), (row * width, shifts[row]))
    pets = extractor()
    groups = pets.component_frame_groups(strip, frames)
    if not groups or len(groups) != frames:
        raise ValueError('expected complete connected poses')
    boxes = [pets.component_bounds(group) for group in groups]
    slot = width / columns
    for index, box in enumerate(boxes):
        row_start = index // columns * width
        if box[0] <= row_start or box[2] >= row_start + width or box[1] <= 0 or box[3] >= strip.height:
            raise ValueError('pose touches sheet boundary; regenerate with gutters')
    # The upstream fitter never upscales. Build all registered viewports at
    # native resolution first, then apply ONE shared production transform.
    pets.CELL_WIDTH, pets.CELL_HEIGHT = round(slot) + 16, strip.height + 16
    poses = pets.fit_registered_component_groups(strip, groups)
    boxes = [pose.getchannel('A').getbbox() for pose in poses]
    envelope = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    scale = min(432 / (envelope[2] - envelope[0]), target_height / (envelope[3] - envelope[1]))
    dx = 240 - (envelope[0] + envelope[2]) / 2 * scale
    dy = baseline - envelope[3] * scale
    atlas = Image.new('RGBA', (480 * frames, 520))
    hashes, output_bounds = [], []
    for index, pose in enumerate(poses):
        registered = pose.transform((480, 520), Image.Transform.AFFINE,
            (1 / scale, 0, -dx / scale, 0, 1 / scale, -dy / scale), Image.Resampling.BICUBIC)
        # Clear hidden RGB without interpreting black outlines as background.
        clean = Image.new('RGBA', registered.size)
        clean.paste(registered, mask=registered.getchannel('A').point(lambda a: 255 if a else 0))
        hashes.append(hashlib.sha256(clean.tobytes()).hexdigest())
        output_bounds.append(clean.getchannel('A').getbbox())
        atlas.paste(clean, (index * 480, 0))
    if len(set(hashes)) != frames:
        raise ValueError('all poses must be distinct; duplicated frames rejected')
    output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output)
    report = {'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
        'frames': frames, 'method': 'Pets 0.1.6 component groups / shared transform',
        'scale': scale, 'dx': dx, 'dy': dy, 'target_height': target_height, 'row_edges': edges, 'row_shifts': shifts, 'output_bounds': output_bounds,
        'asset_sha256': hashlib.sha256(output.read_bytes()).hexdigest()}
    output.with_suffix('.registration.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return report


STANDARD = ['idle', 'running-right', 'running-left', 'waving', 'jumping', 'failed', 'waiting', 'running', 'review',
            'lifted', 'landing', 'petting', 'poked', 'feeding', 'happy', 'sleeping', 'special']
PAIRED = ['lifted', 'landing', 'feeding', 'happy', 'sleeping', 'special', 'special2', 'stumble', 'jumping']


def inspect_pack(root):
    """Run the upstream Pets frame inspector with the DeskPet contract."""
    spec = importlib.util.spec_from_file_location('pets_inspect', SCRIPTS / 'inspect_frames.py')
    inspector = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(inspector)
    inspector.CELL_WIDTH, inspector.CELL_HEIGHT = 480, 520
    inspector.ROW_FRAME_COUNTS = {a: 16 if a == 'gaze' else 8 for a in STANDARD + ['special2', 'stumble'] + [a + '-alt' for a in PAIRED] + ['gaze']}
    old_argv = sys.argv
    try:
        sys.argv = ['inspect_frames', '--frames-root', str(root / 'frames'), '--json-out', str(root / 'pets-inspection.json'), '--states', 'all', '--require-components']
        inspector.main()
    finally:
        sys.argv = old_argv


def build_assets():
    run = ROOT / 'work/gugugaga-art'
    bundle = ROOT / 'assets/bundled-skins/gugugaga'
    extras = bundle / 'variants'
    qa = ROOT / 'previews/gugugaga-production'
    for path in (bundle / 'animations', extras, qa):
        path.mkdir(parents=True, exist_ok=True)
    prompts = {j['id']: j['prompt'] for j in json.loads((run / 'all-prompts.json').read_text(encoding='utf-8'))}
    records, clips = [], {}
    actions = STANDARD + ['special2', 'stumble'] + [a + '-alt' for a in PAIRED] + ['gaze']
    manifest_rows = []
    for action in actions:
        source_id = {'lifted': 'dragged', 'poked': 'poked-repair'}.get(action, action)
        source = run / 'raw' / (source_id + '-v1.png')
        packed = run / 'packed' / (action + '.png')
        report = pack_sheet(source, packed, rows=4 if action == 'gaze' else 2, frames=16 if action == 'gaze' else 8,
            target_height=340 if action.startswith('sleeping') else 468)
        strip = Image.open(packed).convert('RGBA')
        poses = [strip.crop((i * 480, 0, (i + 1) * 480, 520)) for i in range(report['frames'])]
        if action == 'gaze':
            gaze = Image.new('RGBA', (1920, 2080))
            for i, pose in enumerate(poses): gaze.paste(pose, (i % 4 * 480, i // 4 * 520))
            gaze.save(bundle / 'gaze.png')
            destination = bundle / 'gaze.png'
        elif action.endswith('-alt'):
            destination = extras / (action[:-4] + '.png')
            strip.save(destination)
        elif action in ('stumble', 'special2'):
            destination = extras / (action + '-original.png')
            strip.save(destination)
        else:
            destination = bundle / 'animations' / (action + '.png')
            strip.save(destination)
            fps = {'idle': 4, 'running-right': 6, 'running-left': 6, 'failed': 5, 'waiting': 4, 'running': 5, 'review': 5, 'petting': 5}.get(action, 8)
            clips[action] = {'file': 'animations/' + action + '.png', 'columns': 8, 'frames': 8, 'fps': fps,
                'loop': action in ('idle', 'running-right', 'running-left', 'waiting', 'running', 'review', 'sleeping', 'lifted')}
        frame_dir = run / 'frames' / action
        frame_dir.mkdir(parents=True, exist_ok=True)
        for i, pose in enumerate(poses): pose.save(frame_dir / f'{i:02d}.png')
        manifest_rows.append({'state': action, 'method': 'components'})
        report.update({'action': action, 'prompt': prompts[source_id], 'asset': str(destination.relative_to(ROOT)),
            'asset_sha256': hashlib.sha256(destination.read_bytes()).hexdigest(), 'source': str(source.relative_to(ROOT))})
        records.append(report)
        # Review at the actual desktop size, on both light and dark backgrounds.
        contact = Image.new('RGB', (192 * len(poses), 464))
        for row, background in enumerate(('#eceff3', '#20242c')):
            for i, pose in enumerate(poses):
                tile = Image.new('RGBA', (192, 232), background)
                tile.alpha_composite(pose.resize((192, 208), Image.Resampling.LANCZOS))
                ImageDraw.Draw(tile).text((5, 212), str(i), fill='black' if row == 0 else 'white')
                contact.paste(tile.convert('RGB'), (192 * i, 232 * row))
        contact.save(qa / (action + '-contact.jpg'), quality=90)
        if action != 'gaze':
            views = []
            for pose in poses:
                view = Image.new('RGBA', (384, 208), '#eceff3')
                view.paste(Image.new('RGBA', (192, 208), '#20242c'), (192, 0))
                small = pose.resize((192, 208), Image.Resampling.LANCZOS)
                view.alpha_composite(small); view.alpha_composite(small, (192, 0)); views.append(view.convert('RGB'))
            fps = clips.get(action, {}).get('fps', 8)
            views[0].save(qa / (action + '.gif'), save_all=True, append_images=views[1:], duration=round(1000 / fps), loop=0, disposal=2, optimize=False)
    (run / 'frames/frames-manifest.json').write_text(json.dumps({'rows': manifest_rows}), encoding='utf-8')
    # The shipped manifest owns the persona; asset rebuilding must not depend on historical plans.
    prompt = json.loads((bundle / 'pet.json').read_text(encoding='utf-8-sig'))['system_prompt']
    manifest = {'version': 2, 'name': '咕嘎', 'description': '呆萌好奇 · 贪吃小企鹅', 'format': 'illustrated-atlases-v1',
        'cell_width': 480, 'cell_height': 520, 'preview': 'preview.png', 'special_action': '咕嘎摇摇', 'system_prompt': prompt,
        'animations': {a: clips[a] for a in STANDARD}, 'gaze': {'file': 'gaze.png', 'columns': 4, 'frames': 16, 'interpolation': 'none', 'order': 'clockwise-from-up'}}
    manifest.update({'id': 'gugugaga', 'pack_version': '0.9.3', 'avatar': 'avatar.png', 'special_action2': '转圈摆鳍',
        'extra_animations': {a: {'file': 'variants/' + a + '-original.png', 'columns': 8, 'frames': 8, 'fps': 8, 'loop': False} for a in ('stumble', 'special2')},
        'alternate_animations': {a: {'file': 'variants/' + a + '.png', 'columns': 8, 'frames': 8, 'fps': 8, 'loop': a in ('lifted', 'sleeping')} for a in PAIRED},
        'voice_presets': {'speech': ['冰糖', 'shimmer', 'nova'], 'live': ['Leda', 'Zephyr'],
            'description': '自然清亮，语速适中；开心时轻快，认真时清楚，不刻意幼声。', 'sample_text': '你好，我是咕嘎。今天也一起慢慢来。咕咕嘎嘎。'}})
    (bundle / 'pet.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    Image.open(bundle / 'animations/idle.png').crop((0, 0, 480, 520)).save(bundle / 'preview.png')
    Image.open(ROOT / 'previews/gugugaga-v1/avatar.png').convert('RGBA').resize((512, 512), Image.Resampling.LANCZOS).save(ROOT / 'assets/avatars/gugugaga.png')
    Image.open(ROOT / 'assets/avatars/gugugaga.png').save(bundle / 'avatar.png')
    (bundle / 'provenance.json').write_text(json.dumps({'generator': 'built-in imagegen', 'poses': 240, 'records': records}, ensure_ascii=False, indent=2), encoding='utf-8')
    inspect_pack(run)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, nargs='?')
    parser.add_argument('output', type=Path, nargs='?')
    parser.add_argument('--build', action='store_true')
    parser.add_argument('--columns', type=int, default=4)
    parser.add_argument('--rows', type=int, default=2)
    args = parser.parse_args()
    if args.build:
        build_assets()
    elif args.source and args.output:
        print(json.dumps(pack_sheet(args.source, args.output, args.columns, args.rows, args.columns * args.rows), indent=2))
    else:
        parser.error('supply source and output or --build')
