using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    // Release authoring only. Reorders existing BGRA pixels without resampling.
    public static class SkinPackExport
    {
        const int Width=480,Height=520;
        static readonly double[] BaseSeconds={1.02,1.76,1.76,.64,.7,1.28,1.14,1.08,1.08};
        static readonly double[] InteractionSeconds={1,.64,1.6,.8,1.6,1.2,3.2,2};
        static readonly string[] InteractionNames={"lifted","landing","petting","poked","feeding","happy","sleeping","special"};
        static int verified;
        [STAThread] public static int Main(string[] args)
        {
            try {
                if(args.Length!=1)throw new ArgumentException("Supply an isolated export directory.");
                string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
                var pets=new[]{
                    new SkinDefinition("e-bao","E包","Dafeiyu.EbaoAtlas","Dafeiyu.EbaoPreview",true,PersonaProfiles.Ebao),
                    new SkinDefinition("dafeiyu","大肥鱼","Dafeiyu.Atlas","Dafeiyu.Preview",true,PersonaProfiles.Dafeiyu),
                    new SkinDefinition("marfu","马尔芙","Dafeiyu.MarfuAtlas","Dafeiyu.MarfuPreview",true,PersonaProfiles.Marfu,"聪明俏皮 · 金发绿眼魔法少女")};
                foreach(var pet in pets)Export(pet,Path.Combine(output,pet.Id));
                Console.WriteLine("PASS complete v2 skin export: "+verified+" frames verified pixel-for-pixel; integer alignment and existing mirrors only.");return 0;
            }catch(Exception e){Console.WriteLine(e);return 1;}
        }
        static byte[] Pixels(BitmapSource image)
        {
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixels=new byte[Width*Height*4];converted.CopyPixels(pixels,Width*4,0);return pixels;
        }
        static byte[] Registered(SkinDefinition pet,SpriteAtlas atlas,int action,int phase)
        {
            int col=pet.ActionFrame(action,phase),row=pet.ActionRow(action);bool mirror=pet.MirrorsAction(action);
            Point offset=SpriteAlignment.Offset(pet.Id,row,col,mirror);int dx=(int)offset.X,dy=(int)offset.Y;
            if(offset.X!=dx || offset.Y!=dy)throw new InvalidOperationException("Only integer pixel registration may be exported.");
            var original=Pixels(atlas.GetFrame(row,col));var result=new byte[original.Length];int visible=0,copied=0;
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++) {
                int src=(y*Width+x)*4;int tx=(mirror?Width-1-x:x)+dx,ty=y+dy;
                if(original[src+3]!=0)visible++;
                if(tx<0 || ty<0 || tx>=Width || ty>=Height){if(original[src+3]!=0)throw new InvalidOperationException(pet.Id+" action "+action+" alignment would clip source artwork.");continue;}
                int dst=(ty*Width+tx)*4;Buffer.BlockCopy(original,src,result,dst,4);
                if(original[src+3]!=0)copied++;
                for(int c=0;c<4;c++)if(result[dst+c]!=original[src+c])throw new InvalidOperationException("Pixel copy changed source color.");
            }
            if(copied!=visible)throw new InvalidOperationException("Source pixels were dropped.");return result;
        }
        static void WriteAtlas(string path,IList<byte[]> frames,int columns)
        {
            int w=Width*columns,h=Height*((frames.Count+columns-1)/columns);var pixels=new byte[w*h*4];
            for(int i=0;i<frames.Count;i++)for(int y=0;y<Height;y++)Buffer.BlockCopy(frames[i],y*Width*4,pixels,((i/columns*Height+y)*w+i%columns*Width)*4,Width*4);
            var bitmap=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(path))encoder.Save(file);
            BitmapSource decoded;using(var file=File.OpenRead(path))decoded=BitmapDecoder.Create(file,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
            for(int i=0;i<frames.Count;i++) {
                var frame=new CroppedBitmap(decoded,new Int32Rect(i%columns*Width,i/columns*Height,Width,Height));
                if(!Pixels(frame).SequenceEqual(frames[i]))throw new InvalidOperationException("PNG encoding changed pixels: "+path);verified++;
            }
        }
        static void Export(SkinDefinition pet,string folder)
        {
            Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"animations"));var atlas=pet.Load();var interactions=InteractionAtlas.LoadForSkin(pet);
            var manifest=new SkinManifest{Version=2,Name=pet.Name,Format="illustrated-atlases-v1",CellWidth=Width,CellHeight=Height,Preview="preview.png",SystemPrompt=pet.SystemPrompt,Description=pet.Description,SpecialAction=pet.Name+"专属动作",Animations=new Dictionary<string,IllustratedAnimation>(),Gaze=new IllustratedGaze{File="gaze.png",Columns=4,Frames=16,Interpolation="none",Order="clockwise-from-up"}};
            for(int action=0;action<AnimationPlayer.States.Length;action++) {
                string name=AnimationPlayer.States[action];var frames=new List<byte[]>();
                if(ExportQuiet(pet,name,folder,manifest))continue;
                for(int phase=0;phase<AnimationPlayer.Counts[action];phase++)frames.Add(Registered(pet,atlas,action,phase));
                WriteAtlas(Path.Combine(folder,"animations",name+".png"),frames,8);
                manifest.Animations.Add(name,new IllustratedAnimation{File="animations/"+name+".png",Columns=8,Frames=frames.Count,Fps=frames.Count/BaseSeconds[action],Loop=action!=4});
            }
            for(int row=0;row<InteractionNames.Length;row++) {
                string name=InteractionNames[row];if(ExportQuiet(pet,name,folder,manifest))continue;
                var frames=Enumerable.Range(0,4).Select(col=>Pixels(interactions.GetFrame(row,col))).ToList();
                WriteAtlas(Path.Combine(folder,"animations",name+".png"),frames,8);
                manifest.Animations.Add(name,new IllustratedAnimation{File="animations/"+name+".png",Columns=8,Frames=4,Fps=4/InteractionSeconds[row],Loop=row==0 || row==6});
            }
            var gaze=Enumerable.Range(0,16).Select(i=>Registered(pet,atlas,9+i/8,i%8)).ToList();WriteAtlas(Path.Combine(folder,"gaze.png"),gaze,4);
            WriteAtlas(Path.Combine(folder,"preview.png"),new[]{Registered(pet,atlas,0,0)},1);
            using(var file=File.Create(Path.Combine(folder,"pet.json")))SkinCatalog.Serializer().WriteObject(file,manifest);
            IllustratedPack.Validate(folder,manifest,true);
            Console.WriteLine("PASS "+pet.Id+": 17 actions, eight-frame quiet originals, unchanged other frame counts, 16 gaze directions, persona and special action.");
        }
        static bool ExportQuiet(SkinDefinition pet,string action,string folder,SkinManifest manifest)
        {
            var spec=AdditionalInteractions.Quiet(pet,action,true);if(spec==null)return false;
            string relative="animations/"+action+".png",path=Path.Combine(folder,relative);
            using(var input=typeof(SkinPackExport).Assembly.GetManifestResourceStream(spec.ResourceName))using(var output=File.Create(path))input.CopyTo(output);
            var restored=IllustratedPack.ReadImage(path,spec.Width*spec.Columns,spec.Height);
            using(var input=typeof(SkinPackExport).Assembly.GetManifestResourceStream(spec.ResourceName)){
                var original=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
                for(int i=0;i<spec.Frames;i++){
                    var rect=new Int32Rect(i*Width,0,Width,Height);
                    if(!Pixels(new CroppedBitmap(original,rect)).SequenceEqual(Pixels(new CroppedBitmap(restored,rect))))throw new InvalidDataException("Quiet export changed source pixels.");verified++;
                }
            }
            manifest.Animations.Add(action,new IllustratedAnimation{File=relative,Columns=spec.Columns,Frames=spec.Frames,Fps=spec.Fps,Loop=spec.Loop});return true;
        }
    }
}
