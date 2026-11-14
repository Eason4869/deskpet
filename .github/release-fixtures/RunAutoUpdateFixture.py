"""Repair only the external release fixture; keep the tagged product checkout clean."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

p=argparse.ArgumentParser()
p.add_argument('--source',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--mode',choices=('fixed','diagnose-gap'),default='fixed')
a=p.parse_args();source=a.source.resolve();output=a.output.resolve();output.mkdir(parents=True,exist_ok=True)
expected='112aeb97dc6bf846b0bb0a2878c0973b406f319a'
def git(*args):return subprocess.check_output(['git','-C',str(source),*args],text=True).strip()
assert git('rev-parse','HEAD')==expected and not git('status','--porcelain')
original=source/'tests/AutoUpdateSmoke.cs';text=original.read_text(encoding='utf-8-sig')
start=text.index('                using(var feed=new Feed())\n                {\n                    bool handedOff=false;FileStream settingsLock=null;')
end=text.index('                using(var feed=new Feed())',start+40)
old=text[start:end]
assert old.count('设置写入失败时保留原设置且不启动助手或退出')==1
new='''                using(var feed=new Feed())
                {
                    bool handedOff=false;FileStream settingsLock=null;byte[] lockedBaseline=null;
                    string settingsPath=Path.Combine(pet.Store.DirectoryPath,"settings.json");
                    using(var controller=new UpdateCoordinator(pet,feed,job=>{handedOff=true;return Task.FromResult(true);},ReleaseInfo.DisplayVersion))
                    try
                    {
                        typeof(PetWindow).GetField("updates",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,controller);
                        controller.Changed+=delegate
                        {
                            if(!controller.IsApplying || settingsLock!=null)return;
                            pet.Settings.ReadReplies=false;pet.Store.Save(pet.Settings);
                            settingsLock=new FileStream(settingsPath,FileMode.Open,FileAccess.Read,FileShare.Read);
                            lockedBaseline=File.ReadAllBytes(settingsPath);
                            pet.Settings.ReadReplies=true;
                            Console.WriteLine("SETTINGS LOCK baselineFalse="+!pet.Store.Load().ReadReplies+" pendingTrue="+pet.Settings.ReadReplies);
                        };
                        Task task=controller.CheckAndUpdateAsync(true);Wait(()=>task.IsCompleted,4);
                        // Exercise an ordinary countdown save before the actual fault.
                        pet.SaveSettings();
                        Wait(()=>settingsLock!=null,8);Wait(()=>!controller.IsBusy,4);
                        Console.WriteLine("SETTINGS CONJUNCTS handedOff="+handedOff+" visible="+pet.IsVisible+" status="+controller.Status+" storedReadReplies="+pet.Store.Load().ReadReplies);
                        Check(!handedOff && pet.IsVisible && controller.Status.Contains("设置尚未保存") && !pet.Store.Load().ReadReplies,"设置写入失败时保留原设置且不启动助手或退出");
                        Check(lockedBaseline!=null && lockedBaseline.SequenceEqual(File.ReadAllBytes(settingsPath)),"设置锁生效后的原文件字节保持不变");
                        Check(idleChat.IsEnabled && idleQuick.IsEnabled,"设置保存失败后恢复两个聊天窗口");
                    }
                    finally{if(settingsLock!=null)settingsLock.Dispose();pet.Settings.ReadReplies=false;}
                }
'''
if a.mode=='diagnose-gap':
    new=old.replace('Wait(()=>task.IsCompleted,4);pet.Settings.ReadReplies=true;','Wait(()=>task.IsCompleted,4);pet.Settings.ReadReplies=true;pet.SaveSettings();')
    marker='                        Check(!handedOff && pet.IsVisible && controller.Status.Contains("设置尚未保存") && !pet.Store.Load().ReadReplies,'
    assert new.count(marker)==1
    new=new.replace(marker,'                        Console.WriteLine("SETTINGS CONJUNCTS handedOff="+handedOff+" visible="+pet.IsVisible+" status="+controller.Status+" storedReadReplies="+pet.Store.Load().ReadReplies);\n'+marker)
assert old.count('Check(')==2 and new.count('Check(')==(3 if a.mode=='fixed' else 2)
fixed=text[:start]+new+text[end:]
encoding_marker='            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;'
assert fixed.count(encoding_marker)==1
fixed=fixed.replace(encoding_marker,'            Console.OutputEncoding=new System.Text.UTF8Encoding(false);\n'+encoding_marker)
fixture=output/'AutoUpdateSmoke.cs';fixture.write_text(fixed,encoding='utf-8-sig')
command=['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(source/'Build.ps1'),'-Target','AutoUpdateSmoke','-TestArguments','--compile-only']
with (output/'compile-tagged.log').open('w',encoding='utf-8') as log:subprocess.run(command,cwd=source,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=180)
rsp=(source/'work/AutoUpdateSmoke.rsp').read_text(encoding='utf-8-sig').splitlines()
matches=[i for i,line in enumerate(rsp) if line=='"'+str(original)+'"'];assert len(matches)==1,matches
copied=list(rsp);copied[matches[0]]='"'+str(fixture)+'"'
assert sum(x!=y for x,y in zip(rsp,copied))==1
response=output/'AutoUpdateSmoke.rsp';response.write_text('\n'.join(copied)+'\n',encoding='utf-8-sig')
csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
with (output/'compile-fixed.log').open('w',encoding='utf-8') as log:subprocess.run([str(csc),'@'+str(response)],cwd=source,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=180)
launcher=output/'OwnedDesktop.exe'
subprocess.run([str(csc),'/nologo','/target:exe','/out:'+str(launcher),str(Path(__file__).with_name('OwnedDesktop.cs'))],check=True,timeout=30)
with (output/'test.log').open('w',encoding='utf-8') as log:
    result=subprocess.run([str(launcher),str(source/'work/AutoUpdateSmoke.exe')],cwd=source,stdout=log,stderr=subprocess.STDOUT,timeout=165,creationflags=subprocess.CREATE_NO_WINDOW)
assert git('rev-parse','HEAD')==expected and not git('status','--porcelain')
report=dict(product_commit=expected,infrastructure_commit=os.environ.get('GITHUB_SHA','local'),mode=a.mode,source_fixture_sha256=hashlib.sha256(text.encode()).hexdigest(),external_fixture_sha256=hashlib.sha256(fixture.read_bytes()).hexdigest(),changed_compiler_arguments=1,product_tree_clean=True,exit_code=result.returncode)
(output/'result.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print((output/'test.log').read_text(encoding='utf-8'),flush=True)
print(json.dumps(report),flush=True)
raise SystemExit(result.returncode)
