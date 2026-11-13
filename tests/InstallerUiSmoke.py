"""Compile and drive the actual installer in a unique, owned native fixture.

The production script is copied with unique AppId, mutex, Run key and shortcut
paths. These substitutions live only here; no fixture define enters release code.
All setup windows run on an inactive Windows desktop, never the user's desktop.
"""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes as W
import json
import hashlib
import os
import re
import shutil
import subprocess
import time
import uuid
import winreg

parser=argparse.ArgumentParser()
parser.add_argument('--compiler',type=Path,default=Path(os.environ.get('DESKPET_TEST_INNO','work/tools/inno/ISCC.exe')))
parser.add_argument('--mutate-hidden-dir',action='store_true',help='Mutation check: restore the old reinstall path-hiding bug in the owned copy only.')
parser.add_argument('--mutate-no-previous-dir',action='store_true',help='Mutation check: stop reusing the previous directory in the owned copy only.')
parser.add_argument('--finish-regressions-only',action='store_true',help='Run the focused completion state/ownership regressions only.')
args=parser.parse_args()
PROJECT=Path(__file__).resolve().parents[1]
ROOT=PROJECT/'work'/('installer-ui-'+uuid.uuid4().hex)
ROOT.mkdir(parents=True)
IDENTITY=str(uuid.uuid4()).upper()
RUN_KEY='Software\\DeskPet.Tests\\InstallerUi\\'+IDENTITY
APP_KEY='Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{'+IDENTITY+'}_is1'
DESKTOP=ROOT/'desktop';DESKTOP.mkdir()
SHORTCUT=DESKTOP/'桌面伙伴.lnk'
INSTALL=ROOT/'中文 install'
DEFAULT_INSTALL=ROOT/'default install'
GUARD_PROBE=PROJECT/'work'/('installer-guard-probe-'+uuid.uuid4().hex)
user=ctypes.WinDLL('user32',use_last_error=True)
user.SetProcessDPIAware()
user.CreateDesktopW.argtypes=[W.LPCWSTR,ctypes.c_void_p,ctypes.c_void_p,W.DWORD,W.DWORD,ctypes.c_void_p]
user.CreateDesktopW.restype=ctypes.c_void_p
desktop_name='DeskPet-InstallerUi-'+uuid.uuid4().hex
desktop=user.CreateDesktopW(desktop_name,None,None,0,0x10000000,None)
assert desktop,ctypes.WinError(ctypes.get_last_error())
user.SetThreadDesktop.argtypes=[ctypes.c_void_p]
assert user.SetThreadDesktop(desktop),ctypes.WinError(ctypes.get_last_error())
ENUM=ctypes.WINFUNCTYPE(W.BOOL,W.HWND,W.LPARAM)
user.EnumDesktopWindows.argtypes=[ctypes.c_void_p,ENUM,W.LPARAM]
user.EnumWindows.argtypes=[ENUM,W.LPARAM]
user.EnumChildWindows.argtypes=[W.HWND,ENUM,W.LPARAM]
user.SendMessageW.argtypes=[W.HWND,W.UINT,W.WPARAM,W.LPARAM]
user.SendMessageW.restype=W.LPARAM
user.PostMessageW.argtypes=[W.HWND,W.UINT,W.WPARAM,W.LPARAM]
user.GetWindowTextW.argtypes=[W.HWND,W.LPWSTR,ctypes.c_int]
user.GetClassNameW.argtypes=[W.HWND,W.LPWSTR,ctypes.c_int]
user.IsWindowVisible.argtypes=[W.HWND]
user.IsWindowEnabled.argtypes=[W.HWND]
user.GetDlgCtrlID.argtypes=[W.HWND]
user.IsWindow.argtypes=[W.HWND]
passed=0
processes=[]
kernel=ctypes.WinDLL('kernel32',use_last_error=True)

class StartupInfo(ctypes.Structure):
    _fields_=[('cb',W.DWORD),('reserved',W.LPWSTR),('desktop',W.LPWSTR),('title',W.LPWSTR),
        ('x',W.DWORD),('y',W.DWORD),('xsize',W.DWORD),('ysize',W.DWORD),('xchars',W.DWORD),('ychars',W.DWORD),
        ('fill',W.DWORD),('flags',W.DWORD),('show',W.WORD),('reserved_size',W.WORD),('reserved2',ctypes.c_void_p),
        ('stdin',W.HANDLE),('stdout',W.HANDLE),('stderr',W.HANDLE)]

