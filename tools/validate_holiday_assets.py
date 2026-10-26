"""Validate packaged holiday art and produce review contact sheets. Never generates art."""
import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
from PIL import Image, ImageDraw, UnidentifiedImageError

ROOT=Path(__file__).resolve().parents[1]
CHARACTERS=('e-bao','dafeiyu','marfu','xiaobei','gugugaga')
BASE=('idle','running-right','running-left','waving','jumping','failed','waiting','running','review')
INTERACTIONS=('lifted','landing','petting','poked','feeding','happy','sleeping','special')
EXTRA=('stumble','special2')
ALTERNATE=('lifted','landing','feeding','happy','sleeping','special','special2','stumble')

def required_clips(character):
    return {(a,'default') for a in BASE+INTERACTIONS+EXTRA}|{(a,'alternate') for a in ALTERNATE+ (('jumping',) if character in ('e-bao','gugugaga') else ())}

def asset_path(root,value):
    if not isinstance(value,str) or not value or len(value)>240 or '\\' in value or ':' in value or any(p in ('','..','.') for p in value.split('/')) or PurePosixPath(value).is_absolute():
        raise ValueError('invalid asset path: '+str(value))
    root=Path(root).resolve(); path=root.joinpath(*value.split('/')).resolve()
    if not path.is_relative_to(root):raise ValueError('asset path escapes root')
    return path

def read_json(path):
    if path.stat().st_size>1024*1024:raise ValueError('JSON exceeds 1 MiB')
    def unique(pairs):
        result={}
        for key,value in pairs:
            if key in result:raise ValueError('duplicate JSON field: '+key)
            result[key]=value
        return result
    return json.loads(path.read_text(encoding='utf-8-sig'),object_pairs_hook=unique)

