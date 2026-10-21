"""Verify an immutable tag and channel before building or publishing any asset."""
from pathlib import Path
import argparse
import json
import re
import subprocess
from release_assets import release_metadata, version_key

ROOT=Path(__file__).resolve().parents[1]
def git(*args):
    return subprocess.check_output(['git',*args],cwd=ROOT,text=True).strip()

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--tag',required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--latest')
    args=parser.parse_args()
    source=(ROOT/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')
    version=re.search(r'DisplayVersion="([^"]+)"',source)[1]
    key=version_key(version)
    metadata=release_metadata(version,args.tag,'dev' if key[4]==0 else 'main')
    commit=git('rev-parse',args.tag+'^{commit}')
    if commit!=git('rev-parse','HEAD') or git('status','--porcelain'):
        raise ValueError('Release requires a clean checkout of the exact tagged commit')
    if subprocess.run(['git','merge-base','--is-ancestor',commit,'origin/'+metadata['branch']],cwd=ROOT).returncode:
        raise ValueError('Tagged commit does not belong to the release branch')
    if args.latest and not metadata['prerelease'] and key<=version_key(args.latest):
        raise ValueError('The stable latest version must advance')
    expected='.'.join(str(n) for n in key[:4])
    if re.search(r'AssemblyVersion="([^"]+)"',source)[1]!=expected:
        raise ValueError('Display version and AssemblyVersion disagree')
    metadata['commit']=commit
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(metadata,indent=2),encoding='utf-8')
