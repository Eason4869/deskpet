"""Real helper + real Inno in a unique, owned installation; never production keys.

Uses an inert parent (no Program/wake IPC), unique AppId and installation mutex,
and a minimal test installer with no startup values, icons or Run section.
"""
from pathlib import Path
import hashlib
import ctypes
import atexit
import json
import os
import shutil
import subprocess
import sys
import time
import uuid
import winreg

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT))
from worker_fixture import compile_sources, VERSION, COMMIT, numeric

ROOT = PROJECT/'work'/('uninstall-handoff-'+uuid.uuid4().hex)
ROOT.mkdir(parents=True)
APP_ID = str(uuid.uuid4()).upper()
MUTEX = 'Local\\DeskPet.UninstallFixture.'+uuid.uuid4().hex
INNO = Path(os.environ.get('DESKPET_TEST_INNO', str(PROJECT/'work/tools/inno/ISCC.exe')))
assert INNO.is_file(), 'Set DESKPET_TEST_INNO to the read-only compiler'
passed = 0
# All child windows, including /SILENT progress, belong to this inactive desktop.
# Never SwitchDesktop, enumerate user windows, or wake the real Program process.
desktop_name='DeskPet-Uninstall-'+uuid.uuid4().hex
user32=ctypes.WinDLL('user32',use_last_error=True)
user32.CreateDesktopW.restype=ctypes.c_void_p
user32.CreateDesktopW.argtypes=[ctypes.c_wchar_p,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p]
desktop=user32.CreateDesktopW(desktop_name,None,None,0,0x10000000,None)
if not desktop: raise ctypes.WinError(ctypes.get_last_error())
user32.CloseDesktop.argtypes=[ctypes.c_void_p]
atexit.register(user32.CloseDesktop,desktop)

def check(value, label):
    global passed
    if not value:
        raise AssertionError(label)
    passed += 1
    print('PASS '+label, flush=True)

def start(command, cwd):
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 0
    si.lpDesktop=desktop_name
    return subprocess.Popen(list(map(str,command)), cwd=cwd, startupinfo=si, creationflags=subprocess.CREATE_NO_WINDOW)

def run(command):
    with start(command, ROOT) as process:
        code=process.wait(timeout=90)
        check(code==0, 'owned command succeeded: '+Path(command[0]).name+' exit='+str(code))

def wait(predicate, seconds=15):
    end = time.monotonic()+seconds
    while time.monotonic()<end:
        if predicate(): return
        time.sleep(.05)
    raise AssertionError('fixture timeout')

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

