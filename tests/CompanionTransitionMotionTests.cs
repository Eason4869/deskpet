using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Dafeiyu.Tests
{
    public static class CompanionTransitionMotionTests
    {
        static int checks;
        static void Check(bool valid,string message)
        {
            if(!valid)throw new Exception(message);
            checks++;
        }

#if !NET10_0
        public static int Main(){return Run();}
#endif
        public static int Run()
        {
            try
            {
                var serializer=new DataContractJsonSerializer(typeof(PetSettings));
                Func<string,PetSettings> read=json=>{
                    using(var input=new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var result=serializer.ReadObject(input) as PetSettings;
                        if(result==null)throw new InvalidDataException(json);
                        return result;
                    }
                };
                Check(read("{}").CompanionTransitionEffect=="ripple","legacy preferences default to avatar ripple");
                Check(read("{\"CompanionTransitionEffect\":\"invalid\"}").CompanionTransitionEffect=="ripple","unknown motion falls back to ripple");
                var settings=read("{\"CompanionTransitionEffect\":\"off\"}");
                Check(settings.Copy().CompanionTransitionEffect=="off","off survives settings copy");
                settings.CompanionTransitionEffect="random";
                var destination=new PetSettings();
                var copy=typeof(SettingsStore).GetMethod("CopyPreferences",BindingFlags.NonPublic|BindingFlags.Static);
                if(copy==null)throw new MissingMethodException("CopyPreferences");
                copy.Invoke(null,new object[]{settings,destination});
                Check(destination.CompanionTransitionEffect=="random","preference copy keeps Random");
                Check(CompanionTransitionMotion.Choices.SequenceEqual(new[]{"off","ripple","zipper","random"}),"dropdown order");
                Check(CompanionTransitionMotion.Duration("ripple").TotalMilliseconds==650&&CompanionTransitionMotion.Duration("zipper").TotalMilliseconds==700,"preset durations");
                Check(Enumerable.Range(0,40).Select(_=>CompanionTransitionMotion.Resolve("random")).All(p=>p=="ripple"||p=="zipper"),"random selects an animated preset");
                foreach(var size in new[]{Tuple.Create(420d,390d),Tuple.Create(1280d,820d)})
                {
                    double w=size.Item1,h=size.Item2,x=28,y=h-40,seam=.23*w;
                    var ripple=CompanionRevealPaths.Create("ripple",w,h,x,y,seam);
                    var points=new RecordingOutline();
                    ripple.WriteReveal(0,points);
                    Check(points.Points.Count==0,"the initial ripple leaves the old view visible");
                    ripple.WriteReveal(.25,points);
                    Check(Math.Abs((points.MinX+points.MaxX)/2-x)<.01&&Math.Abs((points.MinY+points.MaxY)/2-y)<.01,"ripple is centered on the actual avatar");
                    points=new RecordingOutline();
                    ripple.WriteReveal(1,points);
                    Check(points.MinX<=0&&points.MaxX>=w&&points.MinY<=0&&points.MaxY>=h,"ripple reaches the viewport corners");
                    foreach(int ring in new[]{0,1,2})
                    {
                        points=new RecordingOutline();
                        ripple.WriteRing(.45,ring,points);
                        Check(points.Figures==2,"each ripple ring has a transparent center");
                    }
                    var zipper=CompanionRevealPaths.Create("zipper",w,h,x,y,seam);
                    foreach(double p in new[]{.25,.5,.67,.85})
                    {
                        points=new RecordingOutline();
                        zipper.WriteReveal(p,points);
                        double bottom=h*Math.Min(1,p/.68);
                        Check(Math.Abs(points.MaxY-bottom)<.01,"the opened area ends at the straight pull until it reaches the bottom");
                        if(p<.68)
                        {
                            var lining=new RecordingOutline();
                            zipper.Zipper.WritePart(p,ZipperPart.Lining,lining);
                            Check(lining.Points.Any(pt=>Math.Abs(pt.Item2-h)<.01&&Math.Abs(pt.Item1-seam)<=2),"closed strip continues vertically below the pull");
                        }
                    }
                    points=new RecordingOutline();
                    zipper.WriteReveal(1,points);
                    Check(points.MinX<=0&&points.MaxX>=w&&points.MinY<=0&&points.MaxY>=h,"zipper opens the full viewport");
                }
                Console.WriteLine("Companion transition geometry: "+checks+" passed");
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }

        sealed class RecordingOutline:ILiquidThemeOutline
        {
            internal readonly List<Tuple<double,double>> Points=new List<Tuple<double,double>>();
            internal int Figures;
            internal double MinX {get{return Points.Min(p=>p.Item1);}}
            internal double MaxX {get{return Points.Max(p=>p.Item1);}}
            internal double MinY {get{return Points.Min(p=>p.Item2);}}
            internal double MaxY {get{return Points.Max(p=>p.Item2);}}
            public void MoveTo(double x,double y){Figures++;LineTo(x,y);}
            public void LineTo(double x,double y){Points.Add(Tuple.Create(x,y));}
            public void CurveTo(double x1,double y1,double x2,double y2,double x,double y){LineTo(x1,y1);LineTo(x2,y2);LineTo(x,y);}
            public void Close(){}
        }
    }
}
