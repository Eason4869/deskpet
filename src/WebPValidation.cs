using System;
using System.IO;
using System.Text;
namespace Dafeiyu
{
    internal static class WebPValidation
    {
        // Container rules: developers.google.com/speed/webp/docs/riff_container
        // VP8 frame tag/partition layout: RFC 6386 section 9.1.
        internal static GeneratedImageFormat Validate(byte[] data)
        {
            if(data==null||data.Length<26||Text(data,0)!="RIFF"||Text(data,8)!="WEBP"||Little(data,4)!=(uint)data.Length-8)throw Invalid();
            int position=12,width=0,height=0,canvasWidth=0,canvasHeight=0;bool extended=false,pixels=false,alpha=false;
            while(position+8<=data.Length){string kind=Text(data,position);uint rawSize=Little(data,position+4);if(rawSize>data.Length-position-8)throw Invalid();int size=(int)rawSize,p=position+8;
                if(kind=="VP8X"){
                    if(position!=12||extended||size!=10||(data[p]&0xc1)!=0||data[p+1]!=0||data[p+2]!=0||data[p+3]!=0||(data[p]&2)!=0)throw Invalid();
                    extended=true;canvasWidth=1+Little24(data,p+4);canvasHeight=1+Little24(data,p+7);
                }
                else if(kind=="ALPH"){
                    if(!extended||pixels||alpha||size<2||(data[p]&0xc0)!=0||(data[p]&3)>1||((data[p]>>4)&3)>1)throw Invalid();alpha=true;
                }
                else if(kind=="VP8 "){
                    if(pixels||size<=10||data[p+3]!=157||data[p+4]!=1||data[p+5]!=42)throw Invalid();
                    int tag=Little24(data,p),partition=tag>>5;
                    // Require a visible, supported keyframe and both compressed partitions.
                    // A matching signature plus the ten-byte uncompressed header is not pixels.
                    if((tag&1)!=0||((tag>>1)&7)>3||(tag&16)==0||partition<1||partition>=size-10||(data[p+7]&0xc0)!=0||(data[p+9]&0xc0)!=0)throw Invalid();
                    width=data[p+6]|data[p+7]<<8;height=data[p+8]|data[p+9]<<8;pixels=true;
                }
                else if(kind=="VP8L"){
                    if(pixels||alpha||size<=5||data[p]!=47||(data[p+4]&0xe0)!=0)throw Invalid();
                    width=1+(data[p+1]|((data[p+2]&63)<<8));height=1+((data[p+2]>>6)|(data[p+3]<<2)|((data[p+4]&15)<<10));pixels=true;
                }
                else if(kind=="ANIM"||kind=="ANMF")throw new InvalidDataException(TextResources.Get("webPValidation.validate.text"));
                else if(!extended)throw Invalid();
                position=p+size;if((size&1)!=0){if(position>=data.Length||data[position]!=0)throw Invalid();position++;}
            }
            if(position!=data.Length||!pixels||!GeneratedImageValidation.ValidDimensions(width,height)||(extended&&(canvasWidth!=width||canvasHeight!=height)))throw Invalid();
            return new GeneratedImageFormat{MediaType="image/webp",Width=width,Height=height};
        }
        static string Text(byte[] data,int p){return Encoding.ASCII.GetString(data,p,4);}
        static uint Little(byte[] data,int p){return (uint)(data[p]|data[p+1]<<8|data[p+2]<<16|data[p+3]<<24);}
        static int Little24(byte[] data,int p){return data[p]|data[p+1]<<8|data[p+2]<<16;}
        static InvalidDataException Invalid(){return new InvalidDataException(TextResources.Get("webPValidation.invalid.text"));}
    }
}
