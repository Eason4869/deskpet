using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Dafeiyu.Tests
{
    public static class UpdateApplyTests
    {
        static int passed;static Type engine,jobType;static string fixtureDirectory,oldHash;
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static object Call(string name,params object[] args){try{return engine.GetMethod(name).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        static void Reject(Action action,string name){try{action();}catch(InvalidOperationException){Check(true,name);return;}throw new Exception(name+" was accepted");}
        static string Hash(string path){using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        static void Field(object job,string name,object value){jobType.GetField(name).SetValue(job,value);}
        static object Job(string directory,string work,string package,string mode)
        {
            object job=Activator.CreateInstance(jobType);Field(job,"InstallationDirectory",directory);Field(job,"WorkDirectory",work);Field(job,"PackagePath",package);Field(job,"Mode",mode);
            Field(job,"ProtocolVersion",2);Field(job,"InstalledAssemblyVersion","0.0.1.0");Field(job,"TargetAssemblyVersion",ReleaseVersion.Parse(ReleaseInfo.DisplayVersion).AssemblyBase.ToString());Field(job,"TargetBuildId",System.Diagnostics.FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion);
            Field(job,"InstalledVersion","0.0.1");Field(job,"TargetVersion",ReleaseInfo.DisplayVersion);Field(job,"Size",new FileInfo(package).Length);Field(job,"Digest",Hash(package));
            Field(job,"AssetUrl",ReleaseInfo.RepositoryUrl+"/releases/download/v"+ReleaseInfo.DisplayVersion+"/"+Path.GetFileName(package));return job;
        }
        static void Archive(string path,string badEntry)
        {
            using(var zip=new ZipArchive(File.Create(path),ZipArchiveMode.Create))
            {
                foreach(string name in new[]{"桌面伙伴.exe","桌面伙伴.exe.config","DeskPet.Update.exe","README.md","docs/installation.md","templates/README.md"})
                {
                    var entry=zip.CreateEntry("DafeiyuDeskPet/"+name);using(var output=entry.Open())
                    {if(name.EndsWith(".exe"))using(var input=File.OpenRead(Assembly.GetExecutingAssembly().Location))input.CopyTo(output);else{byte[] data=System.Text.Encoding.UTF8.GetBytes("new "+name);output.Write(data,0,data.Length);}}
                }
                if(badEntry!=null){var entry=zip.CreateEntry(badEntry);using(var output=entry.Open())output.WriteByte(42);}
            }
        }
        public static int Main()
        {
            try
            {
                engine=typeof(PetSettings).Assembly.GetType("Dafeiyu.UpdateInstall");Check(engine!=null,"提供退出后的更新执行器");
                jobType=typeof(PetSettings).Assembly.GetType("Dafeiyu.UpdateJob");
                Check(new FileInfo(Assembly.GetExecutingAssembly().Location).Length<10L*1024*1024,"升级夹具不携带伙伴图像资源");
                string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"apply-tests",Guid.NewGuid().ToString("N"),"中文 安装目录");Directory.CreateDirectory(directory);
                fixtureDirectory=directory;
                string exe=Path.Combine(directory,"桌面伙伴.exe");string source=Path.Combine(directory,"old.cs");File.WriteAllText(source,"[assembly:System.Reflection.AssemblyVersion(\"0.0.1.0\")][assembly:System.Reflection.AssemblyInformationalVersion(\"0.0.1.0\")] class Old {static void Main(){}}");
                using(var compiler=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"csc.exe"),"/nologo /out:"+UpdateInstall.Quote(exe)+" "+UpdateInstall.Quote(source)){UseShellExecute=false,CreateNoWindow=true})){compiler.WaitForExit();if(compiler.ExitCode!=0)throw new Exception("old PE fixture failed");}oldHash=Hash(exe);File.WriteAllText(Path.Combine(directory,"README.md"),"old readme");
                string data=Path.Combine(directory,"data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"settings.json"),"private config sentinel");
                string work=Path.Combine(data,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
                string package=Path.Combine(work,"DeskPet-Windows-"+ReleaseInfo.DisplayVersion+".zip");Archive(package,null);object job=Job(directory,work,package,"portable");
                Call("Validate",job);Check(true,"中文空格路径及完整的受信发行包通过校验");
                Field(job,"Digest",new string('0',64));Reject(()=>Call("Validate",job),"篡改或 SHA256 不符不得进入安装");Field(job,"Digest",Hash(package));
                Field(job,"Size",1L);Reject(()=>Call("Validate",job),"长度不符不得进入安装");Field(job,"Size",new FileInfo(package).Length);
                Field(job,"AssetUrl","https://github.com/evil/pet/releases/download/v"+ReleaseInfo.DisplayVersion+"/"+Path.GetFileName(package));Reject(()=>Call("Validate",job),"其他仓库的附件不能自动执行");Field(job,"AssetUrl",ReleaseInfo.RepositoryUrl+"/releases/download/v"+ReleaseInfo.DisplayVersion+"/"+Path.GetFileName(package));
                Field(job,"InstalledVersion",ReleaseInfo.DisplayVersion);Reject(()=>Call("Validate",job),"相同版本或降级不得自动安装");Field(job,"InstalledVersion","0.0.1");
                Field(job,"WorkDirectory",Path.GetDirectoryName(directory));Reject(()=>Call("Validate",job),"工作目录不能离开该安装的 data/updates");Field(job,"WorkDirectory",work);
                Call("ApplyPortable",job,new Action<int>(count=>{if(count==2)throw new InvalidOperationException("simulated disk failure");}));
                throw new Exception("rollback fixture did not fail");
            }
            catch(Exception e)
            {
                if(e.Message=="simulated disk failure")return ContinueAfterRollback();
                Console.WriteLine("FAIL Apply: "+e.Message);return 1;
            }
        }
        static int ContinueAfterRollback()
        {
            try
            {
                string directory=fixtureDirectory;
                Check(Hash(Path.Combine(directory,"桌面伙伴.exe"))==oldHash && File.ReadAllText(Path.Combine(directory,"README.md"))=="old readme","替换中途失败恢复原版文件");
                Check(File.ReadAllText(Path.Combine(directory,"data","settings.json"))=="private config sentinel","失败不改动用户 data");
                foreach(string entry in new[]{"DafeiyuDeskPet/../escape.txt","DafeiyuDeskPet/data/settings.json","DafeiyuDeskPet/README.md","DafeiyuDeskPet/evil.exe","DafeiyuDeskPet/C:/evil.txt","DafeiyuDeskPet/docs/../../evil.txt"})
                {
                    string work=Path.Combine(directory,"data","updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);string path=Path.Combine(work,"DeskPet-Windows-"+ReleaseInfo.DisplayVersion+".zip");Archive(path,entry);var job=Job(directory,work,path,"portable");
                    Reject(()=>Call("ApplyPortable",job,null),"非法 ZIP 条目被拒绝: "+entry);
                    Check(Hash(Path.Combine(directory,"桌面伙伴.exe"))==oldHash,"拒绝 ZIP 前没有修改原版");
                }
                string validWork=Path.Combine(directory,"data","updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(validWork);string valid=Path.Combine(validWork,"DeskPet-Windows-"+ReleaseInfo.DisplayVersion+".zip");Archive(valid,null);
                Call("ApplyPortable",Job(directory,validWork,valid,"portable"),null);
                Check(AssemblyName.GetAssemblyName(Path.Combine(directory,"桌面伙伴.exe")).Version.ToString()==ReleaseInfo.AssemblyVersion,"便携版替换完整发行文件");
                Check(File.ReadAllText(Path.Combine(directory,"data","settings.json"))=="private config sentinel","成功保留配置及导入外观目录");
                Check(File.Exists(Path.Combine(directory,"DeskPet.Update.exe")) && File.Exists(Path.Combine(directory,"docs","installation.md")) && File.Exists(Path.Combine(directory,"templates","README.md")),"助手、文档和模板教程一起更新");
                Console.WriteLine("Update apply tests: "+passed+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL Apply: "+e.Message);return 1;}
        }
    }
}