class ProcessInfo(ctypes.Structure):
    _fields_=[('process',W.HANDLE),('thread',W.HANDLE),('pid',W.DWORD),('tid',W.DWORD)]

kernel.CreateProcessW.argtypes=[W.LPCWSTR,W.LPWSTR,ctypes.c_void_p,ctypes.c_void_p,W.BOOL,W.DWORD,ctypes.c_void_p,W.LPCWSTR,ctypes.POINTER(StartupInfo),ctypes.POINTER(ProcessInfo)]
kernel.WaitForSingleObject.argtypes=[W.HANDLE,W.DWORD]
kernel.GetExitCodeProcess.argtypes=[W.HANDLE,ctypes.POINTER(W.DWORD)]
kernel.TerminateProcess.argtypes=[W.HANDLE,W.UINT]
kernel.CloseHandle.argtypes=[W.HANDLE]

class NativeProcess:
    def __init__(self,info):self.info=info;self.pid=info.pid
    def poll(self):
        code=W.DWORD();kernel.GetExitCodeProcess(self.info.process,ctypes.byref(code))
        return None if code.value==259 else code.value
    def wait(self,timeout=30):
        if kernel.WaitForSingleObject(self.info.process,int(timeout*1000))!=0:raise TimeoutError('native installer timeout')
        return self.poll()
    def kill(self):kernel.TerminateProcess(self.info.process,1)

def check(condition,label):
    global passed
    assert condition,label
    passed+=1;print('PASS '+label,flush=True)

def wait(predicate,label,seconds=30):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        result=predicate()
        if result:return result
        time.sleep(.04)
    raise AssertionError('timeout: '+label+' windows='+repr([(classname(w),text(w)) for w in windows()])+' processes='+repr([p.poll() for p in processes]))

def text(hwnd):
    buf=ctypes.create_unicode_buffer(2048);user.SendMessageW(hwnd,0xD,len(buf),ctypes.cast(buf,ctypes.c_void_p).value);return buf.value

def classname(hwnd):
    buf=ctypes.create_unicode_buffer(256);user.GetClassNameW(hwnd,buf,len(buf));return buf.value

def windows():
    result=[]
    @ENUM
    def collect(hwnd,unused):
        result.append(hwnd)
        return True
    user.EnumWindows(collect,0)
    return result

def controls(hwnd):
    result=[]
    @ENUM
    def collect(child,unused):
        if user.IsWindowVisible(child):result.append(child)
        return True
    user.EnumChildWindows(hwnd,collect,0)
    return result

def find(hwnd,caption):
    return next((c for c in controls(hwnd) if text(c).replace('&','').strip()==caption),None)

def click(hwnd):
    assert hwnd,'missing native button'
    user.PostMessageW(hwnd,0x201,1,5|(5<<16))
    user.PostMessageW(hwnd,0x202,0,5|(5<<16))

def set_text(hwnd,value):
    buffer=ctypes.create_unicode_buffer(value)
    user.SendMessageW(hwnd,0xC,0,ctypes.cast(buffer,ctypes.c_void_p).value)

def keyboard_space(hwnd):
    user.GetWindowThreadProcessId.argtypes=[W.HWND,ctypes.POINTER(W.DWORD)]
    user.AttachThreadInput.argtypes=[W.DWORD,W.DWORD,W.BOOL]
    user.SetFocus.argtypes=[W.HWND];user.SetFocus.restype=W.HWND
    owner=user.GetWindowThreadProcessId(hwnd,None);caller=kernel.GetCurrentThreadId()
    assert user.AttachThreadInput(caller,owner,True)
    try:
        user.SetFocus(hwnd)
        user.PostMessageW(hwnd,0x100,0x20,0);user.PostMessageW(hwnd,0x101,0x20,0)
        time.sleep(.15)
    finally:user.AttachThreadInput(caller,owner,False)

