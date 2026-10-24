"""Actual About confirmation, WPF owner close, trusted helper and unique Inno.

Never uses production AppId, single-instance SID/wake IPC, startup or shortcuts.
All children use one owned inactive desktop, never SwitchDesktop.
"""
from pathlib import Path
import atexit, ctypes, hashlib, json, os, shutil, subprocess, sys, time, uuid, winreg
from worker_fixture import PROJECT, CSC, VERSION, COMMIT, compile_sources
sys.path.insert(0,str(PROJECT))

ROOT=PROJECT/'work'/('uc-'+uuid.uuid4().hex[:8]);ROOT.mkdir()
APP_ID=str(uuid.uuid4()).upper();MUTEX='Local\\DeskPet.UninstallUi.'+uuid.uuid4().hex
INNO=Path(os.environ['DESKPET_TEST_INNO']);assert INNO.is_file()
runtime=ROOT/'runtime';runtime.mkdir();installation=ROOT/'i';data=installation/'data'
key_path='Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{'+APP_ID+'}_is1'
name='DeskPet-UninstallUi-'+uuid.uuid4().hex
u=ctypes.WinDLL('user32',use_last_error=True);u.CreateDesktopW.restype=ctypes.c_void_p
u.CreateDesktopW.argtypes=[ctypes.c_wchar_p,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p]
desktop=u.CreateDesktopW(name,None,None,0,0x10000000,None)
if not desktop:raise ctypes.WinError(ctypes.get_last_error())
u.CloseDesktop.argtypes=[ctypes.c_void_p];atexit.register(u.CloseDesktop,desktop)
passed=0
def check(ok,label):
    global passed
    if not ok:raise AssertionError(label)
    passed+=1;print('PASS '+label,flush=True)
def start(command,cwd=ROOT):
    si=subprocess.STARTUPINFO();si.lpDesktop=name;si.dwFlags|=subprocess.STARTF_USESHOWWINDOW;si.wShowWindow=0
    return subprocess.Popen(list(map(str,command)),cwd=cwd,startupinfo=si,creationflags=subprocess.CREATE_NO_WINDOW)
def run(command,seconds=90):
    p=start(command);code=p.wait(timeout=seconds);check(code==0,'owned command '+Path(command[0]).name+' exit='+str(code))
def wait(checker,seconds=40):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if (data/'fixture-error').exists():raise AssertionError((data/'fixture-error').read_text(encoding='utf-8-sig'))
        if checker():return
        time.sleep(.04)
    raise TimeoutError('owned WPF/Inno fixture')
def registered():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key_path):return True
    except FileNotFoundError:return False
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

# Reuse the real WPF compiler/resources, changing only test identity and entrypoint.
rsp=(PROJECT/'work/UninstallUiTests.rsp').read_text(encoding='utf-8-sig')
for filename in ('ReleaseInfo.cs','UpdateInstall.cs'):
    original=PROJECT/'src'/filename;text=original.read_text(encoding='utf-8-sig')
    if filename=='ReleaseInfo.cs':text=text.replace('Local\\\\Dafeiyu.DeskPet.Installation',MUTEX.replace('\\','\\\\'))
    else:text=text.replace('3B63C162-076E-4B2F-BDCC-2572664875A9',APP_ID)
    target=ROOT/filename;target.write_text(text,encoding='utf-8')
    rsp=rsp.replace(str(original),str(target))
