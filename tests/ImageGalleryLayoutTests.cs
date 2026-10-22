using System;
using System.Collections.Generic;
using System.IO;

namespace Dafeiyu.Tests
{
    public static class ImageGalleryLayoutTests
    {
        static int passed;
        public static int Main()
        {
            passed=0;string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"image-layout-tests-"+Guid.NewGuid().ToString("N"));
            try{Geometry();Preferences(directory);Console.WriteLine("ImageGalleryLayoutTests: "+passed+" passed");return 0;}
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static bool Equal(double actual,double expected){return Math.Abs(actual-expected)<0.000001;}
        static bool Finite(double value){return !Double.IsNaN(value) && !Double.IsInfinity(value);}
        static List<GeneratedImage> Mixed(){return new List<GeneratedImage>{new GeneratedImage{Id="newest",Width=100,Height=200},new GeneratedImage{Id="second",Width=200,Height=100},new GeneratedImage{Id="third",Width=100,Height=100},new GeneratedImage{Id="fourth",Width=200,Height=300},new GeneratedImage{Id="oldest",Width=0,Height=100}};}
        static void Geometry()
        {
            var images=Mixed();var masonry=ImageGalleryLayout.Calculate(images,692,"masonry");
            Check(masonry.Columns==3 && masonry.Items.Count==5 && Equal(masonry.Height,456),"masonry reports three equal columns and complete mixed-ratio content height");
            Check(Equal(masonry.Items[0].Width,220) && Equal(masonry.Items[0].Height,440) && Equal(masonry.Items[1].Height,110) && Equal(masonry.Items[3].Height,330),"masonry preserves landscape and portrait source aspect ratios");
            Check(Equal(masonry.Items[0].X,0) && Equal(masonry.Items[1].X,236) && Equal(masonry.Items[2].X,472) && Equal(masonry.Items[3].X,236) && Equal(masonry.Items[3].Y,126) && Equal(masonry.Items[4].X,472) && Equal(masonry.Items[4].Y,236),"masonry assigns each next input image to the shortest column with stable ties");
            Check(Equal(masonry.Items[4].Height,220) && images[0].Id=="newest" && images[4].Id=="oldest","invalid source dimensions fall back to square without reordering input");
            var grid=ImageGalleryLayout.Calculate(images,692,"grid");
            Check(grid.Columns==3 && Equal(grid.Height,456) && Equal(grid.Items[3].X,0) && Equal(grid.Items[3].Y,236) && Equal(grid.Items[4].X,236) && Equal(grid.Items[4].Y,236),"square grid fills rows in input order with known complete height");
            bool squares=true;foreach(var item in grid.Items)squares&=Equal(item.Width,220)&&Equal(item.Height,220);Check(squares,"grid uses uniform squares for every source image ratio");
            foreach(string mode in new[]{"masonry","grid"}){
                var narrow=ImageGalleryLayout.Calculate(images,100,mode);Check(narrow.Columns==1 && Equal(narrow.Items[0].Width,100) && Equal(narrow.Items[1].X,0),"narrow viewport uses a single available-width column: "+mode);
                bool noOverlap=true;for(int i=0;i<narrow.Items.Count;i++)for(int j=i+1;j<narrow.Items.Count;j++){var a=narrow.Items[i];var b=narrow.Items[j];if(a.X<b.X+b.Width && a.X+a.Width>b.X && a.Y<b.Y+b.Height && a.Y+a.Height>b.Y)noOverlap=false;}Check(noOverlap,"layout rectangles never overlap in narrow viewport: "+mode);
            }
            var empty=ImageGalleryLayout.Calculate(new List<GeneratedImage>(),692,"grid");Check(empty.Items.Count==0 && Equal(empty.Height,0) && empty.Columns>=1,"empty gallery has no placements or phantom trailing gap");
            Check(ImageGalleryLayout.Calculate(null,500,"masonry").Items.Count==0,"null image list is an empty gallery");
            foreach(double width in new[]{Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity,-100.0,0.0,0.5,Double.MaxValue})foreach(string mode in new[]{"masonry","grid"}){
                var layout=ImageGalleryLayout.Calculate(images,width,mode);bool valid=Finite(layout.Height)&&layout.Height>=0&&layout.Columns>=1;foreach(var item in layout.Items)valid&=Finite(item.X)&&Finite(item.Y)&&Finite(item.Width)&&Finite(item.Height)&&item.Width>=1&&item.Height>0;Check(valid,"invalid or extreme viewport metrics produce finite placement geometry: "+mode+" "+width);
            }
            var fallback=ImageGalleryLayout.Calculate(new List<GeneratedImage>{null,new GeneratedImage{Width=-5,Height=50}},220,"unknown");Check(Equal(fallback.Items[0].Height,220)&&Equal(fallback.Items[1].Height,220)&&Equal(fallback.Height,456),"null images and invalid layout mode use safe masonry square fallback");
        }
        static void Preferences(string directory)
        {
            var settings=new PetSettings();Check(settings.ImageGalleryLayout=="masonry","new settings default to masonry gallery");
            settings.ImageGalleryLayout="grid";var copy=settings.Copy();settings.ImageGalleryLayout="masonry";Check(copy.ImageGalleryLayout=="grid","settings copy preserves independent gallery layout preference");
            var store=new SettingsStore(Path.Combine(directory,"settings.json"));store.Save(copy);Check(store.Load().ImageGalleryLayout=="grid","grid preference persists across settings reload");
            copy.ImageGalleryLayout="unsupported";copy.Normalize();Check(copy.ImageGalleryLayout=="masonry","normalization replaces unknown layout with masonry");
            File.WriteAllText(Path.Combine(directory,"legacy.json"),"{}");Check(new SettingsStore(Path.Combine(directory,"legacy.json")).Load().ImageGalleryLayout=="masonry","old JSON inherits masonry gallery default");
        }
    }
}
