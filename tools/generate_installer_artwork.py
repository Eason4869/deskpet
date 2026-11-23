"""Deterministic native installer decoration; Pillow is a regeneration tool only.

Portrait inputs are the five official package preview.png files normalized in
Task 3 and copied verbatim to installer/assets/{id}.png. The logo remains the
application's assets/deskpet-logo.png. The success badge follows the approved
inline SVG geometry. All output is committed: building/installing needs no
Python, browser, SVG engine, network, or scratch files.
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]/'installer/assets'
S=3

def panel(name):
    size=(360*S,582*S)
    bg=Image.new('RGBA',size)
    pixels=bg.load()
    for y in range(size[1]):
        for x in range(size[0]):
            t=(x*.423+y*.906)/(size[0]*.423+size[1]*.906)
            a,b,blend=((234,241,255),(245,248,255),t/.64) if t<.64 else ((245,248,255),(237,244,255),(t-.64)/.36)
            pixels[x,y]=tuple(round(i+(j-i)*blend) for i,j in zip(a,b))+(255,)
    draw=ImageDraw.Draw(bg)
    draw.ellipse((-83*S,99*S,327*S,509*S),outline='#dce7fb',width=S)
    overlay=Image.new('RGBA',size)
    ImageDraw.Draw(overlay).ellipse((120*S,150*S,370*S,400*S),fill=(255,255,255,105))
    bg=Image.alpha_composite(bg,overlay)
    portrait=Image.open(ROOT/(name+'.png')).convert('RGBA').resize((339*S,367*S),Image.Resampling.LANCZOS)
    bg.alpha_composite(portrait,(10*S,85*S))
    bg.resize((720,1164),Image.Resampling.LANCZOS).save(ROOT/(name+'-panel.png'))

def surface(name,w,h,fill,outline):
    for focus in (False,True):
        if focus and name in ('dot-active','path-field'):continue
        if name in ('dot','dot-active') and not focus:
            width=23 if name=='dot-active' else 7
            im=Image.new('RGBA',(width*S,7*S))
            ImageDraw.Draw(im).rounded_rectangle((0,0,width*S-1,7*S-1),radius=round(3.5*S),fill='#4568ed' if name=='dot-active' else '#c4d1e9')
            im.save(ROOT/(name+'.png'));continue
        bg='#edf4ff' if name.startswith('dot') else 'white'
        im=Image.new('RGBA',(w*S,h*S),bg);d=ImageDraw.Draw(im)
        d.rounded_rectangle((S,S,w*S-S,h*S-S),radius=8*S,fill=fill,
            outline='#829cff' if focus else outline,width=(2 if focus else 1)*S)
        if name.startswith('dot'):
            width=23 if name=='dot-active' else 7;left=(w-width)/2
            d.rounded_rectangle((round(left*S),round(9.5*S),round((left+width)*S),round(16.5*S)),radius=4*S,
                fill='#4568ed' if name=='dot-active' else '#c4d1e9')
        im.save(ROOT/(name+('-focus' if focus else '')+'.png'))

def badge():
    im=Image.new('RGBA',(64*S,64*S));d=ImageDraw.Draw(im)
    d.ellipse((2*S,6*S,56*S,60*S),fill='#F0F5FF')
    gradient=Image.new('RGBA',im.size);g=ImageDraw.Draw(gradient)
    for y in range(64*S):
        t=max(0,min(1,(y/S-10)/45))
        color=tuple(round(a+(b-a)*t) for a,b in zip((121,173,255),(69,104,237)))+(255,)
        g.line((0,y,64*S,y),fill=color)
    mask=Image.new('L',im.size);ImageDraw.Draw(mask).ellipse((8*S,10*S,50*S,52*S),fill=255)
    im.paste(gradient,(0,0),mask);d=ImageDraw.Draw(im)
    d.arc((11*S,13*S,47*S,49*S),220,300,fill='#BBD5FF',width=round(1.5*S))
    points=[(19.5,31),(26,37.5),(39,24.5)]
    d.line([(round(x*S),round(y*S)) for x,y in points],fill='white',width=4*S,joint='curve')
    for x,y in points:d.ellipse((round((x-2)*S),round((y-2)*S),round((x+2)*S),round((y+2)*S)),fill='white')
    d.polygon([(54*S,5*S),(56*S,10*S),(61*S,12*S),(56*S,14*S),(54*S,19*S),(52*S,14*S),(47*S,12*S),(52*S,10*S)],fill='#7AA4FA')
    d.ellipse((round(5.5*S),round(50.5*S),round(10.5*S),round(55.5*S)),fill='#A5C2FF')
    im.save(ROOT/'success.png')

def spinner():
    for frame in range(12):
        im=Image.new('RGBA',(19*S,19*S),'white');d=ImageDraw.Draw(im)
        d.ellipse((1,1,19*S-2,19*S-2),fill='white',outline='#dbe2ed',width=S)
        d.arc((2,2,19*S-3,19*S-3),230+frame*30,350+frame*30,fill='#4568ed',width=round(1.7*S))
        im.save(ROOT/('stage-spinner-'+str(frame)+'.png'))

if __name__=='__main__':
    spinner()
    for role in ('e-bao','dafeiyu','marfu','xiaobei','gugugaga'):panel(role)
    for row in [('button-primary',150,46,'#4568ed','#4568ed'),('button-secondary',130,45,'white','#ccd7eb'),
        ('button-link',110,30,'white','white'),('dot',26,26,'#edf4ff','#edf4ff'),('dot-active',26,26,'#edf4ff','#edf4ff')]:surface(*row)
    surface('path-field',420,45,'#fafcff','#c9d4ea')
    im=Image.new('RGBA',(560*S,45*S),'white');d=ImageDraw.Draw(im)
    d.rounded_rectangle((0,0,560*S-1,45*S-1),radius=9*S,fill='#f3f6fc')
    d.ellipse((15*S,14*S,31*S,30*S),outline='#8294b4',width=S)
    d.line((23*S,20*S,23*S,26*S),fill='#8294b4',width=S);d.ellipse((22*S,17*S,24*S,19*S),fill='#8294b4')
    im.save(ROOT/'info-box.png')
    for name,color in [('progress-track','#eaf0fc')]:
        im=Image.new('RGBA',(560*S,9*S),'white');ImageDraw.Draw(im).rounded_rectangle((0,0,560*S-1,9*S-1),radius=round(4.5*S),fill=color);im.save(ROOT/(name+'.png'))
    Image.new('RGB',(558,1),'#e3e9f4').save(ROOT/'divider.png')
    for index in range(3):
        im=Image.new('RGBA',(19*S,19*S),'white');d=ImageDraw.Draw(im)
        d.ellipse((1,1,19*S-2,19*S-2),fill='#edf7f2' if index==0 else 'white',outline='#edf7f2' if index==0 else '#dbe2ed',width=S)
        if index==0:d.line([(round(5.3*S),round(9.3*S)),(round(8.3*S),round(12.3*S)),(round(13.7*S),round(6.7*S))],fill='#5a9d84',width=round(1.3*S),joint='curve')
        if index==1:d.arc((2,2,19*S-3,19*S-3),230,350,fill='#4568ed',width=round(1.7*S))
        im.save(ROOT/('stage-'+str(index)+'.png'))
    badge()
