using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu
{
    // Static PNG integrity precedes native original-size pixel decoding. Neither
    // stage display's header check nor a decoder's tolerant header load is enough.
    public static class HolidayKeepsakePng
    {
        internal const int MaximumBytes=12*1024*1024;
        static readonly uint[] crcTable=MakeCrcTable();
        internal static bool Geometry(int w,int h)
        {return w>=800&&w<=3200&&h>0&&h<=2000&&(long)w*h<=6400000&&Math.Abs(w/(h*(5.0/3))-1)<=.005;}
        internal static string Hash(byte[] bytes)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        internal static byte[] Read(Stream input,int length,string hash,int width,int height,IHolidayKeepsakeSceneValidator validator)
        {
            if(input==null||length<45||length>MaximumBytes||!Geometry(width,height))throw new InvalidDataException("Invalid scene descriptor.");
            byte[] bytes;
            using(var output=new MemoryStream()){
                var buffer=new byte[16384];int count;
                while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>length)throw new InvalidDataException("Scene exceeds descriptor.");output.Write(buffer,0,count);}
                bytes=output.ToArray();
            }
            if(bytes.Length!=length||Hash(bytes)!=hash)throw new InvalidDataException("Scene digest mismatch.");
            ValidateStructure(bytes,width,height);validator.Validate(bytes,width,height);return bytes;
        }
        public static void ValidateStructure(byte[] bytes,int width,int height)
        {
            byte[] signature={137,80,78,71,13,10,26,10};
            if(bytes==null||bytes.Length<45||bytes.Length>MaximumBytes||!Geometry(width,height))throw new InvalidDataException("Invalid PNG size.");
            for(int i=0;i<8;i++)if(bytes[i]!=signature[i])throw new InvalidDataException("Not PNG.");
            int offset=8,depth=0,color=0,interlace=0;bool header=false,palette=false,data=false,endedData=false,end=false,transparency=false;
            using(var compressed=new MemoryStream()){
                while(offset<bytes.Length){
                    if(bytes.Length-offset<12)throw new InvalidDataException("Truncated PNG chunk.");
                    uint raw=Big(bytes,offset);if(raw>MaximumBytes||raw>bytes.Length-offset-12)throw new InvalidDataException("Invalid PNG chunk length.");
                    int count=(int)raw,start=offset+8;string kind=Encoding.ASCII.GetString(bytes,offset+4,4);
                    for(int n=offset+4;n<offset+8;n++)if(!((bytes[n]>=65&&bytes[n]<=90)||(bytes[n]>=97&&bytes[n]<=122)))throw new InvalidDataException("Invalid PNG chunk type.");
                    if((bytes[offset+6]&32)!=0)throw new InvalidDataException("Reserved PNG chunk bit.");
                    if(Crc(bytes,offset+4,count+4)!=Big(bytes,start+count))throw new InvalidDataException("PNG CRC mismatch.");
                    if(!header&&kind!="IHDR")throw new InvalidDataException("Missing first IHDR.");
                    if(data&&kind!="IDAT")endedData=true;
                    switch(kind){
                        case "IHDR":
                            if(header||count!=13||Big(bytes,start)!=(uint)width||Big(bytes,start+4)!=(uint)height)throw new InvalidDataException("PNG geometry mismatch.");
                            depth=bytes[start+8];color=bytes[start+9];interlace=bytes[start+12];
                            bool valid=color==0?(depth==1||depth==2||depth==4||depth==8||depth==16):color==3?(depth==1||depth==2||depth==4||depth==8):(color==2||color==4||color==6)&&(depth==8||depth==16);
                            if(!valid||bytes[start+10]!=0||bytes[start+11]!=0||interlace>1)throw new InvalidDataException("Unsupported PNG header.");header=true;break;
                        case "PLTE":
                            if(palette||data||color==0||color==4||count==0||count%3!=0||count>768||(color==3&&count/3>(1<<depth)))throw new InvalidDataException("Invalid PNG palette.");palette=true;break;
                        case "tRNS":
                            if(transparency||data||color==4||color==6||(color==0&&count!=2)||(color==2&&count!=6)||(color==3&&(!palette||count==0||count>256)))throw new InvalidDataException("Invalid PNG transparency.");transparency=true;break;
                        case "IDAT":
                            if(endedData||(color==3&&!palette))throw new InvalidDataException("Invalid PNG data order.");data=true;compressed.Write(bytes,start,count);break;
                        case "IEND":
                            if(!data||count!=0||offset+12!=bytes.Length)throw new InvalidDataException("Invalid PNG end.");end=true;break;
                        case "acTL":case "fcTL":case "fdAT":throw new InvalidDataException("Animated PNG is not a retained scene.");
                        default:if((bytes[offset+4]&32)==0)throw new InvalidDataException("Unknown critical PNG chunk.");break;
                    }
                    offset+=count+12;
                }
                if(!end)throw new InvalidDataException("Missing PNG end.");
                ValidatePixels(compressed.ToArray(),width,height,depth,color,interlace);
            }
        }
        static void ValidatePixels(byte[] z,int w,int h,int depth,int color,int interlace)
        {
            if(z.Length<6||(z[0]&15)!=8||(z[0]>>4)>7||((z[0]<<8)+z[1])%31!=0||(z[1]&32)!=0)throw new InvalidDataException("Invalid zlib header.");
            int channels=color==0||color==3?1:color==2?3:color==4?2:4;
            int[] xs={0,4,0,2,0,1,0},ys={0,0,4,0,2,0,1},dx={8,8,4,4,2,2,1},dy={8,8,8,4,4,2,2};
            uint a=1,b=0;
            using(var input=new ExactDeflateInput(z,2,z.Length-6))using(var deflate=new DeflateStream(input,CompressionMode.Decompress)){
                for(int pass=0;pass<(interlace==0?1:7);pass++){
                    int pw=interlace==0?w:Math.Max(0,(w-xs[pass]+dx[pass]-1)/dx[pass]);
                    int ph=interlace==0?h:Math.Max(0,(h-ys[pass]+dy[pass]-1)/dy[pass]);if(pw==0||ph==0)continue;
                    int length=(pw*channels*depth+7)/8;var row=new byte[length];
                    for(int y=0;y<ph;y++){
                        int filter=deflate.ReadByte();if(filter<0||filter>4)throw new InvalidDataException("Invalid PNG filter or truncated pixels.");Adler((byte)filter,ref a,ref b);
                        int position=0;while(position<length){int count=deflate.Read(row,position,length-position);if(count==0)throw new InvalidDataException("Truncated PNG pixels.");position+=count;}
                        foreach(byte value in row)Adler(value,ref a,ref b);
                    }
                }
                if(deflate.ReadByte()!=-1)throw new InvalidDataException("Excess PNG pixels.");
                if(input.Position!=input.Length)throw new InvalidDataException("Trailing compressed PNG bytes.");
            }
            if((b<<16|a)!=Big(z,z.Length-4))throw new InvalidDataException("PNG compressed checksum mismatch.");
        }
        // DeflateStream otherwise reads ahead past the final block and silently
        // accepts trailing junk before Adler32. Withhold the final byte from bulk
        // reads: valid DEFLATE needs that byte; an early final block leaves it
        // unread. This proves the boundary without per-byte reads or another codec.
        sealed class ExactDeflateInput:Stream
        {
            readonly MemoryStream input;
            internal ExactDeflateInput(byte[] bytes,int offset,int count){input=new MemoryStream(bytes,offset,count,false);}
            public override bool CanRead {get{return true;}} public override bool CanSeek {get{return false;}} public override bool CanWrite {get{return false;}}
            public override long Length {get{return input.Length;}} public override long Position {get{return input.Position;}set{throw new NotSupportedException();}}
            public override int Read(byte[] buffer,int offset,int count)
            {
                if(buffer==null)throw new ArgumentNullException("buffer");
                if(offset<0||count<0)throw new ArgumentOutOfRangeException();
                if(offset>buffer.Length||count>buffer.Length-offset)throw new ArgumentException("Invalid read range.");
                if(count==0)return 0;
                long remaining=input.Length-input.Position;
                // A completed final DEFLATE block does not ask for more input.
                // Raw DeflateStream otherwise treats a truncated source as EOF,
                // even when the expected pixels happened to arrive beforehand.
                if(remaining==0)throw new InvalidDataException("Truncated final PNG compression block.");
                return input.Read(buffer,offset,(int)Math.Min(count,remaining>1?remaining-1:remaining));
            }
            public override void Flush(){} public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}
            public override void SetLength(long value){throw new NotSupportedException();} public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
            protected override void Dispose(bool disposing){if(disposing)input.Dispose();base.Dispose(disposing);}
        }
        static void Adler(byte value,ref uint a,ref uint b){a=(a+value)%65521;b=(b+a)%65521;}
        static uint Big(byte[] bytes,int at){return ((uint)bytes[at]<<24)|((uint)bytes[at+1]<<16)|((uint)bytes[at+2]<<8)|bytes[at+3];}
        static uint[] MakeCrcTable(){var table=new uint[256];for(uint n=0;n<256;n++){uint c=n;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320U^(c>>1):c>>1;table[n]=c;}return table;}
        static uint Crc(byte[] bytes,int start,int count){uint c=0xffffffffU;for(int i=start;i<start+count;i++)c=crcTable[(c^bytes[i])&255]^(c>>8);return c^0xffffffffU;}
    }
}
