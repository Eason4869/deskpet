using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class LiveWaveformTests
    {
        static byte[] Frame(VoiceOrbit orbit)
        {
            orbit.Measure(new Size(220,220));orbit.Arrange(new Rect(0,0,220,220));orbit.UpdateLayout();
            var image=new RenderTargetBitmap(220,220,96,96,PixelFormats.Pbgra32);image.Render(orbit);
            var bytes=new byte[220*220*4];image.CopyPixels(bytes,220*4,0);return bytes;
        }
        [STAThread] public static int Main()
        {
            try{
                var type=typeof(VoiceOrbit);var ctor=type.GetConstructor(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(Func<double>)},null);
                if(ctor==null)throw new Exception("steady audio has no independent animation clock");
                double seconds=0;var orbit=(VoiceOrbit)ctor.Invoke(new object[]{new Func<double>(()=>seconds)});
                for(int i=0;i<50;i++)orbit.Level=.6;
                byte[] before=Frame(orbit);seconds=.2;orbit.Level=.6;byte[] after=Frame(orbit);
                if(before.SequenceEqual(after))throw new Exception("steady audible output leaves a frozen waveform");
                for(int i=0;i<300;i++)orbit.Level=0;
                byte[] silence=Frame(orbit);seconds=.6;orbit.Level=0;
                if(!silence.SequenceEqual(Frame(orbit)))throw new Exception("silence displays false speech motion");
                byte[] pcm=new byte[640];for(int i=320;i<640;i+=2){pcm[i]=0xff;pcm[i+1]=0x3f;}
                var meter=typeof(AudioLevel).GetMethod("Pcm16",new[]{typeof(byte[]),typeof(int),typeof(int)});
                if(meter==null)throw new Exception("output metering averages the whole response instead of playback chunks");
                double quiet=(double)meter.Invoke(null,new object[]{pcm,0,320}),loud=(double)meter.Invoke(null,new object[]{pcm,320,320});
                if(quiet!=0||loud<.7)throw new Exception("chunk metering lost real loudness variation");
                Console.WriteLine("Live waveform tests passed: constant audio moves, silence rests, local PCM levels differ");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
