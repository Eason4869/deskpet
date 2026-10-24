"""Build/validate title-only font assets. Asset preparation only; no runtime dependency.

pip install --target work/f4a/python fonttools==4.62.1 uharfbuzz==0.53.0
$env:PYTHONPATH='work/f4a/python'
python tools/fonts/relationship_title_fonts.py --build --only SC  # first native proof
python tools/fonts/relationship_title_fonts.py --build           # complete matrix
python tools/fonts/relationship_title_fonts.py --check           # offline CI check
"""
import argparse
import hashlib
import io
import json
import pathlib
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'assets/fonts/relationships'
CACHE = ROOT / 'work/f4a/upstream'
CJK = 'f8d157532fbfaeda587e826d4cd5b21a49186f7c'
GF = 'e7db6ca603fd4862010383ff17ffd6cc4ffac1c0'
LOCALES = ['zh_CN','zh_TW','en_US','ja_JP','ko_KR','fr_FR','de_DE','hi_IN','id_ID','it_IT','pt_BR','es_419','es_ES']
KEYS = ['relationship.stage.'+s for s in ['first_meeting','familiar','close','trusted','heartfelt','best_friend']] + ['relationship.card.title','relationship.card.halloweenTitle']
REGIONS = {'zh_CN':'SC','zh_TW':'TC','ja_JP':'JP','ko_KR':'KR','hi_IN':'Devanagari'}
FACES = {
    'SC': ('notofonts/noto-cjk',CJK,'Serif/OTF/SimplifiedChinese/NotoSerifCJKsc-Regular.otf','Serif/LICENSE'),
    'TC': ('notofonts/noto-cjk',CJK,'Serif/OTF/TraditionalChinese/NotoSerifCJKtc-Regular.otf','Serif/LICENSE'),
    'JP': ('notofonts/noto-cjk',CJK,'Serif/OTF/Japanese/NotoSerifCJKjp-Regular.otf','Serif/LICENSE'),
    'KR': ('notofonts/noto-cjk',CJK,'Serif/OTF/Korean/NotoSerifCJKkr-Regular.otf','Serif/LICENSE'),
    'Latin': ('google/fonts',GF,'ofl/notoserif/NotoSerif[wdth,wght].ttf','ofl/notoserif/OFL.txt'),
    'Devanagari': ('google/fonts',GF,'ofl/notoserifdevanagari/NotoSerifDevanagari[wdth,wght].ttf','ofl/notoserifdevanagari/OFL.txt'),
}
SOURCE_HASHES = {
    'SC':'2a2eae2628df83556c54018c41e20fa532c1b862c5256ae8b3f23feb918d12ca',
    'TC':'234301038e76e7c35c43113785024700c4e4fe7bdce1d1fbbc42fca7e6683798',
    'JP':'d9854c7a8ef170b5a7932558856fd64eb8de0b007cd823fed6f9f514ad2803d3',
    'KR':'77b4b741f864d27f15e90f275b17106dde90b2ad28f82bab72dc95805db5fb42',
    'Latin':'4d8e6761424656867019081a1a01336f3cb086982682698714054fc33f782713',
    'Devanagari':'1191e07bfeb062d80e252eb85b0eafdfbda1e350707a2a60628668e8f677dbbb',
}
# Native WPF Indic shaping probes require RA/virama even when neither is printed.
# Preserving these two scalars and their layout closure is essential: the title-only
# subset otherwise renders direct cmap glyphs, whereas HarfBuzz silently still shapes.
SHAPING_SUPPORT = {'Devanagari':'\u0930\u094d'}

def digest(data): return hashlib.sha256(data).hexdigest()
def corpus():
    return {loc: {key:json.loads((ROOT/('assets/locales/'+loc+'.json')).read_text(encoding='utf-8-sig'))[key] for key in KEYS} for loc in LOCALES}
