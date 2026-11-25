using System;
using System.IO;
using System.Linq;
using System.Reflection;
namespace Dafeiyu.Tests
{
    public static class ReleaseVersionTests
    {
        public static int Main()
        {
            try {
                var type=typeof(ReleaseInfo).Assembly.GetType("Dafeiyu.ReleaseVersion");
                if(type==null)throw new Exception("missing shared release version parser");
                var parse=type.GetMethod("Parse",BindingFlags.Static|BindingFlags.Public);
                int count=0;
                foreach(var pair in new[]{new[]{"0.9.1","0.9.2-dev"},new[]{"0.9.2-dev","0.9.3"},new[]{"0.9.4-dev.2","0.9.4-dev.10"},new[]{"0.9.4-dev.10","0.9.4"},new[]{"0.9.9","0.10.0-dev.1"},new[]{"0.9.4","0.9.4.1"},new[]{"0.11.4-dev.2","1.0.0"},new[]{"1.0.0-dev.2","1.0.0"}}) {
                    var left=parse.Invoke(null,new object[]{pair[0]});var right=parse.Invoke(null,new object[]{pair[1]});
                    if(((IComparable)left).CompareTo(right)>=0 || ((IComparable)right).CompareTo(left)<=0)throw new Exception("incorrect ordering: "+pair[0]+" < "+pair[1]);count++;
                }
                if(((IComparable)parse.Invoke(null,new object[]{"v0.9.4"})).CompareTo(parse.Invoke(null,new object[]{"0.9.4.0"}))!=0)throw new Exception("legacy zero revision differs");
                foreach(string value in new[]{"../0.9.4","0.9.4\n","0.9.4-dev.-1","0.9.4-dev.01","0.9.4-rc.1","65535.0.0","0.9.4-dev.2147483648","0.9","0.9.4+fake"}) {
                    try{parse.Invoke(null,new object[]{value});throw new Exception("accepted invalid version: "+value);}catch(TargetInvocationException e){if(!(e.InnerException is ArgumentException))throw;}count++;
                }
                if(ReleaseInfo.InstallerAssetName("0.9.5-dev.2")!="DeskPet-Windows-Setup-0.9.5-dev.2.exe" || !ReleaseInfo.WindowsUpdateAsset("DeskPet-Windows-0.9.5-dev.2.zip","0.9.5-dev.2",true))throw new Exception("preview asset rejected");
                CheckUpdateNotes();
                Console.WriteLine("Release version tests: "+count+" passed");return 0;
            } catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }

        static void CheckUpdateNotes()
        {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"release-notes-"+Guid.NewGuid().ToString("N"));
            try {
                if(ReleaseNotes.ForStartup(directory,"1.0.5",false,null)!=null)throw new Exception("fresh install opened release notes");
                var preview=ReleaseNotes.ForStartup(directory,"1.0.6-dev.3",true,null);
                if(preview==null || preview.Previous.Canonical!="1.0.5")throw new Exception("skipped preview upgrade was not recognized");
                var notes=preview.Entries("zh_CN");
                if(!notes.Select(n=>n.Version.Canonical).SequenceEqual(new[]{"1.0.6-dev.3","1.0.6-dev.2","1.0.6-dev.1"}) || !notes[0].Items[0].Contains("卸载向导"))throw new Exception("preview notes are missing or mixed with stable releases");
                if(notes[0].Details==null || notes[1].Details!=null)throw new Exception("preview details should link to published releases");
                if(!preview.Entries("en_US")[0].Items[0].Contains("uninstall wizard"))throw new Exception("English changelog was not embedded");
                preview.MarkSeen();
                if(ReleaseNotes.ForStartup(directory,"1.0.6-dev.3",true,null)!=null || ReleaseNotes.ForStartup(directory,"1.0.5",true,null)!=null)throw new Exception("same version or downgrade reopened release notes");
                var promoted=ReleaseNotes.ForStartup(directory,"1.0.6",true,null);
                if(promoted==null || !promoted.Entries("zh_CN").Select(n=>n.Version.Canonical).SequenceEqual(new[]{"1.0.6"}))throw new Exception("preview to stable promotion did not show the stable notes");

                var stable=ReleaseNotes.ForStartup(Path.Combine(directory,"legacy"),"1.0.5",true,"1.0.1");
                if(stable==null || !stable.Entries("zh_CN").Select(n=>n.Version.Canonical).SequenceEqual(new[]{"1.0.5","1.0.3","1.0.2"}))throw new Exception("stable notes included preview releases or missed intermediate versions");
                var unknown=ReleaseNotes.ForStartup(Path.Combine(directory,"unknown"),"1.0.5",true,null);
                if(unknown==null || unknown.Previous!=null || unknown.Entries("zh_CN").Count!=1)throw new Exception("migration without a prior version must only show the installed release");
                if(ReleaseNotes.ForStartup(Path.Combine(directory,"same"),"1.0.6-dev.3",true,"v1.0.6-dev.3")!=null)throw new Exception("known unchanged legacy version displayed a notice");
                Console.WriteLine("Post-update notes: channel, range, promotion, migration and deduplication passed");
            } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}
