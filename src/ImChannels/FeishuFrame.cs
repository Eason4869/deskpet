using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dafeiyu.ImChannels
{
    // Wire format documented by larksuite/node-sdk ws-client/proto-buf/pbbp2.js.
    public sealed class FeishuFrame
    {
        public ulong Sequence,Log;public int Service,Method;
        public readonly Dictionary<string,string> Headers=new Dictionary<string,string>();
        public byte[] Payload=new byte[0];public string Encoding="",PayloadType="",LogNew="";
        public byte[] Encode()
        {
            using(var output=new MemoryStream()){
                Number(output,1,Sequence);Number(output,2,Log);Number(output,3,(ulong)Service);Number(output,4,(ulong)Method);
                foreach(var pair in Headers)using(var header=new MemoryStream()){Bytes(header,1,System.Text.Encoding.UTF8.GetBytes(pair.Key));Bytes(header,2,System.Text.Encoding.UTF8.GetBytes(pair.Value));Bytes(output,5,header.ToArray());}
                if(Encoding.Length>0)Bytes(output,6,System.Text.Encoding.UTF8.GetBytes(Encoding));if(PayloadType.Length>0)Bytes(output,7,System.Text.Encoding.UTF8.GetBytes(PayloadType));Bytes(output,8,Payload);if(LogNew.Length>0)Bytes(output,9,System.Text.Encoding.UTF8.GetBytes(LogNew));return output.ToArray();
            }
        }
        public static FeishuFrame Decode(byte[] data)
        {
            if(data==null||data.Length>ImProtocol.MaxBytes)throw new InvalidDataException();var frame=new FeishuFrame();int mask=0;
            using(var input=new MemoryStream(data)){while(input.Position<input.Length){ulong tag=Varint(input);int id=(int)(tag>>3),wire=(int)(tag&7);if(id>=1&&id<=4){if(wire!=0)throw new InvalidDataException();ulong value=Varint(input);mask|=1<<(id-1);switch(id){case 1:frame.Sequence=value;break;case 2:frame.Log=value;break;case 3:frame.Service=checked((int)value);break;case 4:frame.Method=checked((int)value);break;}}
                    else if(id>=5&&id<=9){if(wire!=2)throw new InvalidDataException();var bytes=ReadBytes(input);if(id==5){if(frame.Headers.Count>=64)throw new InvalidDataException();string key="",value="";using(var header=new MemoryStream(bytes)){while(header.Position<header.Length){ulong ht=Varint(header);if(ht==10)key=Text(ReadBytes(header));else if(ht==18)value=Text(ReadBytes(header));else Skip(header,(int)(ht&7));}}if(key.Length>0)frame.Headers[key]=value;}
                        else if(id==6)frame.Encoding=Text(bytes);else if(id==7)frame.PayloadType=Text(bytes);else if(id==8)frame.Payload=bytes;else frame.LogNew=Text(bytes);}
                    else Skip(input,wire);
                }}if(mask!=15)throw new InvalidDataException();return frame;
        }
        public string Header(string key){string value;return Headers.TryGetValue(key,out value)?value:"";}
        static string Text(byte[] bytes){return new UTF8Encoding(false,true).GetString(bytes);}
        static void Number(Stream stream,int id,ulong value){Write(stream,(ulong)(id<<3));Write(stream,value);}
        static void Bytes(Stream stream,int id,byte[] data){Write(stream,(ulong)((id<<3)|2));Write(stream,(ulong)data.Length);stream.Write(data,0,data.Length);}
        static void Write(Stream stream,ulong value){while(value>=128){stream.WriteByte((byte)((value&127)|128));value>>=7;}stream.WriteByte((byte)value);}
        static ulong Varint(Stream input){ulong result=0;for(int n=0;n<10;n++){int value=input.ReadByte();if(value<0||(n==9&&value>1))throw new InvalidDataException();result|=(ulong)(value&127)<<(n*7);if((value&128)==0)return result;}throw new InvalidDataException();}
        static byte[] ReadBytes(Stream stream){ulong size=Varint(stream);if(size>ImProtocol.MaxBytes||size>(ulong)(stream.Length-stream.Position))throw new InvalidDataException();var bytes=new byte[(int)size];if(stream.Read(bytes,0,bytes.Length)!=bytes.Length)throw new InvalidDataException();return bytes;}
        static void Skip(Stream stream,int wire){if(wire==0)Varint(stream);else if(wire==2)ReadBytes(stream);else if(wire==1||wire==5){int size=wire==1?8:4;if(stream.Length-stream.Position<size)throw new InvalidDataException();stream.Position+=size;}else throw new InvalidDataException();}
    }
    internal sealed class FeishuFragments
    {
        sealed class Parts{internal DateTimeOffset At=DateTimeOffset.UtcNow;internal byte[][] Values;internal int Bytes;}
        readonly Dictionary<string,Parts> pending=new Dictionary<string,Parts>();
        internal byte[] Add(FeishuFrame frame)
        {
            int total,index;if(!Int32.TryParse(frame.Header("sum"),out total)||!Int32.TryParse(frame.Header("seq"),out index)||total<1||total>64||index<0||index>=total)throw new InvalidDataException();
            if(total==1)return frame.Payload;string id=frame.Header("message_id");if(id.Length==0||id.Length>256)throw new InvalidDataException();
            foreach(var key in pending.Where(x=>DateTimeOffset.UtcNow-x.Value.At>TimeSpan.FromSeconds(30)).Select(x=>x.Key).ToArray())pending.Remove(key);
            Parts parts;if(!pending.TryGetValue(id,out parts)){if(pending.Count>=32)throw new InvalidDataException();parts=new Parts{Values=new byte[total][]};pending.Add(id,parts);}
            if(parts.Values.Length!=total)throw new InvalidDataException();if(parts.Values[index]!=null){if(!parts.Values[index].SequenceEqual(frame.Payload))throw new InvalidDataException();return null;}
            parts.Bytes+=frame.Payload.Length;if(parts.Bytes>ImProtocol.MaxBytes){pending.Remove(id);throw new InvalidDataException();}parts.Values[index]=frame.Payload;
            if(parts.Values.Any(x=>x==null))return null;pending.Remove(id);return parts.Values.SelectMany(x=>x).ToArray();
        }
    }
}
