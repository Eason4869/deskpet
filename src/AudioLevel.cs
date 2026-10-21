using System;
namespace Dafeiyu
{
    public interface IAudioLevelSource { double AudioLevel { get; } }
    public static class AudioLevel
    {
        public static double Pcm16(byte[] bytes)
        {
            return Pcm16(bytes,0,bytes==null?0:bytes.Length-bytes.Length%2);
        }
        public static double Pcm16(byte[] bytes,int offset,int length)
        {
            if(bytes==null || length<2)return 0;
            if(offset<0 || length<0 || offset>bytes.Length-length || (offset&1)!=0 || (length&1)!=0)throw new ArgumentException("PCM range must contain complete samples.");
            double sum=0;int count=length/2;
            for(int i=0;i<count;i++){int at=offset+i*2;double value=(short)(bytes[at]|bytes[at+1]<<8)/32768.0;sum+=value*value;}
            return Math.Min(1,Math.Sqrt(sum/count)*3);
        }
    }
}