def start(command,*,allow_reuse_previous=False,guard_probe=False):
    executable=Path(command[0]).resolve()
    assert executable.is_relative_to(ROOT.resolve()),'refusing to execute a fixture program outside its owned root'
    if executable.name in ('installer.exe','legacy-installer.exe'):
        targets=[str(value)[5:] for value in command[1:] if str(value).upper().startswith('/DIR=')]
        if guard_probe:
            assert len(targets)==1 and Path(targets[0]).resolve()==GUARD_PROBE.resolve() and not GUARD_PROBE.exists()
            assert GUARD_PROBE.resolve().parent==(PROJECT/'work').resolve(),'negative guard probe must remain inside repository work'
        elif allow_reuse_previous:
            assert not targets,'reuse-previous fixture must not override /DIR'
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER,APP_KEY) as key:
                previous=winreg.QueryValueEx(key,'Inno Setup: App Path')[0]
            assert Path(previous).resolve()==INSTALL.resolve() and (INSTALL/'桌面伙伴.exe').is_file(),'no-/DIR mode requires the exact existing owned fixture registration'
        else:
            assert len(targets)==1 and Path(targets[0]).resolve().is_relative_to(ROOT.resolve()),'every ordinary installer launch requires one explicit owned /DIR'
    si=StartupInfo();si.cb=ctypes.sizeof(si);si.desktop=desktop_name
    info=ProcessInfo();line=ctypes.create_unicode_buffer(subprocess.list2cmdline(list(map(str,command))))
    assert kernel.CreateProcessW(None,line,None,None,False,0,None,str(ROOT),ctypes.byref(si),ctypes.byref(info)),ctypes.WinError(ctypes.get_last_error())
    kernel.CloseHandle(info.thread);p=NativeProcess(info)
    processes.append(p);return p

def setup(*flags,allow_reuse_previous=False):
    directory=[] if allow_reuse_previous or any(str(flag).upper().startswith('/DIR=') for flag in flags) else ['/DIR='+str(INSTALL)]
    return start([ROOT/'installer.exe','/SP-','/NORESTART',*directory,'/LOG='+str(ROOT/('setup-'+str(len(processes))+'.log')),'/GROUP=DeskPetFixture-'+IDENTITY,*flags],allow_reuse_previous=allow_reuse_previous)

def wizard():
    return next((w for w in windows() if classname(w)=='TWizardForm'),None)

def path_page():
    # BorderStyle changes recreate the early wizard HWND during initialization.
    # Acquire the ready window, never retain that transient pre-layout handle.
    def ready():
        w=wizard()
        return w if w and find(w,'恢复默认') else None
    try:w=wait(ready,'always-visible editable directory page')
    except Exception:
        w=wizard()
        if w:snapshot(w,'failed-page');print('CONTROLS',[(classname(c),text(c)) for c in controls(w)],flush=True)
        raise
    return w

def path_edit(w):
    return next(c for c in controls(w) if classname(c)=='TNewPathEdit')

def snapshot(w,name):
    time.sleep(.2)
    from PIL import Image
    rect=W.RECT();user.GetWindowRect.argtypes=[W.HWND,ctypes.POINTER(W.RECT)];user.GetWindowRect(w,ctypes.byref(rect))
    width,height=rect.right-rect.left,rect.bottom-rect.top
    assert width>0 and height>0,'cannot capture an expired or not-yet-sized native window'
    gdi=ctypes.WinDLL('gdi32');user.GetWindowDC.restype=ctypes.c_void_p;user.GetWindowDC.argtypes=[W.HWND]
    gdi.CreateCompatibleDC.argtypes=[ctypes.c_void_p];gdi.CreateCompatibleDC.restype=ctypes.c_void_p
    gdi.CreateCompatibleBitmap.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_int];gdi.CreateCompatibleBitmap.restype=ctypes.c_void_p
    gdi.SelectObject.argtypes=[ctypes.c_void_p,ctypes.c_void_p];gdi.SelectObject.restype=ctypes.c_void_p
    dc=user.GetWindowDC(w);mem=gdi.CreateCompatibleDC(dc);bmp=gdi.CreateCompatibleBitmap(dc,width,height);old=gdi.SelectObject(mem,bmp)
    user.PrintWindow.argtypes=[W.HWND,ctypes.c_void_p,W.UINT];user.PrintWindow(w,mem,2)
    class Header(ctypes.Structure):
        _fields_=[('size',W.DWORD),('width',W.LONG),('height',W.LONG),('planes',W.WORD),('bits',W.WORD),('compression',W.DWORD),('image',W.DWORD),('x',W.LONG),('y',W.LONG),('used',W.DWORD),('important',W.DWORD)]
    header=Header(40,width,-height,1,32,0,0,0,0,0,0);data=ctypes.create_string_buffer(width*height*4)
    gdi.GetDIBits.argtypes=[ctypes.c_void_p,ctypes.c_void_p,W.UINT,W.UINT,ctypes.c_void_p,ctypes.c_void_p,W.UINT]
    gdi.GetDIBits(mem,bmp,0,height,data,ctypes.byref(header),0)
    Image.frombuffer('RGB',(width,height),data,'raw','BGRX',0,1).save(ROOT/(name+'.png'))
    gdi.SelectObject(mem,old)
    gdi.DeleteObject.argtypes=[ctypes.c_void_p];gdi.DeleteDC.argtypes=[ctypes.c_void_p];user.ReleaseDC.argtypes=[W.HWND,ctypes.c_void_p]
    gdi.DeleteObject(bmp);gdi.DeleteDC(mem);user.ReleaseDC(w,dc)

