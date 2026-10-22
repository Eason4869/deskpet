using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace Dafeiyu
{
    // Binary data lives only in the current API request context, never tool text or task history.
    internal sealed class AgentImageInspection
    {
        internal const int MaximumBytes=3*1024*1024;
        readonly byte[] bytes;
        internal readonly string Name,MediaType,Question;internal readonly int Width,Height;
        AgentImageInspection(string name,byte[] bytes,string mime,int width,int height,string question){Name=name;this.bytes=bytes;MediaType=mime;Width=width;Height=height;Question=question;}
        internal object[] Content(string summary,bool anthropic){var parts=new List<object>();parts.Add(new{type="text",text=summary+(String.IsNullOrWhiteSpace(Question)?"":"\n图片问题："+Question)});string data=Convert.ToBase64String(bytes);if(anthropic)parts.Add(new{type="image",source=new{type="base64",media_type=MediaType,data=data}});else parts.Add(new{type="image_url",image_url=new{url="data:"+MediaType+";base64,"+data}});return parts.ToArray();}
        internal string Summary(){return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{name=Name,media_type=MediaType,width=Width,height=Height,bytes=bytes.Length,vision_input=true});}
        internal static AgentImageInspection Parse(string path,byte[] bytes,string question)
        {
            if(bytes==null||bytes.Length<14||bytes.Length>MaximumBytes)throw new ArgumentException("图片为空、已损坏或超过 3 MiB 检查限制。");
            string mime=null;int w=0,h=0;string ext=Path.GetExtension(path).ToLowerInvariant();
            if(ext==".png"&&bytes.Length>=33&&bytes[0]==137&&Encoding.ASCII.GetString(bytes,1,7)=="PNG\r\n\u001a\n"){
                bool header=false,data=false,end=false;int p=8;
                while(p+12<=bytes.Length){long len=BE(bytes,p);if(len>bytes.Length-p-12)throw Invalid();string kind=Encoding.ASCII.GetString(bytes,p+4,4);uint crc=0xffffffff;for(int i=p+4;i<p+8+(int)len;i++){crc^=bytes[i];for(int b=0;b<8;b++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);}if((crc^0xffffffff)!=BE(bytes,p+8+(int)len))throw Invalid();
                    if(!header){if(kind!="IHDR"||len!=13||BE(bytes,p+8)>16384||BE(bytes,p+12)>16384)throw Invalid();w=(int)BE(bytes,p+8);h=(int)BE(bytes,p+12);header=true;}
                    if(kind=="IDAT"&&len>0)data=true;p+=12+(int)len;if(kind=="IEND"){if(len!=0||p!=bytes.Length)throw Invalid();end=true;break;}}
                if(!header||!data||!end)throw Invalid();mime="image/png";
            }
            else if((ext==".jpg"||ext==".jpeg")&&bytes[0]==255&&bytes[1]==216&&bytes[bytes.Length-2]==255&&bytes[bytes.Length-1]==217){
                int p=2;while(p+3<bytes.Length){if(bytes[p++]!=255)throw Invalid();while(p<bytes.Length&&bytes[p]==255)p++;if(p>=bytes.Length)throw Invalid();int marker=bytes[p++];if(marker==0xda)break;if(marker==0xd9)break;if(marker>=0xd0&&marker<=0xd7)continue;if(p+2>bytes.Length)throw Invalid();int len=(bytes[p]<<8)|bytes[p+1];if(len<2||p+len>bytes.Length)throw Invalid();if((marker>=0xc0&&marker<=0xc3)||(marker>=0xc5&&marker<=0xc7)||(marker>=0xc9&&marker<=0xcb)||(marker>=0xcd&&marker<=0xcf)){if(len<8)throw Invalid();h=(bytes[p+3]<<8)|bytes[p+4];w=(bytes[p+5]<<8)|bytes[p+6];}p+=len;}
                mime="image/jpeg";
            }
            else if(ext==".gif"&&(Encoding.ASCII.GetString(bytes,0,6)=="GIF87a"||Encoding.ASCII.GetString(bytes,0,6)=="GIF89a")&&bytes[bytes.Length-1]==0x3b){w=LE16(bytes,6);h=LE16(bytes,8);mime="image/gif";}
            else if(ext==".webp"&&bytes.Length>=30&&Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&Encoding.ASCII.GetString(bytes,8,4)=="WEBP"&&LE(bytes,4)==bytes.Length-8){
                try{var format=WebPValidation.Validate(bytes);w=format.Width;h=format.Height;mime=format.MediaType;}catch(InvalidDataException){throw Invalid();}
            }
            if(mime==null||w<1||h<1||w>16384||h>16384||(long)w*h>20000000)throw Invalid();
#if !PORTABLE
            if(mime!="image/webp")try{using(var stream=new MemoryStream(bytes))using(var image=System.Drawing.Image.FromStream(stream,true,true)){if(image.Width!=w||image.Height!=h)throw Invalid();using(var bitmap=new System.Drawing.Bitmap(image))bitmap.GetPixel(0,0);}}catch(Exception e){if(!(e is ArgumentException||e is System.Runtime.InteropServices.ExternalException||e is OutOfMemoryException))throw;throw Invalid();}
#endif
            return new AgentImageInspection(Path.GetFileName(path),bytes,mime,w,h,question);
        }
        static ArgumentException Invalid(){return new ArgumentException("图片格式、尺寸或数据无效；支持 PNG/JPEG/GIF/WebP，最多 2000 万像素。");}
        static uint BE(byte[] b,int p){return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
        static uint LE(byte[] b,int p){return (uint)(b[p]|b[p+1]<<8|b[p+2]<<16|b[p+3]<<24);}
        static int LE16(byte[] b,int p){return b[p]|b[p+1]<<8;}static int LE24(byte[] b,int p){return b[p]|b[p+1]<<8|b[p+2]<<16;}
    }
}