parent_source = r'''using System;using System.IO;using System.Threading;using System.Diagnostics;
public static class Probe {
 public static void Main(string[] args) {
  string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");Directory.CreateDirectory(data);
  string tag=args.Length==0?"parent":args[0];
  using(var mutex=new Mutex(false,Dafeiyu.ReleaseInfo.InstallationMutex)) {
   bool acquired=mutex.WaitOne(0);if(acquired)mutex.ReleaseMutex();
   File.WriteAllText(Path.Combine(data,tag+"-unowned"),acquired.ToString());
   using(var self=Process.GetCurrentProcess())File.WriteAllText(Path.Combine(data,tag+"-started"),self.StartTime.ToUniversalTime().Ticks.ToString());
   if(tag.StartsWith("api-")) {
    try {
     var ticket=Dafeiyu.UninstallHandoff.PrepareAsync(AppDomain.CurrentDomain.BaseDirectory).GetAwaiter().GetResult();
     File.WriteAllText(Path.Combine(data,tag+"-ready"),ticket.WorkDirectory);
     while(!File.Exists(Path.Combine(data,tag+"-action")))Thread.Sleep(30);
     try {
      if(tag=="api-abort") {ticket.Abort();try{ticket.Commit();File.WriteAllText(Path.Combine(data,tag+"-bad"),"committed");}catch(InvalidOperationException){}}
      else if(tag!="api-dispose")ticket.Commit();
     }catch(Exception){File.WriteAllText(Path.Combine(data,tag+"-failed"),"failed");}
     finally{ticket.Dispose();}
     File.WriteAllText(Path.Combine(data,tag+"-done"),"done");
    }catch(Exception){File.WriteAllText(Path.Combine(data,tag+"-prepare-failed"),"failed");}
   }
   bool denied=false;
   while(!File.Exists(Path.Combine(data,"exit-"+tag))) {
    if(tag=="denied"&&!denied&&File.Exists(Path.Combine(data,"deny-mutex"))) {
     var acl=mutex.GetAccessControl();
     acl.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User,
      System.Security.AccessControl.MutexRights.Synchronize|System.Security.AccessControl.MutexRights.Modify,System.Security.AccessControl.AccessControlType.Deny));
     mutex.SetAccessControl(acl);denied=true;File.WriteAllText(Path.Combine(data,"mutex-denied"),"denied");
    }
    Thread.Sleep(30);
   }
  }
 }
}'''
runtime = ROOT/'runtime'; runtime.mkdir()
directory = ROOT/'owned 中文 installation'
compile_sources(runtime/'桌面伙伴.exe', VERSION, APP_ID, MUTEX, parent_source)
compile_sources(runtime/'DeskPet.Update.exe', VERSION, APP_ID, MUTEX)
script = ROOT/'Owned.iss'
script.write_text('''[Setup]
AppId={{%s}
AppName=DeskPet owned uninstall fixture
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
Source: "%s\\桌面伙伴.exe"; DestDir: "{app}"
Source: "%s\\桌面伙伴.exe.config"; DestDir: "{app}"
Source: "%s\\DeskPet.Update.exe"; DestDir: "{app}"
Source: "%s\\DeskPet.Update.exe.config"; DestDir: "{app}"
''' % (APP_ID,VERSION,directory,MUTEX,ROOT,runtime,runtime,runtime,runtime), encoding='utf-8-sig')
run([INNO,'/Qp',script])
run([ROOT/'OwnedSetup.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/LOG='+str(ROOT/'install.log'),'/DIR='+str(directory)])
key_path = 'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{'+APP_ID+'}_is1'
data = directory/'data'; data.mkdir()
sentinels = ['settings.json','history/keep.history','relationships/keep.json','relationships/receipts.json','images/keep.png','skins/custom/pet.json']
for name in sentinels:
    path=data/name; path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(('preserved '+name).encode())
before = {name:digest(data/name) for name in sentinels}
parent = holder = worker = api_parent = denied_holder = None
api_tag = None

def launch_parent(tag='parent'):
    for name in (tag+'-started','exit-'+tag):
        (data/name).unlink(missing_ok=True)
    result = start([directory/'桌面伙伴.exe',tag], directory)
    wait(lambda:(data/(tag+'-started')).exists())
    return result

def make_job(pid=None, ticks=None):
    work=data/'uninstall'/uuid.uuid4().hex; work.mkdir(parents=True)
    shutil.copy2(directory/'DeskPet.Update.exe',work/'DeskPet.Update.exe')
    shutil.copy2(directory/'DeskPet.Update.exe.config',work/'DeskPet.Update.exe.config')
    job=dict(ProtocolVersion=1, InstallationDirectory=str(directory),WorkDirectory=str(work),
        UninstallerPath=str(directory/'unins000.exe'), UninstallerDigest=digest(directory/'unins000.exe'),
        UninstallerDataDigest=digest(directory/'unins000.dat'), ParentPid=parent.pid if pid is None else pid,
        ParentStartedUtcTicks=int((data/'parent-started').read_text(encoding='utf-8-sig')) if ticks is None else ticks,
        InstalledVersion=VERSION,InstalledAssemblyVersion=numeric(VERSION),InstalledBuildId=VERSION+'+'+COMMIT)
    path=work/'uninstall.json';path.write_text(json.dumps(job),encoding='utf-8')
    return work,path

def launch_job(work,path):
    return start([work/'DeskPet.Update.exe','--uninstall',path],work)

try:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key_path) as key:
        check(Path(winreg.QueryValueEx(key,'InstallLocation')[0])==directory,'real unique Inno registration belongs to owned directory')
    parent=launch_parent()
    work,path=make_job()
    worker=launch_job(work,path)
    wait(lambda:(work/'ready').exists() or worker.poll() is not None)
    check((work/'ready').exists() and worker.poll() is None,'real uninstall helper acknowledges exact live parent')
    (work/'abort').write_text('cancel')
    check(worker.wait(timeout=10)!=0 and parent.poll() is None,'aborted handoff leaves exact parent and installation alive')

    # Actual public ticket API in a real installed parent, not a mocked delegate.
    for api_tag in ('api-abort','api-dispose','api-fail'):
        api_parent=launch_parent(api_tag)
        wait(lambda:(data/(api_tag+'-ready')).exists())
        api_work=Path((data/(api_tag+'-ready')).read_text(encoding='utf-8-sig'))
        if api_tag=='api-fail': (api_work/'commit').mkdir()
        (data/(api_tag+'-action')).write_text('go')
        wait(lambda:(data/(api_tag+'-done')).exists())
        wait(lambda:(api_work/'result.json').exists())
        check(api_parent.poll() is None and not json.loads((api_work/'result.json').read_text(encoding='utf-8-sig'))['Success'],api_tag+': actual ticket abort/failure keeps parent and rejects removal')
        check(not (data/(api_tag+'-bad')).exists(),api_tag+': cancelled ticket cannot commit')
        if api_tag=='api-fail':
            check((data/(api_tag+'-failed')).exists() and (api_work/'commit').is_dir(),'commit publication failure is thrown and leaves no authorization file')
        (data/('exit-'+api_tag)).write_text('exit');api_parent.wait(timeout=10)

    api_tag='api-missing-helper'
    helper=directory/'DeskPet.Update.exe'; backup=directory/'DeskPet.Update.exe.owned-backup'
    helper.rename(backup)
    try:
        api_parent=launch_parent(api_tag)
        wait(lambda:(data/(api_tag+'-prepare-failed')).exists())
        check(api_parent.poll() is None,'actual PrepareAsync missing-helper failure leaves parent alive')
        (data/('exit-'+api_tag)).write_text('exit');api_parent.wait(timeout=10)
    finally: backup.rename(helper)

    work,path=make_job(ticks=1);worker=launch_job(work,path)
    check(worker.wait(timeout=10)!=0 and not (work/'ready').exists(),'PID start-time mismatch never acknowledges or removes installation')
    work,path=make_job(pid=2147483647);worker=launch_job(work,path)
    check(worker.wait(timeout=10)!=0 and not (work/'ready').exists(),'missing parent cannot establish initial trust')

    work,path=make_job()
    invalid=json.loads(path.read_text(encoding='utf-8-sig')); invalid['InstalledBuildId']=VERSION+'+'+'0'*40
    path.write_text(json.dumps(invalid),encoding='utf-8')
    worker=launch_job(work,path)
    check(worker.wait(timeout=10)!=0 and not (work/'ready').exists(),'wrong installed build identity never acknowledges')

    work,path=make_job()
    with (work/'DeskPet.Update.exe').open('ab') as changed: changed.write(b'changed copy')
    worker=launch_job(work,path)
    check(worker.wait(timeout=10)!=0 and not (work/'ready').exists(),'changed copied helper bytes reject even with matching assembly identity')

    work,path=make_job()
    invalid=json.loads(path.read_text(encoding='utf-8-sig')); invalid['WorkDirectory']=str(ROOT)
    path.write_text(json.dumps(invalid),encoding='utf-8')
    worker=launch_job(work,path)
    check(worker.wait(timeout=10)!=0 and not (work/'ready').exists() and not (ROOT/'result.json').exists(),'foreign job directory rejected without writing foreign results')

    # Only this test's unique registry key is changed, then restored.
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key_path,0,winreg.KEY_READ|winreg.KEY_WRITE) as key:
        original=winreg.QueryValueEx(key,'UninstallString')[0]
        try:
            winreg.SetValueEx(key,'UninstallString',0,winreg.REG_SZ,str(ROOT/'unins000.exe'))
            work,path=make_job();worker=launch_job(work,path)
            check(worker.wait(timeout=10)!=0 and not (work/'ready').exists(),'foreign registered uninstall path never acknowledges')
        finally: winreg.SetValueEx(key,'UninstallString',0,winreg.REG_SZ,original)

    work,path=make_job();worker=launch_job(work,path)
    wait(lambda:(work/'ready').exists())
    (data/'exit-parent').write_text('exit');parent.wait(timeout=10)
    check(worker.wait(timeout=10)!=0 and (directory/'桌面伙伴.exe').exists(),'parent exit without committed close never uninstalls')

    parent=launch_parent();work,path=make_job();worker=launch_job(work,path)
    wait(lambda:(work/'ready').exists())
    uninstall_data=directory/'unins000.dat'; saved_data=uninstall_data.read_bytes()
    try:
        uninstall_data.write_bytes(saved_data+b'changed after handshake')
        (work/'commit').write_bytes(b'\x01')
        (data/'exit-parent').write_text('exit');parent.wait(timeout=10)
        check(worker.wait(timeout=10)!=0 and (directory/'桌面伙伴.exe').exists(),'final revalidation rejects changed Inno data after ready')
    finally: uninstall_data.write_bytes(saved_data)

    parent=launch_parent();work,path=make_job();worker=launch_job(work,path);wait(lambda:(work/'ready').exists())
    (work/'inno-uninstall.log').mkdir();(work/'commit').write_bytes(b'\x01')
    (data/'exit-parent').write_text('exit');parent.wait(timeout=10)
    check(worker.wait(timeout=10)!=0 and (directory/'桌面伙伴.exe').exists(),'occupied Inno log destination rejects before launching uninstaller')

    parent=launch_parent();denied_holder=launch_parent('denied')
    work,path=make_job();worker=launch_job(work,path);wait(lambda:(work/'ready').exists())
    (data/'deny-mutex').write_text('deny');wait(lambda:(data/'mutex-denied').exists())
    (work/'commit').write_bytes(b'\x01')
    (data/'exit-parent').write_text('exit');parent.wait(timeout=10)
    check(worker.wait(timeout=10)!=0 and (directory/'桌面伙伴.exe').exists(),'access-denied mutex probe fails closed while owned installation remains')
    (data/'exit-denied').write_text('exit');denied_holder.wait(timeout=10)

    holder=launch_parent('holder');api_tag='api-success';api_parent=launch_parent(api_tag)
    wait(lambda:(data/(api_tag+'-ready')).exists())
    work=Path((data/(api_tag+'-ready')).read_text(encoding='utf-8-sig'))
    (data/(api_tag+'-action')).write_text('go')
    wait(lambda:(data/(api_tag+'-done')).exists())
    check((work/'commit').read_bytes()==b'\x01' and not (work/'abort').exists(),'Dispose after successful actual ticket Commit does not abort')
    (data/('exit-'+api_tag)).write_text('exit');api_parent.wait(timeout=10)
    time.sleep(.6)
    check((data/'holder-unowned').read_text(encoding='utf-8-sig')=='True','fixture proves WaitOne succeeds while the installation object still exists')
    check(not (work/'result.json').exists() and (directory/'桌面伙伴.exe').exists(),'unowned installation mutex OBJECT still blocks after exact parent exit')
    (data/'exit-holder').write_text('exit');holder.wait(timeout=10)
    wait(lambda:(work/'result.json').exists(),45)
    check(json.loads((work/'result.json').read_text(encoding='utf-8-sig'))['Success'],'real silent Inno uninstall starts only after final mutex handle disappears')
    check(not (directory/'桌面伙伴.exe').exists(),'real Inno removed installed executable')
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key_path): removed=False
    except FileNotFoundError: removed=True
    check(removed,'real Inno removed only its unique fixture registration')
    check({name:digest(data/name) for name in sentinels}==before,'settings history images roles relations and receipts preserved byte-for-byte')
    result=json.loads((work/'result.json').read_text(encoding='utf-8-sig'))
    check(result['Success'],'helper persisted actual uninstall success receipt')
finally:
    for process,tag in ((parent,'parent'),(holder,'holder'),(api_parent,api_tag),(denied_holder,'denied')):
        if process is not None and process.poll() is None:
            (data/(tag+'-action')).write_text('go')
            (data/('exit-'+tag)).write_text('exit');process.wait(timeout=10)
    if worker is not None and worker.poll() is None:
        (work/'abort').write_text('abort');worker.wait(timeout=10)
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key_path): registered=True
    except FileNotFoundError: registered=False
    if registered and (directory/'unins000.exe').exists():
        run([directory/'unins000.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'])
print('Uninstall handoff smoke: %d passed; owned evidence: %s' % (passed,ROOT),flush=True)
