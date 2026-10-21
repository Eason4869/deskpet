"""Exercise installation detection with an isolated uninstall registration."""
from pathlib import Path
import os
import subprocess
import uuid
import winreg

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT / 'work' / ('update-registration-' + uuid.uuid4().hex)
ROOT.mkdir(parents=True)
fixture_id = str(uuid.uuid4()).upper()
source = (PROJECT / 'src/UpdateInstall.cs').read_text(encoding='utf-8-sig')
source = source.replace('3B63C162-076E-4B2F-BDCC-2572664875A9', fixture_id)
(ROOT / 'UpdateInstall.cs').write_text(source, encoding='utf-8-sig')
(ROOT / 'Probe.cs').write_text('''using System; class Probe {
    static int Main(string[] args) { try {
        bool actual=Dafeiyu.UpdateInstall.IsInstalledDirectory(args[0]);
        Console.WriteLine("installed="+actual); return actual==Boolean.Parse(args[1])?0:1;
    } catch(Exception e) { Console.WriteLine(e.Message); return 2; } }
}''', encoding='utf-8-sig')
framework = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319'
subprocess.run([str(framework / 'csc.exe'), '/nologo', '/codepage:65001',
    '/out:' + str(ROOT / 'Probe.exe'),
    *['/reference:' + str(framework / name) for name in ['System.Runtime.Serialization.dll', 'System.IO.Compression.dll']],
    str(ROOT / 'Probe.cs'), str(ROOT / 'UpdateInstall.cs'), str(PROJECT / 'src/ReleaseInfo.cs'),str(PROJECT/'src/ReleaseVersion.cs'),str(PROJECT/'src/ReleaseIdentity.cs'),str(PROJECT/'src/RetiredRuntimeFiles.cs')], check=True)
directory = ROOT / '中文 portable with spaces'
directory.mkdir()
(directory / '桌面伙伴.exe').write_bytes(b'owned app fixture')
key_path = r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + fixture_id + '}_is1'
passed = 0

def check(expected, description):
    global passed
    result = subprocess.run([str(ROOT / 'Probe.exe'), str(directory), str(expected)], capture_output=True, text=True, errors='replace', timeout=10)
    if result.returncode:
        raise AssertionError(description + ': ' + result.stdout + result.stderr)
    passed += 1
    print('PASS ' + description, flush=True)

try:
    check(False, 'unregistered portable folder selects ZIP updates')
    with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_WRITE | winreg.KEY_WOW64_64KEY) as key:
        winreg.SetValueEx(key, 'InstallLocation', 0, winreg.REG_SZ, str(directory))
        check(False, 'stale location alone does not select an installer')
        uninstall = directory / 'unins000.exe'
        winreg.SetValueEx(key, 'UninstallString', 0, winreg.REG_SZ, '"' + str(uninstall) + '"')
        check(False, 'missing registered uninstaller selects portable updates')
        uninstall.write_bytes(b'owned uninstaller fixture')
        check(False, 'missing uninstall metadata selects portable updates')
        uninstall.with_suffix('.dat').write_bytes(b'owned uninstall metadata')
        check(True, 'matching live Inno registration selects installer updates')
        winreg.SetValueEx(key, 'InstallLocation', 0, winreg.REG_SZ, str(ROOT / 'different install'))
        check(False, 'another installation does not capture this portable folder')
        winreg.SetValueEx(key, 'InstallLocation', 0, winreg.REG_SZ, str(directory))
        winreg.SetValueEx(key, 'UninstallString', 0, winreg.REG_SZ, '"' + str(ROOT / 'unins000.exe') + '"')
        check(False, 'uninstaller outside the current directory is stale')
        winreg.SetValueEx(key, 'UninstallString', 0, winreg.REG_SZ, 'unins000.exe')
        check(False, 'relative uninstaller registration cannot capture a portable folder')
        winreg.SetValueEx(key, 'UninstallString', 0, winreg.REG_SZ, '"' + str(uninstall) + '"')
        winreg.SetValueEx(key, 'InstallLocation', 0, winreg.REG_SZ, r'C:\invalid|path')
        check(False, 'malformed registration does not prevent portable updates')
finally:
    try:
        winreg.DeleteKeyEx(winreg.HKEY_CURRENT_USER, key_path, winreg.KEY_WOW64_64KEY, 0)
    except FileNotFoundError:
        pass
print('Update registration smoke: %d passed' % passed)
