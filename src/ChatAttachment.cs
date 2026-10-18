using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;

namespace Dafeiyu
{
    [DataContract]
    public sealed class ChatAttachment
    {
        public const int MaximumBytes=4*1024*1024;
        public const string FileFilter="支持的文件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.txt;*.md;*.csv;*.tsv;*.json;*.xml;*.yaml;*.yml;*.log;*.cs;*.py;*.js;*.ts;*.tsx;*.jsx;*.html;*.css;*.sql;*.ps1;*.sh;*.toml;*.ini;*.cfg|所有文件|*.*";
        [DataMember] public string Name {get;private set;}
        [DataMember] public string MediaType {get;private set;}
        [DataMember] public string Data {get;private set;}
        [DataMember] public string Text {get;private set;}[DataMember] public string SourcePath {get;private set;}
        public bool IsImage {get{return MediaType!=null && MediaType.StartsWith("image/",StringComparison.Ordinal);}}
        public ChatAttachment Copy(){return (ChatAttachment)MemberwiseClone();}
        public static ChatAttachment FromText(string name,string text){var file=new ChatAttachment{Name=name,Text=text,Data="",MediaType="text/plain"};Snapshot(new[]{file});return file;}
        public static ChatAttachment CreateImage(string name,byte[] png)
        {
            if(png==null || png.Length<8 || png.Length>MaximumBytes || png[0]!=137 || png[1]!=80 || png[2]!=78 || png[3]!=71)throw new ApiException("截图格式无效或超过 4 MB，请选择更小的应用窗口。");
            var file=new ChatAttachment{Name=name,Data=Convert.ToBase64String(png),Text="",MediaType="image/png"};Snapshot(new[]{file});return file;
        }
        public static long StoredSize(IEnumerable<ChatAttachment> files){return files==null?0:files.Sum(f=>(long)(f.Data??"").Length+Encoding.UTF8.GetByteCount(f.Text??"")+512);}
        public static List<ChatAttachment> Snapshot(IEnumerable<ChatAttachment> files)
        {
            var copy=files==null?new List<ChatAttachment>():files.Select(f=>f==null?null:f.Copy()).ToList();
            if(copy.Count>4 || copy.Any(f=>f==null || String.IsNullOrEmpty(f.Name) || f.Name.Length>255 || (f.MediaType!="text/plain" && f.MediaType!="application/pdf" && f.MediaType!="image/png" && f.MediaType!="image/jpeg" && f.MediaType!="image/gif" && f.MediaType!="image/webp") || (f.Text??"").Length>100000 || (f.Data??"").Length>MaximumBytes*4L/3+4) || StoredSize(copy)>MaximumBytes*4L/3+4096)
                throw new ApiException("最多添加 4 个附件，总大小不超过 4 MB；文字文件最多 10 万字。");
            return copy;
        }
        public static ChatAttachment Read(string path)
        {
            var info=new FileInfo(path);if(info.Length==0 || info.Length>MaximumBytes)throw new ApiException("附件不能为空，单次附件总大小不超过 4 MB。");
            string ext=info.Extension.ToLowerInvariant();byte[] bytes=File.ReadAllBytes(path);if(bytes.Length>MaximumBytes)throw new ApiException("附件超过 4 MB。");
            var file=new ChatAttachment{Name=info.Name,Data="",Text="",MediaType="text/plain",SourcePath=info.FullName};
            string signature=Encoding.ASCII.GetString(bytes,0,Math.Min(12,bytes.Length));
            if(ext==".png" && bytes.Length>=8 && bytes[0]==137 && signature.Substring(1,3)=="PNG")file.MediaType="image/png";
            else if((ext==".jpg" || ext==".jpeg") && bytes.Length>=3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255)file.MediaType="image/jpeg";
            else if(ext==".gif" && signature.StartsWith("GIF8"))file.MediaType="image/gif";
            else if(ext==".webp" && signature.StartsWith("RIFF") && signature.EndsWith("WEBP"))file.MediaType="image/webp";
            else if(ext==".pdf" && signature.StartsWith("%PDF-"))file.MediaType="application/pdf";
            else if(ext==".doc" || ext==".docx" || ext==".xls" || ext==".xlsx")
            {
                // Word/Excel documents are attached as their readable text so the
                // model receives content rather than compressed binary parts.
                try{file.Text=DocumentFormats.ReadDocumentText(path,bytes,CancellationToken.None);}
                catch(ArgumentException error){throw new ApiException(error.Message);}
                if(file.Text.Length>100000)file.Text=file.Text.Substring(0,99990)+"\n[文档文字过长，已截断]";
            }
            else if(new[]{".txt",".md",".csv",".tsv",".json",".xml",".yaml",".yml",".log",".cs",".py",".js",".ts",".tsx",".jsx",".html",".css",".sql",".ps1",".sh",".toml",".ini",".cfg"}.Contains(ext))
            {
                try{using(var memory=new MemoryStream(bytes))using(var reader=new StreamReader(memory,new UTF8Encoding(false,true),true))file.Text=reader.ReadToEnd();}
                catch(DecoderFallbackException){throw new ApiException("文字文件编码无法识别，请另存为 UTF-8 后重新添加。");}
                if(file.Text.IndexOf('\0')>=0)throw new ApiException("此文件包含二进制内容，请选择文字文件、图片、PDF 或 DOCX。");
            }
            else throw new ApiException("暂不支持此文件类型或文件格式无效。支持图片、PDF、Word/Excel 文档和常见文字／代码文件。");
            if(file.MediaType!="text/plain")file.Data=Convert.ToBase64String(bytes);
            else if(String.IsNullOrWhiteSpace(file.Text))throw new ApiException("附件没有可读取的文字。");
            Snapshot(new[]{file});return file;
        }
        internal static object Content(ChatMessage message,bool anthropic)
        {
            if(message.Attachments==null || message.Attachments.Count==0)return message.Content;
            var parts=new List<object>();
            foreach(var file in Snapshot(message.Attachments))
            {
                if(file.MediaType=="text/plain")parts.Add(new {type="text",text="[附件："+file.Name+"]\n"+file.Text});
                else if(anthropic)parts.Add(new {type=file.IsImage?"image":"document",source=new {type="base64",media_type=file.MediaType,data=file.Data}});
                else if(file.IsImage)parts.Add(new {type="image_url",image_url=new {url="data:"+file.MediaType+";base64,"+file.Data}});
                else parts.Add(new {type="file",file=new {filename=file.Name,file_data="data:"+file.MediaType+";base64,"+file.Data}});
            }
            if(!String.IsNullOrWhiteSpace(message.Content))parts.Add(new {type="text",text=message.Content});return parts;
        }
    }
}
