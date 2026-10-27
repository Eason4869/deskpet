"""One explicit file contract for portable archives and Inno staging."""
from pathlib import Path, PurePosixPath
import argparse
import json
import shutil
import re
import subprocess
import posixpath
import base64
import os

USER_DOCS = [
    'configuration-guide.md', 'agent-workflows.md', 'skin-format.md', 'character-packs.md',
    'dialogue-setup.md', 'conversation-agent.md', 'third-party-notices.md',
    'installation.md', 'daily-use.md', 'voice-presets.md', 'sources.md',
    'interaction-extension.md', 'holiday-outfits.md', 'image-generation.md', 'im-channels.md',
]
BINARIES = ['桌面伙伴.exe', '桌面伙伴.exe.config', 'DeskPet.Update.exe', 'DeskPet.Update.exe.config']

def collect_runtime_files(project_root, app_root):
    project_root, app_root = Path(project_root), Path(app_root)
    plan = [dict(source=str(app_root/name), destination=name) for name in BINARIES]
    plan += [dict(source=str(project_root/'templates/runtime-readme.md'), destination='README.md'),
             dict(source=str(project_root/'CHANGELOG.md'), destination='CHANGELOG.md'),
             dict(source=str(project_root/'templates/runtime-readme.en.md'), destination='docs/readme.en.md'),
             dict(source=str(project_root/'CHANGELOG.en.md'), destination='docs/changelog.en.md', link_base='')]
    plan += [dict(source=str(project_root/'docs'/name), destination='docs/'+name) for name in USER_DOCS]
    destinations={item['destination'] for item in plan}
    document_destinations={Path(item['source']).relative_to(project_root).as_posix():item['destination']
                           for item in plan if item['destination'].endswith('.md')}
    source=(project_root/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')
    version=re.search(r'DisplayVersion="([^"]+)"',source)[1]
    for item in plan:
        if not item['destination'].endswith('.md'):continue
        content=Path(item['source']).read_text(encoding='utf-8-sig')
        def link(match):
            label,target=match.group(1),match.group(2)
            if re.match(r'[a-zA-Z][a-zA-Z0-9+.-]*:',target) or target.startswith('#'):return match[0]
            location,separator,anchor=target.partition('#')
            relative=posixpath.normpath(posixpath.join(item.get('link_base',posixpath.dirname(item['destination'])),location))
            runtime_target=relative if relative in destinations else document_destinations.get(relative)
            if runtime_target:
                path=posixpath.relpath(runtime_target,posixpath.dirname(item['destination']) or '.')
                return label+'('+path+(separator+anchor if separator else '')+')'
            if relative.startswith('../') or not (project_root/relative).is_file():return match[0]
            prefix='https://raw.githubusercontent.com/Eason4869/deskpet/v' if label.startswith('!') else 'https://github.com/Eason4869/deskpet/blob/v'
            return label+'('+prefix+version+'/'+relative+(separator+anchor if separator else '')+')'
        item['content']=re.sub(r'(!?\[[^\]]*\])\(([^)]+)\)',link,content)
    return plan

def validate_binaries(project_root, app_root):
    project_root, app_root=Path(project_root),Path(app_root)
    source=(project_root/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')
    version=re.search(r'DisplayVersion="([^"]+)"',source)[1]
    assembly=re.search(r'AssemblyVersion="([^"]+)"',source)[1]
    process=subprocess.run(['git','-C',str(project_root),'rev-parse','HEAD'],capture_output=True,text=True)
    commit=process.stdout.strip() if process.returncode==0 else ''
    expected=version+('+'+commit if re.fullmatch('[a-f0-9]{40}',commit) else '')
    if '-dev' in version and '+' not in expected:
        raise ValueError('Preview packages require a verifiable Git commit')
    script='$ErrorActionPreference="Stop"; foreach($name in @("桌面伙伴.exe","DeskPet.Update.exe")) { $path=Join-Path $env:DESKPET_RUNTIME_CHECK $name; if([Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion -cne $env:DESKPET_EXPECTED_BUILD -or [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString() -cne $env:DESKPET_EXPECTED_ASSEMBLY){throw ("Stale build identity: "+$name)} }'
    result=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-EncodedCommand',base64.b64encode(script.encode('utf-16-le')).decode()],env=dict(os.environ,DESKPET_RUNTIME_CHECK=str(app_root),DESKPET_EXPECTED_BUILD=expected,DESKPET_EXPECTED_ASSEMBLY=assembly),capture_output=True,timeout=30)
    if result.returncode:
        raise ValueError('Rebuild application and helper: stale full release identity')

def stage_runtime(plan, staging_root):
    staging_root = Path(staging_root)
    seen = set()
    for item in plan:
        name = item['destination']
        path = PurePosixPath(name)
        if not name or path.is_absolute() or '\\' in name or ':' in name or '"' in name or any(p in ('', '.', '..') for p in name.split('/')) or any(ord(c)<32 for c in name):
            raise ValueError('Invalid runtime destination: '+name)
        if name.casefold() in seen:
            raise ValueError('Duplicate runtime destination: '+name)
        seen.add(name.casefold())
        source = Path(item['source'])
        if not source.is_file() or source.is_symlink() or getattr(source.stat(), 'st_file_attributes', 0) & 0x400:
            raise ValueError('Missing or linked runtime source: '+str(source))
    staging_root.mkdir(parents=True, exist_ok=False)
    for item in plan:
        target = staging_root/item['destination']
        target.parent.mkdir(parents=True, exist_ok=True)
        if 'content' in item:target.write_text(item['content'],encoding='utf-8')
        else:shutil.copy2(item['source'], target)

def write_inno_files(plan, staging_root, include_path):
    lines = []
    for item in plan:
        relative = PurePosixPath(item['destination'])
        source = str(Path(staging_root)/relative)
        destination = '{app}' + (('\\'+str(relative.parent).replace('/', '\\')) if str(relative.parent)!='.' else '')
        lines.append('Source: "'+source+'"; DestDir: "'+destination+'"; Flags: ignoreversion')
    Path(include_path).write_text('\n'.join(lines)+'\n', encoding='utf-8-sig')

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--project',type=Path,required=True)
    parser.add_argument('--app',type=Path,required=True)
    parser.add_argument('--stage',type=Path,required=True)
    parser.add_argument('--inno',type=Path,required=True)
    args=parser.parse_args()
    validate_binaries(args.project,args.app)
    files=collect_runtime_files(args.project,args.app)
    stage_runtime(files,args.stage)
    write_inno_files(files,args.stage,args.inno)
    print(json.dumps({'files':len(files),'stage':str(args.stage)},ensure_ascii=False))