def run_value():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:return winreg.QueryValueEx(key,'DafeiyuDeskPet')[0]
    except FileNotFoundError:return None

def registered_uninstaller():
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,APP_KEY) as key:
        command=winreg.QueryValueEx(key,'UninstallString')[0]
    path=Path(command.split('"')[1] if command.startswith('"') else command)
    assert path.resolve().parent==INSTALL.resolve() and re.fullmatch(r'unins\d+\.exe',path.name),'uninstaller must belong to the exact owned installation'
    return path

def finish_page(w):
    return find(w,'完成并启动')

def complete_install(w):
    click(find(w,'开始安装'))
    values=set();captured=False
    end=time.monotonic()+60
    while time.monotonic()<end and not finish_page(w):
        for c in controls(w):
            value=text(c)
            if classname(c)=='TNewStaticText' and value.isdigit():
                values.add(int(value))
                if 0<int(value)<100 and not captured:snapshot(w,'progress');captured=True
        time.sleep(.015)
    wait(lambda:finish_page(w),'completion page')
    return values

def compile_fixture():
    script=ROOT/'script';shutil.copytree(PROJECT/'installer',script)
    shutil.copytree(PROJECT/'assets',ROOT/'assets',ignore=shutil.ignore_patterns('*'))
    # Only the two tracked assets referenced outside the installer folder.
    for name in ('pet.ico','deskpet-logo.png'):shutil.copy2(PROJECT/'assets'/name,ROOT/'assets'/name)
    for file in script.glob('*.iss'):
        content=file.read_text(encoding='utf-8-sig')
        content=content.replace(r'DefaultDirName={localappdata}\Programs\EbaoDeskPet','DefaultDirName='+str(DEFAULT_INSTALL))
        # Bound the native reset action too; no fixture control points at the
        # real user's default installation directory.
        content=content.replace(r'{localappdata}\Programs\EbaoDeskPet',str(DEFAULT_INSTALL))
        if file.name=='DeskPet.iss':
            assert 'DefaultDirName='+str(DEFAULT_INSTALL) in content,'fixture default directory must be replaced before compilation'
        if file.name=='VersionGuard.iss':
            # Last boundary before native directory creation/file retirement.
            # Even a UI edit or future omitted flag cannot escape this fixture.
            needle="  Result := ''; Path := ExpandConstant('{app}\\桌面伙伴.exe');"
            assert content.count(needle)==1,'native prepare-to-install safety injection must match exactly once'
            prefix=str(ROOT.resolve()).replace("'","''")+'\\'
            guard="  if CompareText(Copy(AddBackslash(ExpandFileName(WizardDirValue)), 1, Length('"+prefix+"')), '"+prefix+"') <> 0 then begin Result := 'Fixture refuses installation outside its owned root.'; Exit; end;\n"
            content=content.replace(needle,guard+needle)
        if args.mutate_hidden_dir:content=content.replace('DisableDirPage=no','DisableDirPage=auto')
        if args.mutate_no_previous_dir:content=content.replace('UsePreviousAppDir=yes','UsePreviousAppDir=no')
        content=content.replace('3B63C162-076E-4B2F-BDCC-2572664875A9',IDENTITY)
        content=content.replace(r'Local\Dafeiyu.DeskPet.Installation','Local\\DeskPet.InstallerUi.'+IDENTITY)
        content=content.replace(r'Software\Microsoft\Windows\CurrentVersion\Run',RUN_KEY)
        content=content.replace('{autodesktop}',str(DESKTOP)).replace('{userprograms}',str(ROOT/'programs')).replace('{group}',str(ROOT/'group'))
        file.write_text(content,encoding='utf-8-sig')
    source=ROOT/'Fixture.cs'
    source.write_text('[assembly:System.Reflection.AssemblyVersion("1.0.4.0")][assembly:System.Reflection.AssemblyInformationalVersion("1.0.4-dev.1")]'+'class Fixture {static void Main(){System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,"launch.txt"),"launch\\n");}}',encoding='utf-8')
    payload=ROOT/'runtime';payload.mkdir()
    csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(csc),'/nologo','/target:winexe','/out:'+str(payload/'桌面伙伴.exe'),str(source)],check=True,capture_output=True)
    (payload/'payload.bin').write_bytes(os.urandom(24*1024*1024))
    include=ROOT/'runtime.iss'
    include.write_text('Source: "'+str(payload/'*')+'"; DestDir: "{app}"; Flags: ignoreversion\n',encoding='utf-8-sig')
    command=[args.compiler,'/Qp','/DRuntimeDir='+str(payload),'/DRuntimeFiles='+str(include),'/DProductVersion=1.0.4-dev.1','/DInformationalVersion=1.0.4-dev.1','/DAssemblyVersion=1.0.4.0','/DRepositoryUrl=https://example.invalid','/DInstallerBaseName=installer','/DOutputDir='+str(ROOT),script/'DeskPet.iss']
    result=subprocess.run(list(map(str,command)),capture_output=True,timeout=120)
    (ROOT/'compile.log').write_bytes(result.stdout+result.stderr)
    if result.returncode: print((result.stdout+result.stderr).decode('utf-8',errors='replace'),flush=True)
    check(result.returncode==0,'production script compiles with native Inno (see compile.log)')
    legacy=script/'DeskPet-legacy.iss'
    legacy.write_text((script/'DeskPet.iss').read_text(encoding='utf-8-sig').replace('; Flags: uninsneveruninstall',''),encoding='utf-8-sig')
    legacy_command=[str(legacy) if str(value)==str(script/'DeskPet.iss') else str(value).replace('/DInstallerBaseName=installer','/DInstallerBaseName=legacy-installer') for value in command]
    result=subprocess.run(legacy_command,capture_output=True,timeout=120)
    (ROOT/'legacy-compile.log').write_bytes(result.stdout+result.stderr)
    check(result.returncode==0,'legacy unconditional icon-log fixture compiles separately')

