using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public sealed class RelationshipCardData
    {
        public string CompanionId {get;private set;}
        public string Name {get;private set;}
        public string StageId {get;private set;}
        public string EarnedLocalDate {get;private set;}
        public string StageName {get{return RelationshipContent.StageName(StageId);}}
        public string DateText {get{return FormatDate(EarnedLocalDate);}}
        public static string FormatDate(string localDate)
        {return DateTime.ParseExact(localDate,"yyyy-MM-dd",CultureInfo.InvariantCulture).ToString("D",CultureInfo.GetCultureInfo(TextResources.CurrentLanguage.Replace('_','-')));}
        public string FileName {get{return SafeFileName(Name,StageId,EarnedLocalDate);}}
        public static RelationshipCardData FromSnapshot(RelationshipSnapshot snapshot,string stageId,string name)
        {
            if(snapshot==null||snapshot.State==null)throw new ArgumentException("A saved milestone is required.");
            var milestone=snapshot.State.Milestones.Single(m=>m.StageId==stageId);
            return new RelationshipCardData{CompanionId=snapshot.State.CompanionId,Name=name,StageId=milestone.StageId,EarnedLocalDate=milestone.EarnedLocalDate};
        }
        public static string SafeFileName(string name,string stage,string date)
        {
            string value=new string((name??"").Where(c=>c>=32&&"<>:\"/\\|?*".IndexOf(c)<0&&Array.IndexOf(Path.GetInvalidFileNameChars(),c)<0).ToArray()).Trim(' ','.');
            if(value.Length>64)value=value.Substring(0,64);if(value.Length==0)value="Companion";
            return value+"-"+stage+"-"+date+".png";
        }
    }
    public enum RelationshipCardSaveResult { Cancelled, Saved, Failed }
    public static class RelationshipCardFile
    {
        public static Task<RelationshipCardSaveResult> SaveAsync(string destination,byte[] png)
        {
            if(String.IsNullOrEmpty(destination))return Task.FromResult(RelationshipCardSaveResult.Cancelled);
            return Task.Run(delegate {
                string temp=null;
                try {
                    byte[] signature={137,80,78,71,13,10,26,10};
                    if(png==null||png.Length<=signature.Length||!png.Take(8).SequenceEqual(signature))return RelationshipCardSaveResult.Failed;
                    string path=Path.GetFullPath(destination);temp=Path.Combine(Path.GetDirectoryName(path),"."+Guid.NewGuid().ToString("N")+".tmp");
                    using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(png,0,png.Length);file.Flush(true);}
                    if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
                    return RelationshipCardSaveResult.Saved;
                }catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException||error is System.Security.SecurityException))throw;return RelationshipCardSaveResult.Failed;}
                finally{if(temp!=null&&File.Exists(temp))try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
            });
        }
    }
}
