"""Install, upgrade, launch and uninstall the built installer in an owned test folder."""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import uuid
import winreg
from WindowsShortcut import create_shortcut

PROJECT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(PROJECT))
from tools.release_assets import installer_asset_name
VERSION=re.search(r'DisplayVersion="([^"]+)"',(PROJECT/'src/ReleaseInfo.cs').read_text(encoding='utf-8-sig')).group(1)
parser=argparse.ArgumentParser()
parser.add_argument('--output-directory',type=Path,default=PROJECT.parent/'outputs')
options=parser.parse_args()
OUTPUT=options.output_directory.resolve()
SETUP=OUTPUT/installer_asset_name(VERSION)
APP_KEY=r'Software\Microsoft\Windows\CurrentVersion\Uninstall\{3B63C162-076E-4B2F-BDCC-2572664875A9}_is1'
RUN_KEY=r'Software\Microsoft\Windows\CurrentVersion\Run'
ENTRY='DafeiyuDeskPet'
root=PROJECT/'work'/('installer-smoke-'+uuid.uuid4().hex)
install=root/'中文 install'
passed=0
process=None
registration=None
old_startup=None
changed_startup=False
legacy_shortcut=None
foreign_shortcut=None

def shortcut(path,target):
    create_shortcut(path,target)

def check(value,label):
    global passed
    if not value: raise AssertionError(label)
    passed+=1
    print('PASS '+label,flush=True)

def execute(executable,args,log):
    flags=['/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/LOG='+str(log)]
    startup=subprocess.STARTUPINFO(); startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow=0
    return subprocess.run([str(executable),*flags,*args],startupinfo=startup,timeout=60).returncode

user=ctypes.WinDLL('user32',use_last_error=True)
ENUM=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
user.EnumWindows.argtypes=[ENUM,wintypes.LPARAM]
user.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)]
user.PostMessageW.argtypes=[wintypes.HWND,wintypes.UINT,wintypes.WPARAM,wintypes.LPARAM]
def close_owned_app():
    if process is None or process.poll() is not None: return
    @ENUM
    def close(window,unused):
        pid=wintypes.DWORD();user.GetWindowThreadProcessId(window,ctypes.byref(pid))
        if pid.value==process.pid:user.PostMessageW(window,0x10,0,0)
        return True
    user.EnumWindows(close,0);process.wait(timeout=10)

def wait_for_uninstall_cleanup():
    # Inno's self-removal helper can outlive the process handle we waited for.
    deadline=time.monotonic()+10
    while registration.exists() and time.monotonic()<deadline:time.sleep(.1)