def change_link_hotkey():
    command='$s=New-Object -ComObject WScript.Shell;$l=$s.CreateShortcut($env:DESKPET_FIXTURE_LINK);$l.TargetPath=$env:DESKPET_FIXTURE_EXE;$l.WorkingDirectory=$env:DESKPET_FIXTURE_APP;$l.Arguments="";$l.Description="";$l.IconLocation=$env:DESKPET_FIXTURE_EXE+",0";$l.WindowStyle=1;$l.Hotkey="CTRL+ALT+D";$l.Save();if(!$l.Hotkey){throw "Missing hotkey"}'
    si=subprocess.STARTUPINFO();si.dwFlags|=subprocess.STARTF_USESHOWWINDOW;si.wShowWindow=0
    subprocess.run(['powershell.exe','-NoProfile','-Command',command],
        env=dict(os.environ,DESKPET_FIXTURE_LINK=str(SHORTCUT),DESKPET_FIXTURE_EXE=str(INSTALL/'桌面伙伴.exe'),DESKPET_FIXTURE_APP=str(INSTALL)),
        startupinfo=si,check=True,capture_output=True,timeout=15)
    return SHORTCUT.read_bytes()

def finish_regressions():
    results=[]
    def record(condition,label):
        results.append((bool(condition),label))
        print(('PASS' if condition else 'FAIL')+' regression: '+label,flush=True)
    def uninstall():
        executable=registered_uninstaller()
        p=start([executable,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'])
        check(p.wait(timeout=30)==0,'focused fixture uninstalls normally')
        wait(lambda:not executable.exists(),'focused uninstaller cleanup')

    # Real inherited Inno task state, followed by the app updater's exact flags
    # (including protocol 2 identity) with no task override whatsoever.
    p=start([ROOT/'legacy-installer.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/DIR='+str(INSTALL),'/TASKS=desktopicon'])
    check(p.wait(timeout=60)==0 and SHORTCUT.exists(),'legacy fixture selects and creates desktopicon')
    p=setup(allow_reuse_previous=True);w=path_page();complete_install(w)
    check(user.SendMessageW(find(w,'创建桌面快捷方式'),0xF0,0,0)==1,'inherited desktop option starts checked')
    click(find(w,'创建桌面快捷方式'));click(find(w,'完成，稍后启动'))
    check(p.wait(timeout=20)==0 and not SHORTCUT.exists(),'interactive Finish opts out of inherited desktop link')
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,APP_KEY) as key:selected=winreg.QueryValueEx(key,'Inno Setup: Selected Tasks')[0]
    check('desktopicon' in selected,'actual historical Inno desktop task remains inherited')
    p=setup('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTARTAPPLICATIONS','/NOCLOSEAPPLICATIONS','/EXPECTEDBUILD=1.0.4-dev.1')
    check(p.wait(timeout=60)==0,'ordinary updater flags without TASKS succeed')
    record(not SHORTCUT.exists(),'ordinary silent update preserves interactive desktop opt-out')
    uninstall();wait(lambda:not INSTALL.exists(),'fresh-install reset for partial Finish case')

    # Let desktop creation succeed, then fail startup ownership validation after
    # Finish has loaded its initial state. The real foreign-value guard supplies
    # the failure without test hooks in production code or registry ACL changes.
    p=setup();w=path_page();complete_install(w)
    desktop_box=find(w,'创建桌面快捷方式');startup_box=find(w,'开机时启动桌面伙伴')
    click(startup_box)
    changed_command='"'+str(ROOT/'changed startup owner.exe')+'" --startup'
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:winreg.SetValueEx(key,'DafeiyuDeskPet',0,winreg.REG_SZ,changed_command)
    click(find(w,'完成，稍后启动'))
    dialog=wait(lambda:next((d for d in windows() if classname(d)=='#32770'),None),'startup ownership failure after shortcut creation')
    check(p.poll() is None and SHORTCUT.exists() and run_value()==changed_command,'real startup ownership failure follows successful desktop creation')
    user.PostMessageW(dialog,0x10,0,0);wait(lambda:not user.IsWindow(dialog),'partial Finish error dismissal')
    click(desktop_box);click(startup_box);click(find(w,'完成，稍后启动'))
    check(p.wait(timeout=20)==0,'revised unchecked Finish choices succeed')
    record(not SHORTCUT.exists() and run_value()==changed_command,'unchecked retry removes only its own partially created link and preserves foreign startup')
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY,0,winreg.KEY_SET_VALUE) as key:winreg.DeleteValue(key,'DafeiyuDeskPet')

    # Only Hotkey differs from installed defaults. Exercise explicit silent
    # update, interactive opt-out, and native uninstall as independent checks.
    p=setup('/VERYSILENT','/SUPPRESSMSGBOXES','/TASKS=desktopicon');check(p.wait(timeout=60)==0,'explicit desktop creation remains supported')
    before=change_link_hotkey()
    p=setup('/VERYSILENT','/SUPPRESSMSGBOXES','/TASKS=desktopicon');check(p.wait(timeout=60)==0,'hotkey fixture silent update succeeds')
    record(SHORTCUT.exists() and SHORTCUT.read_bytes()==before,'silent explicit task preserves a same-target hotkey-modified link')
    before=change_link_hotkey()
    p=setup();w=path_page();complete_install(w)
    desktop_box=find(w,'创建桌面快捷方式')
    record(not user.IsWindowEnabled(desktop_box),'Finish protects a same-target shortcut with a real COM hotkey')
    if user.IsWindowEnabled(desktop_box) and user.SendMessageW(desktop_box,0xF0,0,0)==1:click(desktop_box)
    click(find(w,'完成，稍后启动'));check(p.wait(timeout=20)==0,'hotkey fixture finishes normally')
    record(SHORTCUT.exists() and SHORTCUT.read_bytes()==before,'Finish does not remove the user hotkey shortcut')
    before=change_link_hotkey();uninstall()
    record(SHORTCUT.exists() and SHORTCUT.read_bytes()==before,'uninstall preserves a same-target hotkey-modified link byte for byte')
    if SHORTCUT.exists():SHORTCUT.unlink()
    wait(lambda:not INSTALL.exists(),'focused fixture directory cleanup')
    for condition,label in results:check(condition,label)
    (ROOT/'finish-regressions.json').write_text(json.dumps({'passed':len(results),'results':results},ensure_ascii=False,indent=2),encoding='utf-8')

