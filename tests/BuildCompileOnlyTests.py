"""Compile a real unaware Main: exact flag skips it, a similar argument does not."""
import os
import subprocess
import tempfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
# CI uses Windows PowerShell 5.1, whose default for BOM-less scripts is ANSI.
# Preserve the Unicode executable filename literals before invoking that host.
assert (ROOT/'Build.ps1').read_bytes().startswith(b'\xef\xbb\xbf'),'Build.ps1 requires a UTF-8 BOM for Windows PowerShell 5.1'
scratch=ROOT/'work/compile-only-probe'
scratch.mkdir(parents=True,exist_ok=True)
with tempfile.TemporaryDirectory(dir=scratch) as folder:
    marker=Path(folder)/'main.txt'
    env=dict(os.environ,DESKPET_COMPILE_ONLY_PROBE=str(marker))
    command=['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'Build.ps1'),'-Target','BuildCompileOnlyProbe','-TestArguments']
    subprocess.run(command+['--compile-only'],cwd=ROOT,env=env,check=True)
    assert (ROOT/'work/BuildCompileOnlyProbe.exe').is_file(),'real compiled artifact missing'
    assert not marker.exists(),'Main executed during compile-only'
    subprocess.run(command+['--compile-only-similar'],cwd=ROOT,env=env,check=True)
    assert marker.read_text()=='Main executed','similar argument incorrectly skipped Main'
print('PASS compile-only produces an actual assembly without executing Main; similar flag executes Main')