def encoded(value): return (json.dumps(value,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
def download(repo,pin,path):
    url='https://raw.githubusercontent.com/'+repo+'/'+pin+'/'+urllib.parse.quote(path,safe='/')
    local=CACHE/(repo.replace('/','-')+'-'+pin+'-'+path.replace('/','_'))
    if not local.exists():
        CACHE.mkdir(parents=True,exist_ok=True)
        with urllib.request.urlopen(url,timeout=60) as response: local.write_bytes(response.read())
    return local.read_bytes(),url
def texts(data,face): return '\n'.join(text for loc,entries in data.items() if REGIONS.get(loc,'Latin')==face for text in entries.values())

def build(only):
    import fontTools
    from fontTools.ttLib import TTFont
    from fontTools import subset
    from fontTools.varLib.instancer import instantiateVariableFont
    assert fontTools.__version__=='4.62.1','Use pinned fontTools for reproducible subsets'
    data=corpus(); ASSETS.mkdir(parents=True,exist_ok=True)
    manifest_path=ASSETS/'manifest.json'
    manifest=json.loads(manifest_path.read_text()) if manifest_path.exists() else {'version':'relationship-title-serif-1','faces':{}}
    for face,(repo,pin,path,licensepath) in FACES.items():
        if only and only!=face: continue
        raw,url=download(repo,pin,path)
        assert digest(raw)==SOURCE_HASHES[face],('Pinned source hash mismatch',face)
        license_raw,license_url=download(repo,pin,licensepath)
        license_name=('OFL-CJK.txt' if face in ('SC','TC','JP','KR') else 'OFL-'+face+'.txt')
        (ASSETS/license_name).write_bytes(license_raw)
        font=TTFont(io.BytesIO(raw),recalcTimestamp=False)
        upstream_version=font['name'].getDebugName(5)
        if 'fvar' in font: font=instantiateVariableFont(font,{'wght':400,'wdth':100},inplace=True)
        options=subset.Options()
        options.layout_features=['*']; options.name_IDs=['*']; options.name_legacy=True; options.name_languages=['*']
        options.notdef_glyph=True; options.notdef_outline=True; options.recommended_glyphs=True
        cutter=subset.Subsetter(options=options)
        cutter.populate(text=texts(data,face)+' '+SHAPING_SUPPORT.get(face,''))
        cutter.subset(font)
        family='DeskPet Keepsake '+face
        # All localized family/subfamily/PostScript names are replaced, retaining copyright/OFL records.
        for record in list(font['name'].names):
            if record.nameID in (1,2,3,4,6,16,17,18,21,22,25): font['name'].names.remove(record)
        values={1:family,2:'Regular',3:'DeskPetKeepsake'+face+'-Regular-1',4:family+' Regular',6:'DeskPetKeepsake'+face+'-Regular',16:family,17:'Regular'}
        for name_id,value in values.items():
            font['name'].setName(value,name_id,3,1,0x409)
            font['name'].setName(value,name_id,1,0,0)
        if 'CFF ' in font:
            cff=font['CFF '].cff; cff.fontNames=[values[6]]
            cff.topDictIndex[0].FamilyName=family; cff.topDictIndex[0].FullName=values[4]
        ext='.otf' if 'CFF ' in font else '.ttf'
        filename='deskpet-keepsake-'+face.lower()+ext
        output=io.BytesIO(); font.save(output); final=output.getvalue()
        (ASSETS/filename).write_bytes(final)
        cmap=font.getBestCmap()
        manifest['faces'][face]={'family':family,'file':filename,'source_url':url,'source_commit':pin,'source_sha256':digest(raw),'source_bytes':len(raw),'source_version':upstream_version,'license':license_name,'license_url':license_url,'license_sha256':digest(license_raw),'sha256':digest(final),'bytes':len(final),'shaping_support':[ord(c) for c in SHAPING_SUPPORT.get(face,'')],'codepoints':sorted(cmap)}
        print(face,len(raw),'->',len(final),digest(final))
    (ASSETS/'corpus.json').write_bytes(encoded(data))
    manifest_path.write_bytes(encoded(manifest))
    if len(manifest['faces'])==6:
        (ROOT/'src/Relationships/RelationshipTitleFonts.Generated.cs').write_text(generated_text(manifest),encoding='utf-8')

def generated_text(manifest):
    lines=['// Generated by tools/fonts/relationship_title_fonts.py --build; do not edit.','namespace Dafeiyu { public static partial class RelationshipTitleFonts {','    static RelationshipTitleFontAsset[] CreateAssets() { return new [] {']
    for face in FACES:
        entry=manifest['faces'][face]
        points=','.join(str(p) for p in entry['codepoints'])
        lines.append('        new RelationshipTitleFontAsset("'+face+'", "'+entry['file']+'", "'+entry['family']+'", new int[]{'+points+'}),')
    lines+=['    }; }','} }','']
    return '\n'.join(lines)

def check():
    from fontTools.ttLib import TTFont
    import uharfbuzz as hb
    data=corpus(); previous=json.loads((ASSETS/'corpus.json').read_text(encoding='utf-8'))
    assert data==previous,'Title translations changed: regenerate title font subsets with --build, then review glyph/layout proofs'
    manifest=json.loads((ASSETS/'manifest.json').read_text(encoding='utf-8'))
    assert len(manifest['faces'])==6
    assert (ROOT/'src/Relationships/RelationshipTitleFonts.Generated.cs').read_text(encoding='utf-8')==generated_text(manifest),'Generated runtime coverage manifest is stale'
    for face,entry in manifest['faces'].items():
        raw=(ASSETS/entry['file']).read_bytes(); font=TTFont(io.BytesIO(raw))
        assert digest(raw)==entry['sha256']; assert len(raw)==entry['bytes']
        assert digest((ASSETS/entry['license']).read_bytes())==entry['license_sha256']
        assert font['name'].getDebugName(1)==entry['family']
        assert font['name'].getDebugName(0),'Original upstream copyright name record must survive'
        assert entry['source_sha256']==SOURCE_HASHES[face] and entry['source_commit']==FACES[face][1]
        assert not ('fvar' in font),'Runtime font must be static'
        cmap=font.getBestCmap(); assert sorted(cmap)==entry['codepoints']
        assert all(ord(c) in cmap for c in SHAPING_SUPPORT.get(face,'')),'Native shaping support glyphs missing'
        for char in texts(data,face):
            if char not in '\n\r': assert ord(char) in cmap,(face,char)
        if face=='Devanagari':
            assert 'GSUB' in font and 'GPOS' in font
            hf=hb.Font(hb.Face(raw)); hb.ot_font_set_funcs(hf)
            samples=[v for v in data['hi_IN'].values() if any('\u0900'<=ch<='\u097f' for ch in v)]
            assert samples,'No actual Hindi title shaping sample'
            for sample in samples:
                buf=hb.Buffer(); buf.add_str(sample); buf.guess_segment_properties(); hb.shape(hf,buf)
                assert all(g.codepoint for g in buf.glyph_infos)
                # Compare original after pin/hash verification to ensure subset preserved actual shaping.
                repo,pin,path=FACES[face][:3]
                cached=CACHE/(repo.replace('/','-')+'-'+pin+'-'+path.replace('/','_'))
                if not cached.exists():
                    print('SKIP optional full-source shaping comparison: source cache absent (offline check)')
                    continue
                source=cached.read_bytes(); assert digest(source)==SOURCE_HASHES[face]
                original=hb.Font(hb.Face(source)); hb.ot_font_set_funcs(original)
                orig=hb.Buffer(); orig.add_str(sample); orig.guess_segment_properties(); hb.shape(original,orig,{'kern':True})
                assert len(buf.glyph_infos)==len(orig.glyph_infos),(sample,'shaping glyph count changed')
                assert [(p.x_advance,p.x_offset,p.y_offset) for p in buf.glyph_positions]==[(p.x_advance,p.x_offset,p.y_offset) for p in orig.glyph_positions],(sample,'shaping positions changed')
        print('PASS',face,'family/hash/coverage/static/shaping',len(raw))
    print('PASS',len(data),'locale corpora including Halloween; rerun on translation changes')

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--build',action='store_true');parser.add_argument('--check',action='store_true');parser.add_argument('--only',choices=FACES)
    args=parser.parse_args()
    if args.build: build(args.only)
    if args.check: check()