def check_png(path,width,height,columns,count):
    expected=(width*columns,height*math.ceil(count/columns))
    try:
        if path.stat().st_size>32*1024*1024:raise ValueError('PNG exceeds 32 MiB')
        with Image.open(path) as probe:
            if probe.format!='PNG':raise ValueError('not PNG')
            if probe.size!=expected:raise ValueError(f'wrong dimensions: {probe.size}, expected {expected}')
            if probe.mode!='RGBA':raise ValueError('PNG must be RGBA with transparent background')
            probe.verify()
        with Image.open(path) as image:
            image.load();alpha=image.getchannel('A')
            for i in range(columns*math.ceil(count/columns)):
                frame=alpha.crop(((i%columns)*width,(i//columns)*height,(i%columns+1)*width,(i//columns+1)*height))
                low,high=frame.getextrema()
                if i<count:
                    if high<=16:raise ValueError(f'empty frame {i}')
                    if low!=0:raise ValueError(f'frame {i} has no transparent background')
                elif high!=0:raise ValueError(f'unused padding frame {i} is not transparent')
    except (OSError,UnidentifiedImageError,SyntaxError) as error:raise ValueError('invalid PNG: '+str(error)) from error
    return count

def source_semantics(character,action,variant):
    if character in CHARACTERS and action in ('idle','sleeping'):
        return 8,4.0 if action=='idle' else 2.5,True
    if character in ('xiaobei','gugugaga'):
        manifest=read_json(ROOT/'assets/bundled-skins'/character/'pet.json')
        group='alternate_animations' if variant=='alternate' else ('extra_animations' if action in EXTRA else 'animations')
        item=manifest.get(group,{}).get(action)
        if item:return item['frames'],float(item['fps']),item['loop']
    if variant=='alternate' or action in EXTRA:return 8,8.0,action in ('lifted','sleeping')
    if action in INTERACTIONS:return 4,8.0,action in ('lifted','sleeping')
    index=BASE.index(action)
    period=.22 if action in ('running-left','running-right') else (.17,.11,.11,.16,.13,.16,.19,.18,.18)[index]
    return (6,8,8,4,5,8,6,6,6)[index],1/period,action!='jumping'

def validate_clip(root,clip,expected=None,gaze=False,egg=False):
    count=clip['frame_count'];columns=clip['columns'];fps=clip['fps'];loop=clip['loop']
    if type(count)!=int or type(columns)!=int or not 1<=count<=16 or columns!=(4 if gaze else 8):raise ValueError('invalid frame count/columns')
    if type(loop)!=bool or type(fps) not in (int,float) or not math.isfinite(fps) or not 0<fps<=60:raise ValueError('invalid FPS/loop')
    if clip['frame_width']!=480 or clip['frame_height']!=520:raise ValueError('cell must be 480x520')
    if gaze and count!=16:raise ValueError('gaze requires sixteen directions')
    if egg and (count!=8 or fps!=4 or loop):raise ValueError('egg must be eight frames, 4 FPS, non-looping')
    if expected and (count!=expected[0] or abs(fps-expected[1])>1e-5 or loop!=expected[2]):raise ValueError(f'changes source animation semantics: expected {expected}')
    for key,limit in (('offset_x',480),('offset_y',520)):
        value=clip.get(key,0)
        if type(value) not in (int,float) or not math.isfinite(value) or abs(value)>limit:raise ValueError('invalid frame alignment')
    path=asset_path(root,clip['path']);check_png(path,480,520,columns,count)
    return path,count

def contact_sheet(root,definition,destination):
    destination.mkdir(parents=True,exist_ok=True)
    for clip in definition['clips']+[dict(definition['gaze'],action='gaze',variant='default'),dict(definition['easter_egg'],action='easter-egg',variant='default')]:
        sheet=Image.new('RGB',(8*144,2*180),(245,247,250));draw=ImageDraw.Draw(sheet)
        with Image.open(asset_path(root,clip['path'])) as atlas:
            for i in range(clip['frame_count']):
                row=i//8;col=i%8
                # Gaze uses two frame rows; ordinary sheets show identical frames on both backgrounds.
                for background in ((row,) if clip['frame_count']>8 else (0,1)):
                    x=col*144;y=background*180
                    draw.rectangle((x,y,x+143,y+179),fill=(245,247,250) if background==0 else (25,30,43))
                    cell=atlas.crop(((i%clip['columns'])*480,(i//clip['columns'])*520,(i%clip['columns']+1)*480,(i//clip['columns']+1)*520)).resize((132,143),Image.Resampling.LANCZOS)
                    sheet.paste(cell,(x+6,y+25),cell)
                    draw.text((x+5,y+5),str(i),fill=(90,100,110) if background==0 else (220,225,235))
        sheet.save(destination/(clip['variant']+'-'+clip['action']+'.png'))

def validate(root,contacts=None):
    root=Path(root);report={'schema_version':1,'success':False,'ready_characters':0,'ready_outfits':0,'characters':[],'errors':[]}
    try:
        index=read_json(root/'catalog.json')
        if index.get('schema_version')!=1 or not isinstance(index.get('outfits'),list):raise ValueError('invalid catalog schema')
        if len(index['outfits'])!=50:raise ValueError('complete release requires fifty holiday outfits')
        reviewed={x['path']:x['sha256'] for x in read_json(ROOT/'assets/official/reviewed-outfit-definitions.json')['files']}
        ready=set()
        seen=set()
        for entry in index['outfits']:
            character=entry.get('character_id');item={'character':character,'errors':[],'frames':0,'png_files':0,'compressed_bytes':0,'files':{}};report['characters'].append(item)
            try:
                key=(character,entry.get('outfit_id'))
                if character not in CHARACTERS or key in seen:raise ValueError('unknown character or duplicate outfit')
                seen.add(key)
                item['holiday']=entry.get('holiday_id')
                if entry.get('ready') is not True:raise ValueError('outfit is not marked ready')
                if entry.get('holiday_id')=='halloween' and entry.get('greetings_ready') is not True:raise ValueError('Halloween greeting library must stay ready')
                definition_path=asset_path(root,entry['definition'])
                if reviewed.get(entry['definition'])!=hashlib.sha256(definition_path.read_bytes()).hexdigest():raise ValueError('reviewed motion metadata changed')
                definition=read_json(definition_path)
                if definition.get('schema_version')!=1 or any(definition.get(k)!=entry.get(k) for k in ('character_id','outfit_id','holiday_id','name','version')):raise ValueError('definition identity mismatch')
                if not definition.get('action_name'):raise ValueError('missing Easter egg action name')
                keys=[(x['action'],x.get('variant','default')) for x in definition['clips']]
                if len(keys)!=len(set(keys)) or set(keys)!=required_clips(character):raise ValueError('incomplete or duplicate action/variant matrix')
                files=[]
                for clip in definition['clips']:
                    if clip['frame_count']!=8 or clip['columns']!=8:raise ValueError('reviewed motion needs all eight drawn poses')
                    # Timing/loop belong to each reviewed role, not the old default animation profile.
                    path,count=validate_clip(root,clip);files.append(path);item['frames']+=count
                for key in ('gaze','easter_egg'):
                    path,count=validate_clip(root,definition[key],gaze=key=='gaze',egg=key=='easter_egg');files.append(path);item['frames']+=count
                preview=asset_path(root,definition['preview']);check_png(preview,480,520,1,1);files.append(preview);item['frames']+=1
                if len(set(files))!=len(files):raise ValueError('each required resource needs its own PNG')
                for path in files:
                    data=path.read_bytes();item['files'][path.relative_to(root.resolve()).as_posix()]={'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)};item['compressed_bytes']+=len(data)
                item['png_files']=len(files);report['ready_outfits']+=1;ready.add(character);report['ready_characters']=len(ready)
                if contacts:contact_sheet(root,definition,Path(contacts)/entry['holiday_id']/character)
            except (KeyError,ValueError,OSError,TypeError) as error:item['errors'].append(str(error));report['errors'].append(str(character)+': '+str(error))
        greetings=read_json(root/'halloween/greetings.json')
        roles=greetings.get('characters',[])
        if greetings.get('schema_version')!=1 or greetings.get('holiday_id')!='halloween' or len(roles)!=5 or {x['character_id'] for x in roles}!=set(CHARACTERS):raise ValueError('invalid five-character greeting library')
        if any(len(x['phrases'])!=6 or len(set(x['phrases']))!=6 or any(not isinstance(t,str) or not t.strip() or len(t)>80 or '\n' in t for t in x['phrases']) for x in roles):raise ValueError('greeting library requires six distinct bounded lines per character')
    except (KeyError,ValueError,OSError,TypeError) as error:report['errors'].append(str(error))
    report['success']=not report['errors'] and report['ready_characters']==5 and report['ready_outfits']==50
    return report

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--root',type=Path,default=ROOT/'assets/holidays');parser.add_argument('--contact-sheet',type=Path);parser.add_argument('--report-json',type=Path);args=parser.parse_args()
    report=validate(args.root,args.contact_sheet)
    if args.report_json:args.report_json.parent.mkdir(parents=True,exist_ok=True);args.report_json.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Holiday assets:',report['ready_characters'],'/ 5 roles,',report['ready_outfits'],'/ 50 outfits ready')
    for error in report['errors']:print('ERROR:',error)
    return 0 if report['success'] else 1

if __name__=='__main__':raise SystemExit(main())
