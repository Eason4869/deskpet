using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Dafeiyu.ImChannels
{
    public static class ImLoginQr
    {
        static readonly object gate=new object();static bool loaded;
        public static byte[] Png(string text)
        {
            if(String.IsNullOrEmpty(text)||text.Length>4096)throw new InvalidOperationException("im.protocolFailed");
            lock(gate)if(!loaded){using(var input=typeof(ImLoginQr).Assembly.GetManifestResourceStream("Dafeiyu.QRCoder.dll"))using(var bytes=new MemoryStream()){if(input==null)throw new FileNotFoundException();input.CopyTo(bytes);var assembly=System.Reflection.Assembly.Load(bytes.ToArray());AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args){return new System.Reflection.AssemblyName(args.Name).Name=="QRCoder"?assembly:null;};}loaded=true;}
            try{return Encode(text);}catch(Exception error){if(error is ArgumentException||error.GetType().Name=="DataTooLongException")throw new InvalidOperationException("im.protocolFailed");throw;}
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static byte[] Encode(string text)
        {
            using(var generator=new QRCoder.QRCodeGenerator())using(var data=generator.CreateQrCode(text,QRCoder.QRCodeGenerator.ECCLevel.M))using(var png=new QRCoder.PngByteQRCode(data))return png.GetGraphic(6);
        }
    }
}
