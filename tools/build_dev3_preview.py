"""Offline, read-only asset inspector. No app unlock state is touched."""
from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[1]
roles={'e-bao':'E包','dafeiyu':'大肥鱼','marfu':'玛尔芙','xiaobei':'小贝','gugugaga':'咕嘎'}
names={'default':'默认','halloween':'万圣节','christmas':'圣诞节','spring-festival':'春节','lantern':'元宵节','valentines-day':'情人节','qingming':'清明节','dragon-boat':'端午节','mid-autumn':'中秋节','national-day':'国庆节','qixi':'七夕'}
items=[]
for role,title in roles.items():
 for holiday,name in names.items():
  folder=ROOT/('assets/complete-defaults/'+role if holiday=='default' else 'assets/holidays/'+holiday+'/'+role)
  source=json.loads((folder/('pet.json' if holiday=='default' else 'outfit.json')).read_text(encoding='utf-8-sig'))
  clips=[]
  for section,variant in [('animations','default'),('extra_animations','default'),('alternate_animations','alternate')]:
   for action,clip in source.get(section,{}).items():
    clips.append(dict(action=action,variant=variant,url=(folder/clip['file']).as_uri(),frames=clip['frames'],columns=clip['columns'],fps=clip['fps'],loop=clip['loop']))
  # Outfit descriptors use explicit clips rather than the root manifest dictionaries.
  if not clips:
   for clip in source.get('clips',[]):
    clips.append(dict(action=clip['action'],variant=clip.get('variant','default'),url=(ROOT/'assets/holidays'/clip['path']).as_uri(),frames=clip['frame_count'],columns=clip['columns'],fps=clip['fps'],loop=clip['loop']))
  card=ROOT/('work/festive-scenes/native-cards/'+holiday+'-'+role+'-light.png')
  items.append(dict(role=role,roleName=title,holiday=holiday,name=name,clips=clips,card=card.as_uri() if card.exists() else None))
html="""<!doctype html><meta charset="utf-8"><title>0.11.0-dev.3 角色与节日预览</title>
<style>body{font:16px system-ui;background:#f5f2eb;color:#233449;margin:24px}main{max-width:1180px;margin:auto}h1{font-size:27px}nav{display:flex;gap:10px;flex-wrap:wrap}select,button,label{font:inherit;padding:8px 12px}section{display:flex;gap:30px;flex-wrap:wrap;margin-top:20px}canvas{width:480px;max-width:100%;height:auto;background:repeating-conic-gradient(#e6e8eb 0% 25%,#fff 0% 50%) 0/32px 32px;border:1px solid #bac6d0}aside{flex:1;min-width:300px}img{width:100%;max-width:640px;border-radius:12px}p{line-height:1.6}small{color:#56687b}</style>
<main><h1>角色与节日预览 · 0.11.0-dev.3</h1><p>五位角色 × 默认与十个节日。按图集原始帧率播放，可逐帧核对肢体、脚底基准线与动作收尾。下方卡片为原生 WPF 导出。</p>
<nav><select id="role"></select><select id="holiday"></select><select id="action"></select><button id="play">暂停</button><button id="replay">重播</button><button id="step">下一帧</button><label><input type="checkbox" id="grid" checked>网格参考线</label></nav>
<section><div><canvas width="480" height="520" id="canvas"></canvas><p id="status"></p></div><aside><img id="card"><p id="cardStatus"></p><small>此页读取本地创作资源，应用中的导入、日期换装和命中仍以原生验收为准。</small></aside></section></main>
<script>const DATA=__DATA__;const roles=__ROLES__,holidays=__HOLIDAYS__;const $=id=>document.getElementById(id);let current,clip,img=new Image(),frame=0,elapsed=0,last=performance.now(),playing=true,revision=0,loaded=false;
for(const [v,t] of Object.entries(roles))$('role').add(new Option(t,v));for(const [v,t] of Object.entries(holidays))$('holiday').add(new Option(t,v));
function choose(){current=DATA.find(x=>x.role===$('role').value&&x.holiday===$('holiday').value);const desired=clip?clip.action:'idle',variant=clip?clip.variant:'default';$('action').replaceChildren();for(let i=0;i<current.clips.length;i++){const c=current.clips[i];$('action').add(new Option(c.action+(c.variant==='alternate'?' · 另一动作':''),String(i)))}let selected=current.clips.findIndex(c=>c.action===desired&&c.variant===variant);if(selected<0)selected=current.clips.findIndex(c=>c.action==='idle'&&c.variant==='default');$('action').value=String(Math.max(0,selected));$('card').hidden=!current.card;if(current.card)$('card').src=current.card;$('cardStatus').textContent=current.card?current.roleName+' · '+current.name+' · 原生卡片导出':'默认皮肤使用好感度纪念卡片。';load()}
function load(){clip=current.clips[+$('action').value];frame=elapsed=0;loaded=false;playing=true;$('play').textContent='暂停';const token=++revision;img=new Image();img.onload=()=>{if(token===revision)loaded=true};img.src=clip.url}
function draw(){const ctx=$('canvas').getContext('2d');ctx.clearRect(0,0,480,520);if(loaded)ctx.drawImage(img,(frame%clip.columns)*480,Math.floor(frame/clip.columns)*520,480,520,0,0,480,520);if($('grid').checked){ctx.strokeStyle='#45739355';ctx.lineWidth=1;ctx.beginPath();for(let x=0;x<=480;x+=40){ctx.moveTo(x,0);ctx.lineTo(x,520)}for(let y=0;y<=520;y+=40){ctx.moveTo(0,y);ctx.lineTo(480,y)}ctx.stroke();ctx.strokeStyle='#d94d3c';ctx.beginPath();ctx.moveTo(240,0);ctx.lineTo(240,520);ctx.moveTo(0,480);ctx.lineTo(480,480);ctx.stroke()}$('status').textContent=(frame+1)+' / '+clip.frames+' 帧 · '+clip.fps+' fps · '+(clip.loop?'循环':'单次，最后一帧停留')}
function tick(now){let dt=Math.min(.1,(now-last)/1000);last=now;if(playing&&loaded){elapsed+=dt;let raw=Math.floor(elapsed*clip.fps);frame=clip.loop?raw%clip.frames:Math.min(raw,clip.frames-1);if(!clip.loop&&raw>=clip.frames){playing=false;$('play').textContent='播放'}}draw();requestAnimationFrame(tick)}
$('role').onchange=$('holiday').onchange=choose;$('action').onchange=load;$('play').onclick=()=>{playing=!playing;$('play').textContent=playing?'暂停':'播放'};$('replay').onclick=()=>{elapsed=frame=0;playing=true;$('play').textContent='暂停'};$('step').onclick=()=>{playing=false;$('play').textContent='播放';frame=(frame+1)%clip.frames;elapsed=frame/clip.fps};choose();requestAnimationFrame(tick);</script>"""
html=html.replace('__DATA__',json.dumps(items,ensure_ascii=False)).replace('__ROLES__',json.dumps(roles,ensure_ascii=False)).replace('__HOLIDAYS__',json.dumps(names,ensure_ascii=False))
assert len(items)==55 and all(x['clips'] for x in items)
out=ROOT/'work/dev3-preview.html';out.parent.mkdir(exist_ok=True);out.write_text(html,encoding='utf-8');print(out)