rsp=rsp.replace('/main:Dafeiyu.Tests.UninstallUiTests','/main:Dafeiyu.Tests.UninstallInstalledUi')
rsp=rsp.replace(str(PROJECT/'tests/UninstallUiTests.cs'),str(PROJECT/'tests/UninstallInstalledUi.cs'))
rsp=rsp.replace('/out:'+str(PROJECT/'work/UninstallUiTests.exe'),'/out:'+str(runtime/'桌面伙伴.exe'))
rsp=rsp.replace(',UninstallUiTests.g.resources',',桌面伙伴.g.resources')
(ROOT/'app.rsp').write_text(rsp,encoding='utf-8-sig');run([CSC,'@'+str(ROOT/'app.rsp')])
shutil.copy2(PROJECT/'app.config',runtime/'桌面伙伴.exe.config')
for dll in (PROJECT/'work').glob('*.dll'):shutil.copy2(dll,runtime/dll.name)
compile_sources(runtime/'DeskPet.Update.exe',VERSION,APP_ID,MUTEX)
iss=ROOT/'Owned.iss'
iss.write_text('''[Setup]
AppId={{%s}
AppName=DeskPet owned WPF uninstall fixture
AppVersion=%s
DefaultDirName=%s
PrivilegesRequired=lowest
AppMutex=%s
CloseApplications=no
RestartApplications=no
Uninstallable=yes
OutputDir=%s
OutputBaseFilename=OwnedSetup
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64compatible
[Files]
Source: "%s\\*"; DestDir: "{app}"
'''%(APP_ID,VERSION,installation,MUTEX,ROOT,runtime),encoding='utf-8-sig')
run([INNO,'/Qp',iss]);run([ROOT/'OwnedSetup.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/DIR='+str(installation),'/LOG='+str(ROOT/'install.log')])
check(registered(),'real unique registration exists')
data.mkdir(exist_ok=True)
for relative in ['history/retained.history','relationships/retained.json','relationships/receipts.json','commemorations/portraits/retained.png','preserved-attachments/original.png']:
    p=data/relative;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(('retained '+relative).encode())
parent=holder=None
try:
    for mode in ('cancel','veto','busy','savefail'):
        parent=start([installation/'桌面伙伴.exe',mode],installation)
        wait(lambda:(data/(mode+'-alive')).exists() or parent.poll() is not None)
        check((data/(mode+'-alive')).exists(),'actual installed '+mode+' leaves pet/session running')
        check(parent.wait(timeout=30)==0,'owned normal exit after '+mode)
        check(registered() and (installation/'桌面伙伴.exe').exists(),mode+' never removes real owned installation')
    holder=start([installation/'桌面伙伴.exe','holder'],installation);wait(lambda:(data/'holder-ready').exists())
    parent=start([installation/'桌面伙伴.exe','commit'],installation);wait(lambda:(data/'closed-and-committed').exists())
    jobs=list((data/'uninstall').glob('*/commit'));check(len(jobs)==1,'actual About confirm publishes one commit after WPF close/drains')
    job=jobs[0].parent
    check(parent.poll() is None and not (job/'result.json').exists(),'live exact WPF parent prevents uninstaller launch after Closed')
    (data/'parent-exit').write_text('exit');check(parent.wait(timeout=10)==0,'exact parent exits normally after production shutdown hook')
    time.sleep(.6);check(not (job/'result.json').exists() and registered(),'separate unowned mutex object still prevents real uninstall')
    snapshots=json.loads((data/'pre-uninstall.json').read_text(encoding='utf-8-sig'));before={row['Key']:row['Value'] for row in snapshots}
    check(any(p.endswith('holiday-state.json') for p in before) and any('commemorations\\scenes\\' in p for p in before),'real earned holiday state and original scene included in preservation hashes')
    (data/'holder-exit').write_text('exit');check(holder.wait(timeout=10)==0,'last fixture mutex handle closes normally')
    wait(lambda:(job/'result.json').exists(),60)
    check(json.loads((job/'result.json').read_text(encoding='utf-8-sig'))['Success'],'real silent Inno succeeds after actual WPF close and last mutex release')
    check(not registered() and not (installation/'桌面伙伴.exe').exists(),'only unique installed application and registration removed')
    check(all((data/p).is_file() and digest(data/p)==h for p,h in before.items()),'all final settings history relationships keys and commemorations remain byte-identical')
finally:
    if parent is not None and parent.poll() is None:
        (data/'parent-exit').write_text('exit');parent.wait(timeout=30)
    if holder is not None and holder.poll() is None:
        (data/'holder-exit').write_text('exit');holder.wait(timeout=15)
    if registered() and (installation/'unins000.exe').exists():run([installation/'unins000.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'])
print('Uninstall installed WPF smoke: %d passed; %s'%(passed,ROOT),flush=True)