try:
    compile_fixture()
    finish_regressions()
    if args.finish_regressions_only:raise SystemExit(0)
    p=start([ROOT/'installer.exe','/SP-','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/DIR='+str(GUARD_PROBE),'/LOG='+str(ROOT/'target-guard.log')],guard_probe=True)
    check(p.wait(timeout=30)!=0 and not GUARD_PROBE.exists(),'native PrepareToInstall guard rejects a repository sibling target before directory creation')
    p=setup('/DIR='+str(INSTALL));w=path_page()
    set_text(path_edit(w),r'D:\Apps\EbaoDeskPet');user.SendMessageW(path_edit(w),0xB1,-1,-1);snapshot(w,'path');set_text(path_edit(w),str(INSTALL))
    check(text(path_edit(w))==str(INSTALL),'native editable path accepts Chinese characters and spaces')
    set_text(path_edit(w),str(ROOT/'changed-before-reset'))
    keyboard_space(find(w,'恢复默认'))
    check(text(path_edit(w))==str(DEFAULT_INSTALL),'native focused button supports Space and bounded fixture reset-to-default')
    set_text(path_edit(w),str(INSTALL))
    check(not SHORTCUT.exists() and run_value() is None,'path selection has no completion side effects')
    click(find(w,'×'))
    dialog=wait(lambda:next((d for d in windows() if classname(d)=='#32770'),None),'cancel confirmation')
    yes=wait(lambda:next((c for c in controls(dialog) if user.GetDlgCtrlID(c)==6),None),'native cancellation confirmation button')
    click(yes)
    check(p.wait(timeout=15)!=0 and not INSTALL.exists() and not SHORTCUT.exists() and run_value() is None,'cancelling before install applies no options')
    p=setup('/DIR='+str(ROOT/'initial'));w=path_page()
    edit=path_edit(w);value=ctypes.create_unicode_buffer(str(INSTALL));user.SendMessageW(edit,0xC,0,ctypes.cast(value,ctypes.c_void_p).value)
    check(text(edit)==str(INSTALL),'native directory input is editable: '+repr(text(edit)))
    values=complete_install(w);snapshot(w,'complete')
    check(not SHORTCUT.exists() and run_value() is None,'completion choices remain unapplied before Finish')
    shortcut_box=find(w,'创建桌面快捷方式');startup_box=find(w,'开机时启动桌面伙伴')
    check(user.SendMessageW(shortcut_box,0xF0,0,0)==1 and user.SendMessageW(startup_box,0xF0,0,0)==0,'new install defaults to shortcut on and startup off')
    # A legitimate filesystem failure must remain on Finish and permit retry.
    SHORTCUT.mkdir();click(startup_box);click(find(w,'完成，稍后启动'))
    dialog=wait(lambda:next((d for d in windows() if classname(d)=='#32770'),None),'completion failure is surfaced')
    check(p.poll() is None and run_value() is None,'failed completion applies no startup setting and keeps wizard open')
    snapshot(dialog,'completion-error')
    user.PostMessageW(dialog,0x10,0,0)
    wait(lambda:not user.IsWindow(dialog),'completion error acknowledgement');SHORTCUT.rmdir()
    click(find(w,'完成，稍后启动'));check(p.wait(timeout=20)==0,'Finish later retries successfully')
    check(SHORTCUT.exists() and run_value()=='"'+str(INSTALL/'桌面伙伴.exe')+'" --startup','Finish applies real shortcut and quoted startup command')
    check(not (INSTALL/'launch.txt').exists(),'Finish later does not launch')
    settings=INSTALL/'data/settings.json';settings.parent.mkdir();settings.write_bytes(b'{"Volume":27,"unrelated":"preserve"}')
    settings_before=settings.read_bytes()
    p=setup(allow_reuse_previous=True);w=path_page()
    check(text(path_edit(w))==str(INSTALL),'reinstall still shows and reuses previous directory')
    complete_install(w)
    check(user.SendMessageW(find(w,'创建桌面快捷方式'),0xF0,0,0)==1 and user.SendMessageW(find(w,'开机时启动桌面伙伴'),0xF0,0,0)==1,'upgrade preserves owned shortcut and startup selections')
    click(find(w,'创建桌面快捷方式'));click(find(w,'开机时启动桌面伙伴'));click(find(w,'完成并启动'));check(p.wait(timeout=20)==0,'Finish and launch succeeds')
    wait(lambda:(INSTALL/'launch.txt').exists(),'one launch')
    check((INSTALL/'launch.txt').read_text().splitlines()==['launch'] and not SHORTCUT.exists() and run_value() is None,'explicit unchecked owned options remove only own settings and launch once')
    p=start([ROOT/'legacy-installer.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/DIR='+str(INSTALL),'/TASKS=desktopicon','/LOG='+str(ROOT/'legacy-install.log')])
    check(p.wait(timeout=60)==0 and SHORTCUT.exists(),'genuine old unconditional desktop icon entry is present in uninstall log')
    p=setup('/VERYSILENT','/SUPPRESSMSGBOXES','/TASKS=desktopicon')
    check(p.wait(timeout=60)==0 and SHORTCUT.exists() and run_value() is None,'silent /TASKS=desktopicon remains compatible and never enables startup')
    check((INSTALL/'launch.txt').read_text().splitlines()==['launch'],'silent setup never launches')
    foreign='"'+str(ROOT/'other app.exe')+'" --startup'
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:winreg.SetValueEx(key,'DafeiyuDeskPet',0,winreg.REG_SZ,foreign)
    # Preserve a same-target link whose description the user modified.
    command='$s=New-Object -ComObject WScript.Shell;$l=$s.CreateShortcut($env:DESKPET_FIXTURE_LINK);$l.Description="custom description";$l.Save()'
    si=subprocess.STARTUPINFO();si.dwFlags|=subprocess.STARTF_USESHOWWINDOW;si.wShowWindow=0
    subprocess.run(['powershell.exe','-NoProfile','-Command',command],env=dict(os.environ,DESKPET_FIXTURE_LINK=str(SHORTCUT)),startupinfo=si,check=True,capture_output=True,timeout=15)
    shortcut_before=SHORTCUT.read_bytes()
    p=setup('/VERYSILENT','/SUPPRESSMSGBOXES','/TASKS=desktopicon')
    check(p.wait(timeout=60)==0 and run_value()==foreign and SHORTCUT.read_bytes()==shortcut_before,'silent explicit shortcut task also preserves modified link and foreign startup')
    p=setup();w=path_page();complete_install(w)
    check(not user.IsWindowEnabled(find(w,'创建桌面快捷方式')) and not user.IsWindowEnabled(find(w,'开机时启动桌面伙伴')),'foreign startup and modified shortcut options are protected')
    click(find(w,'完成，稍后启动'));check(p.wait(timeout=20)==0,'protected upgrade finishes normally')
    check(run_value()==foreign and SHORTCUT.read_bytes()==shortcut_before,'Finish preserves foreign Run value and user-modified shortcut byte for byte')
    uninstaller=registered_uninstaller()
    p=start([uninstaller,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART']);check(p.wait(timeout=30)==0,'actual owned uninstaller succeeds')
    wait(lambda:not (INSTALL/'桌面伙伴.exe').exists(),'uninstall')
    wait(lambda:not uninstaller.exists(),'uninstaller self-removal before reinstall')
    check(run_value()==foreign and SHORTCUT.read_bytes()==shortcut_before,'uninstall preserves foreign startup and user-modified shortcut')
    check(settings.read_bytes()==settings_before,'upgrade and uninstall preserve settings bytes')
    SHORTCUT.unlink()
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,RUN_KEY,0,winreg.KEY_SET_VALUE) as key:winreg.DeleteValue(key,'DafeiyuDeskPet')
    p=setup('/VERYSILENT','/SUPPRESSMSGBOXES','/TASKS=desktopicon');check(p.wait(timeout=60)==0,'owned shortcut uninstall fixture installs')
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER,RUN_KEY) as key:winreg.SetValueEx(key,'DafeiyuDeskPet',0,winreg.REG_SZ,'"'+str(INSTALL/'桌面伙伴.exe')+'" --startup')
    p=start([registered_uninstaller(),'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART']);check(p.wait(timeout=30)==0,'owned shortcut fixture uninstalls')
    wait(lambda:not SHORTCUT.exists(),'owned desktop shortcut cleanup')
    check(not SHORTCUT.exists() and run_value() is None,'actual uninstall removes exactly owned shortcut and startup')
    check(any(0<v<100 for v in values),'native percentage shows actual intermediate installation progress')
    (ROOT/'result.json').write_text(json.dumps({'passed':passed,'progress_values':sorted(values),'root':str(ROOT)},ensure_ascii=False,indent=2),encoding='utf-8')
    print('Installer UI smoke: '+str(passed)+' passed; evidence '+str(ROOT),flush=True)
finally:
    for p in processes:
        if p.poll() is None:p.kill();p.wait()
    try:uninstaller=registered_uninstaller()
    except FileNotFoundError:uninstaller=None
    if uninstaller is not None and uninstaller.exists():
        p=start([uninstaller,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART']);p.wait(timeout=30)
    for key in (RUN_KEY,):
        try:winreg.DeleteKey(winreg.HKEY_CURRENT_USER,key)
        except FileNotFoundError:pass
    user.CloseDesktop.argtypes=[ctypes.c_void_p];user.CloseDesktop(desktop)
    for p in processes:kernel.CloseHandle(p.info.process)
