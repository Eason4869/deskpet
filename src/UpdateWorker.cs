using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

#if UPDATE_WORKER
[assembly:System.Reflection.AssemblyTitle("桌面伙伴更新助手")]
[assembly:System.Reflection.AssemblyProduct(Dafeiyu.ReleaseInfo.ProductName)]
[assembly:System.Reflection.AssemblyDescription("桌面伙伴更新助手：校验更新包、保留用户数据并重启应用")]
[assembly:System.Reflection.AssemblyCompany(Dafeiyu.ReleaseInfo.Author)]
[assembly:System.Reflection.AssemblyVersion(Dafeiyu.ReleaseInfo.AssemblyVersion)]
[assembly:System.Reflection.AssemblyFileVersion(Dafeiyu.ReleaseInfo.AssemblyVersion)]
#endif

namespace Dafeiyu
{
    public static class UpdateWorkerProgram
    {
        public static int Main(string[] args)
        {
            UpdateJob job=null;bool parentExited=false,trusted=false;
            try
            {
                if(args.Length!=1)return 2;job=UpdateInstall.Read<UpdateJob>(args[0]);UpdateInstall.Validate(job);
                if(!String.Equals(Path.GetFullPath(args[0]),Path.Combine(job.WorkDirectory,"job.json"),StringComparison.OrdinalIgnoreCase))return 2;
                if(!String.Equals(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName),Path.GetFullPath(job.WorkDirectory),StringComparison.OrdinalIgnoreCase))return 2;
                CheckVersion(job);CheckAbort(job);
                if(job.ParentPid<=0)throw new InvalidOperationException("原进程信息无效。");
                Process parent=null;try{parent=Process.GetProcessById(job.ParentPid);}catch(ArgumentException){parentExited=true;}
                using(parent)
                {
                    if(parent!=null)
                    {
                        if(parent.StartTime.ToUniversalTime().Ticks!=job.ParentStartedUtcTicks || !String.Equals(parent.MainModule.FileName,Path.Combine(job.InstallationDirectory,UpdateInstall.ExecutableName),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("原进程与安装目录不符。");
                        trusted=true;File.WriteAllText(Path.Combine(job.WorkDirectory,"ready"),"ready");
                        var deadline=DateTime.UtcNow.AddMinutes(2);
                        while(!parent.WaitForExit(100)){CheckAbort(job);if(DateTime.UtcNow>=deadline)throw new InvalidOperationException("伙伴尚未退出，本次更新已停止。");}parentExited=true;
                    }
                }
                if(parentExited && !trusted){trusted=true;File.WriteAllText(Path.Combine(job.WorkDirectory,"ready"),"ready");}
                CheckAbort(job);UpdateInstall.Validate(job);CheckVersion(job);
                if(job.Mode=="installed")UpdateInstall.ApplyInstaller(job);else UpdateInstall.ApplyPortable(job,null);
                Result(job,true,"已更新至 "+job.TargetVersion+"，设置与外观已保留。");Restart(job);return 0;
            }
            catch(Exception e)
            {
                // A rejected job cannot write to or start an arbitrary directory.
                if(trusted)
                {
                    try{File.WriteAllText(Path.Combine(job.WorkDirectory,"error.log"),e.ToString());}catch(Exception){ }
                    Result(job,false,e is InvalidOperationException?e.Message:"更新未完成，原版和配置已保留。请重新检查更新。");
                    if(parentExited)try{Restart(job);}catch(Exception){ }
                }
                return 1;
            }
        }
        static void Result(UpdateJob job,bool success,string text)
        {
            var result=new UpdateResult{Success=success,Version=job.TargetVersion,Message=text};
            // Receipts are optional; neither one may prevent restarting the app.
            try{UpdateInstall.Write(Path.Combine(job.InstallationDirectory,"data","update-result.json"),result);}catch(Exception){ }
            try{UpdateInstall.Write(Path.Combine(job.WorkDirectory,"result.json"),result);}catch(Exception){ }
        }
        static void CheckAbort(UpdateJob job)
        {if(File.Exists(Path.Combine(job.WorkDirectory,"abort")))throw new InvalidOperationException("更新交接已取消，当前版本继续运行。");}
        static void CheckVersion(UpdateJob job)
        {
            UpdateInstall.CheckWorker(job,System.Reflection.Assembly.GetExecutingAssembly().Location);
            UpdateInstall.CheckInstalled(job);
        }
        static void Restart(UpdateJob job)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(job.InstallationDirectory,UpdateInstall.ExecutableName),"--update-result")
                {UseShellExecute=false,WorkingDirectory=job.InstallationDirectory});
        }
    }
}
