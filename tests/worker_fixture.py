"""Compile the production helper sources with explicitly isolated fixture identity.

No production registration, startup, single-instance or wake event is accessed.
"""
from pathlib import Path
import os
import re
import shutil
import subprocess

PROJECT = Path(__file__).resolve().parents[1]
CSC = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
APP_ID = '3B63C162-076E-4B2F-BDCC-2572664875A9'
VERSION = re.search(r'DisplayVersion="([^"]+)"', (PROJECT/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig'))[1]
COMMIT = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=PROJECT, text=True).strip()

def numeric(version):
    from tools.release_assets import version_key
    return '.'.join(map(str, version_key(version)[:4]))

def compile_sources(destination, version, fixture_id=None, mutex=None, parent_source=None):
    destination = Path(destination)
    scratch = destination.parent.parent / ('.worker-fixture-sources-'+destination.parent.name) / destination.stem
    scratch.mkdir(parents=True,exist_ok=True)
    sources = []
    for name in (PROJECT/'tools/update-worker-sources.txt').read_text().splitlines():
        source = PROJECT/'src'/name
        if name in ('ReleaseInfo.cs', 'UpdateInstall.cs'):
            text = source.read_text(encoding='utf-8-sig')
            if name == 'ReleaseInfo.cs':
                text = re.sub(r'AssemblyVersion="[^"]+"', 'AssemblyVersion="'+numeric(version)+'"', text)
                text = re.sub(r'DisplayVersion="[^"]+"', 'DisplayVersion="'+version+'"', text)
                if mutex:
                    text = text.replace('Local\\\\Dafeiyu.DeskPet.Installation', mutex.replace('\\','\\\\'))
            elif fixture_id:
                assert fixture_id != APP_ID
                text = text.replace(APP_ID, fixture_id)
            source = scratch/Path(name).name
            source.write_text(text, encoding='utf-8')
        sources.append(source)
    identity = scratch/'Identity.cs'
    identity.write_text('[assembly:System.Reflection.AssemblyInformationalVersion("'+version+'+'+COMMIT+'")]', encoding='utf-8')
    sources.append(identity)
    if parent_source is not None:
        parent = scratch/'Parent.cs'
        parent.write_text(parent_source, encoding='utf-8')
        sources.append(parent)
    references = ['System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.IO.Compression.dll']
    resources = ['/resource:'+str(p)+',Dafeiyu.Locales.'+p.name for p in sorted((PROJECT/'assets/locales').glob('*.json'))]
    subprocess.run([str(CSC), '/nologo', '/codepage:65001', '/utf8output', '/target:winexe',
        '/define:UPDATE_WORKER', '/main:'+('Probe' if parent_source is not None else 'Dafeiyu.UpdateWorkerProgram'),
        '/out:'+str(destination), *['/reference:'+str(CSC.parent/r) for r in references],
        *resources, *map(str,sources)], check=True)
    shutil.copy2(PROJECT/'app.config', str(destination)+'.config')