try:
    check(SETUP.is_file(),'release EXE installer exists')
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,APP_KEY):
            raise RuntimeError('An existing installation is registered; refusing to replace it during this test.')
    except FileNotFoundError:pass
    root.mkdir(parents=True)
    args=['/DIR='+str(install),'/GROUP=DeskPet installer test '+root.name,'/TASKS=']
    check(execute(SETUP,args,root/'install.log')==0,'current-user installer succeeds without elevation')
    registration=install/'unins000.exe';exe=install/'桌面伙伴.exe'
    expected=OUTPUT/'DafeiyuDeskPet/桌面伙伴.exe'
    check(hashlib.sha256(exe.read_bytes()).digest()==hashlib.sha256(expected.read_bytes()).digest(),'installed executable matches the release binary')
    check((install/'templates/pet.json').exists() and (install/'docs/skin-format.md').exists(),'installation includes templates and documentation')
    check(not (install/'data/settings.json').exists(),'installer contains no personal settings')
    settings=install/'data/settings.json';settings.parent.mkdir()
    settings.write_text(json.dumps({'SkinId':'e-bao','Volume':27},ensure_ascii=False),encoding='utf-8')
    skin=install/'data/skins/user-pack/keep.txt';skin.parent.mkdir(parents=True);skin.write_text('retain imported skin',encoding='utf-8')
    download=install/'data/downloads/keep.txt';download.parent.mkdir();download.write_text('retain download',encoding='utf-8')
    settings_before=settings.read_bytes()
    # Simulate the previous product name in this exact installation, never another copy.
    legacy=install/'大肥鱼桌宠.exe';legacy_config=install/'大肥鱼桌宠.exe.config'
    legacy.write_bytes(b'legacy name sentinel');legacy_config.write_text('legacy config sentinel',encoding='utf-8')
    group=Path(os.environ['APPDATA'])/'Microsoft/Windows/Start Menu/Programs'/('DeskPet installer test '+root.name)
    legacy_shortcut=group/'大肥鱼桌面伙伴.lnk';foreign_shortcut=group/'卸载大肥鱼桌面伙伴.lnk'
    shortcut(legacy_shortcut,legacy);shortcut(foreign_shortcut,root/'other installation'/'unins000.exe')
    check(legacy_shortcut.exists() and foreign_shortcut.exists(),'custom Start Menu group contains owned and foreign legacy shortcut fixtures')
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:
        try:old_startup=winreg.QueryValueEx(key,ENTRY)
        except FileNotFoundError:pass
        changed_startup=True
        winreg.SetValueEx(key,ENTRY,0,winreg.REG_SZ,'"'+str(legacy)+'" --startup')
    check(execute(SETUP,args,root/'upgrade.log')==0,'reinstall upgrades the same registered application')
    check(not legacy.exists() and not legacy_config.exists(),'upgrade removes the two known legacy executable files')
    check(not legacy_shortcut.exists(),'upgrade removes the owned legacy shortcut in a custom Start Menu group')
    check(foreign_shortcut.exists(),'upgrade preserves a legacy-named shortcut targeting another installation')
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:
        check(winreg.QueryValueEx(key,ENTRY)[0]=='"'+str(exe)+'" --startup',"upgrade migrates only this installation's previously enabled autostart")
    check(settings.read_bytes()==settings_before and skin.read_text(encoding='utf-8')=='retain imported skin' and download.exists(),'upgrade preserves settings, imported skins and downloads')
    startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
    process=subprocess.Popen([str(exe)],cwd=install,startupinfo=startup)
    time.sleep(1)
    check(process.poll() is None,'installed app starts from a path with Chinese characters and spaces')
    binary_before=exe.read_bytes()
    check(execute(SETUP,args,root/'running-upgrade.log')!=0,'upgrade refuses to replace a running application')
    check(process.poll() is None and exe.read_bytes()==binary_before,'refused upgrade preserves the running application')
    close_owned_app()
    check(process.returncode==0,'installed app closes normally')
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:
        winreg.SetValueEx(key,ENTRY,0,winreg.REG_SZ,'"'+str(exe)+'" --startup');changed_startup=True
    check(execute(registration,[],root/'uninstall.log')==0,'uninstaller completes normally')
    wait_for_uninstall_cleanup()
    check(not exe.exists() and not registration.exists(),'uninstaller removes installed program files')
    check(json.loads(settings.read_text(encoding='utf-8-sig'))['Volume']==27 and skin.exists() and download.exists(),'uninstall preserves personal data')
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:
        try:winreg.QueryValueEx(key,ENTRY);removed=False
        except FileNotFoundError:removed=True
    check(removed,'uninstall clears its own autostart entry')
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,APP_KEY):removed=False
    except FileNotFoundError:removed=True
    check(removed,'uninstall clears the application registration')
    check(execute(SETUP,args,root/'foreign-install.log')==0,'application can be installed again after removal')
    foreign_command='"'+str(root/'another copy'/'桌面伙伴.exe')+'" --startup'
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY,0,winreg.KEY_SET_VALUE) as key:
        winreg.SetValueEx(key,ENTRY,0,winreg.REG_SZ,foreign_command)
    check(execute(registration,[],root/'foreign-uninstall.log')==0,'second uninstaller completes normally')
    wait_for_uninstall_cleanup()
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:
        check(winreg.QueryValueEx(key,ENTRY)[0]==foreign_command,'uninstall preserves the autostart of a different installation')
    (root/'result.json').write_text(json.dumps({'passed':passed,'install':str(install)},ensure_ascii=False,indent=2),encoding='utf-8')
    print('Installer smoke:',passed,'passed',flush=True)
finally:
    close_owned_app()
    if changed_startup:
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:
            if old_startup is None:
                try:
                    current=winreg.QueryValueEx(key,ENTRY)[0]
                    if current in ('"'+str(install/'桌面伙伴.exe')+'" --startup','"'+str(install/'大肥鱼桌宠.exe')+'" --startup',locals().get('foreign_command')):winreg.DeleteValue(key,ENTRY)
                except FileNotFoundError:pass
            else:winreg.SetValueEx(key,ENTRY,0,old_startup[1],old_startup[0])
    if registration is not None and registration.exists():execute(registration,[],root/'cleanup.log')
    for created in (legacy_shortcut,foreign_shortcut):
        if created is not None and created.exists():created.unlink()
    if legacy_shortcut is not None:
        try:legacy_shortcut.parent.rmdir()
        except OSError:pass
