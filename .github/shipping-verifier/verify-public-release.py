"""Verify published release identity, manifests and anonymous download reachability."""
import argparse,hashlib,json,urllib.request
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--release',type=Path,required=True);p.add_argument('--commit',required=True);p.add_argument('--output',type=Path,required=True)
a=p.parse_args();release=json.loads(a.release.read_text(encoding='utf-8'))
assert release['tag_name']=='v1.0.4-dev.2' and release['prerelease'] and not release['draft']
assets={x['name']:x for x in release['assets']};assert len(assets)==len(release['assets'])
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
def fetch(name,partial=False):
 url=assets[name]['browser_download_url'];assert url.startswith('https://github.com/Eason4869/deskpet/releases/download/v1.0.4-dev.2/')
 request=urllib.request.Request(url,headers={'User-Agent':'DeskPet-release-verification',**({'Range':'bytes=0-1023'} if partial else {})})
 with opener.open(request,timeout=90) as response:
  body=response.read(1024 if partial else 4*1024*1024)
  assert response.status in (200,206) and body
  return body,response.status
checks={}
sha_data,_=fetch('SHA256.txt')
for line in sha_data.decode('utf-8-sig').splitlines():
 if not line.strip():continue
 digest,relative=line.split('  ',1);relative=relative.lstrip('*').replace(chr(92),'/')
 assert relative and not relative.startswith('/') and ':' not in relative and '..' not in relative.split('/')
 name=relative.rsplit('/',1)[-1]
 assert name not in checks,(name,'duplicate manifest basename')
 assert name in assets,(name,'missing');assert assets[name]['digest']=='sha256:'+digest,(name,'digest mismatch')
 checks[name]={'sha256':digest,'manifest':'SHA256.txt'}
if 'mac-upload-manifest.json' in assets:
 body,_=fetch('mac-upload-manifest.json');manifest=json.loads(body)
 assert manifest['commit']==a.commit and manifest['version']=='1.0.4-dev.2'
 for item in manifest['assets']:
  name=item['name'];assert name in assets and assets[name]['digest']=='sha256:'+item['sha256']
  assert assets[name]['size']==item['size'];checks[name]={'sha256':item['sha256'],'manifest':'mac-upload-manifest.json'}
else:raise AssertionError('Missing macOS checksum manifest')
for name,asset in assets.items():
 if name not in ('SHA256.txt','mac-upload-manifest.json'):assert name in checks,(name,'not covered by a checksum manifest')
 body,status=fetch(name,True)
 checks.setdefault(name,{'sha256':asset['digest'].removeprefix('sha256:')})
 checks[name].update(size=asset['size'],anonymous_http_status=status,download_prefix_bytes=len(body))
a.output.parent.mkdir(parents=True,exist_ok=True)
a.output.write_text(json.dumps({'release_id':release['id'],'tag':release['tag_name'],'commit':a.commit,'checks':checks,'download_scope':'anonymous prefix read; full artifact digests compared with build manifests and GitHub asset SHA256'},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'PASS {len(checks)} assets: digest manifests and anonymous download prefix')
