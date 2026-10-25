using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DeskPet.Desktop;
using Dafeiyu;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class ProfileArtworkChecks
{
    static byte[] Pixels(Bitmap bitmap)
    {
        var bytes=new byte[bitmap.PixelSize.Width*bitmap.PixelSize.Height*4];var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try{bitmap.CopyPixels(new PixelRect(bitmap.PixelSize),pin.AddrOfPinnedObject(),bytes.Length,bitmap.PixelSize.Width*4);}finally{pin.Free();}return bytes;
    }
    internal static int CheckPage(DesktopController owner,CompanionHomePage page,string stage)
    {
        int count=0;void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
        var left=page.GetLogicalDescendants().OfType<Border>().Single(x=>x.Name=="CompanionProfileCard");var right=page.GetLogicalDescendants().OfType<Border>().Single(x=>x.Name=="CompanionRelationshipCard");
        var allowed=new[]{owner.Skin.DisplayName,owner.Skin.DisplayDescription,TextResources.Get("relationship.home.introduction"),""};
        Check(left.GetLogicalDescendants().OfType<TextBlock>().All(x=>allowed.Contains(x.Text??"")),"left profile text contains character identity/bio only");
        Check(left.GetLogicalDescendants().OfType<TextBlock>().Single(x=>x.Name=="CompanionHomeDescription").FontSize==14,"native profile retains the established 14px biography typography");
        Check(!left.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Name=="CompanionCurrentWelcome")&&(page.View!.Snapshot.State==null?!right.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Name=="CompanionCurrentWelcome"):right.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Name=="CompanionCurrentWelcome"&&x.Text==RelationshipContent.Welcome(page.View.Snapshot.StageId,owner.RelationshipPack))),"welcome remains saved-state-only and belongs to right relation card");
        var image=left.GetLogicalDescendants().OfType<Image>().Single(x=>x.Name=="CompanionProfileScene");var actual=(Bitmap)image.Source!;
        using(var originals=new RelationshipStageArtSource()){var expected=originals.Get(page.View!.CompanionId,stage);Check(expected!=null&&actual.PixelSize==expected.PixelSize&&Pixels(actual).SequenceEqual(Pixels(expected)),"profile uses undecorated verified original scene pixels: "+page.View.CompanionId+"/"+stage);}
        Check(Math.Abs(image.Bounds.Width/image.Bounds.Height-(double)actual.PixelSize.Width/actual.PixelSize.Height)<.025,"profile keeps whole landscape aspect ratio");
        var position=image.TranslatePoint(default,left)!.Value;var bio=left.GetLogicalDescendants().OfType<StackPanel>().Single(x=>x.Name=="CompanionProfileBio");var below=bio.TranslatePoint(default,left)!.Value;
        Check(position.X>=0&&position.X+image.Bounds.Width<=left.Bounds.Width+1&&position.Y+image.Bounds.Height<=below.Y+1,"complete landscape is above identity/bio inside the same card");return count;
    }
    internal static int Run(string directory,string language,bool typographyOnly=false,bool freshOnly=false)
    {
        int count=0;void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
        void Complete(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("Profile artwork fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        Directory.CreateDirectory(directory);new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false,GreetingEnabled=false,QuietMode=true});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory));owner.RelationshipService.UpdateSettings(false,true,true);
        using var page=new CompanionHomePage(owner);var window=new Window{Width=1280,Height=960,Content=page};window.Show();RelationshipStageArtSource? replaced=null;RelationshipHomeView? savedHigh=null;
        string output=Path.GetFullPath("work/relationship-validation/evidence/m6c/profile-art/"+(freshOnly?"fresh-native":typographyOnly?"native-typography":"native"));Directory.CreateDirectory(output);if(typographyOnly)window.Width=520;
        void Capture(string name){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=new RenderTargetBitmap(new PixelSize((int)window.Width,(int)window.Height));image.Render(window);image.Save(Path.Combine(output,language+"-"+name+".png"),new PngBitmapEncoderOptions());}
        try{
            byte[] before=File.ReadAllBytes(Path.Combine(directory,"relationships","state.json"));Complete(page.RefreshAsync());
            Check(page.View!.Snapshot.State==null&&page.View.Progress.Score==0&&!page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Any(),"introductory official art creates no earned state or unlocked album card");
            if(freshOnly)foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{520,1280}){window.Width=width;DesktopTheme.Apply(theme);Complete(page.RefreshAsync());count+=CheckPage(owner,page,"first_meeting");Capture("official-fresh-"+theme+"-"+width);}
            Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"relationships","state.json"))),"fresh profile render does not write relationship score dates milestones or receipts");
            Check(page.GetLogicalDescendants().OfType<TextBlock>().Single(x=>x.Name=="CompanionHomeDescription").FontSize==14,"native short biography retains the established 14px size");
            var ids=typographyOnly||freshOnly?Array.Empty<string>():language=="en_US"?new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}:new[]{"e-bao"};
            foreach(string id in ids){
                var skin=owner.Skins.Single(x=>x.CanonicalBuiltIn&&x.OfficialCharacterId==id);owner.RelationshipService.UpdateSettings(true,true,true);
                if(id=="e-bao"){owner.RelationshipService.Record("official:"+id,RelationshipEventType.Visit,"profile-first",new RelationshipTime(DateTime.UtcNow.AddDays(-60),TimeSpan.Zero));Complete(owner.SelectSkinAsync(skin));Complete(page.RefreshAsync());count+=CheckPage(owner,page,"first_meeting");}
                if(id=="marfu")RelationshipHomeFixtures.SeedMaximum(owner.RelationshipService,"official:"+id);else RelationshipHomeFixtures.Seed156(owner.RelationshipService,"official:"+id);owner.RelationshipService.UpdateSettings(false,true,true);Complete(owner.SelectSkinAsync(skin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
                string stage=id=="marfu"?"best_friend":"familiar";count+=CheckPage(owner,page,stage);if(savedHigh==null||id=="marfu")savedHigh=page.View;
                foreach(string theme in id=="e-bao"?new[]{"light","dark"}:new[]{"light"})foreach(int width in id=="e-bao"?new[]{520,1280}:new[]{1280}){window.Width=width;DesktopTheme.Apply(theme);Complete(page.RefreshAsync());count+=CheckPage(owner,page,stage);Capture(id+"-"+theme+"-"+width);}
                var cache=(RelationshipStageArtSource)typeof(CompanionHomePage).GetField("scenes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)!;Check(cache.Count<=6,"hero plus album keeps page scene cache bounded at six");
            }
            string folder=Path.Combine(directory,"own-bio");Directory.CreateDirectory(folder);File.Copy("assets/e-bao-spritesheet.png",Path.Combine(folder,"spritesheet.png"));string description=RelationshipHomeFixtures.ProfileBiography(language);
            var custom=new PetSkin{Id="profile-own-bio",PickerKey="custom-pack:profile-own-bio",Folder=folder,Atlas="spritesheet.png",Name=RelationshipHomeFixtures.ProfileName(language),Prompt="Fixture",Description=description};owner.Skins.Add(custom);Complete(owner.SelectSkinAsync(custom));Complete(page.RefreshAsync());Check(!page.GetLogicalDescendants().OfType<Image>().Any(x=>x.Name=="CompanionProfileScene")&&page.GetLogicalDescendants().OfType<CompanionPortraitVisual>().Any(),"custom character never borrows official stage art");
            if(!typographyOnly&&!freshOnly){typeof(CompanionHomePage).GetProperty("View")!.SetValue(page,savedHigh);DesktopTheme.Apply(DesktopTheme.IsDark?"light":"dark");Dispatcher.UIThread.RunJobs();Check(!page.GetLogicalDescendants().OfType<Image>().Any(x=>x.Name=="CompanionProfileScene")&&page.GetLogicalDescendants().OfType<TextBlock>().Single(x=>x.Name=="CompanionHomeName").Text==custom.DisplayName,"theme refresh cannot lend a saved high partner scene to a fresh current owner");Complete(page.RefreshAsync());}
            page.GetLogicalDescendants().OfType<Expander>().Single(x=>x.Name=="CompanionIntroduction").IsExpanded=true;window.Width=520;Complete(page.RefreshAsync());var text=page.GetLogicalDescendants().OfType<TextBlock>().Single(x=>x.Name=="CompanionHomeDescription");Check(text.Text==description&&text.FontSize==14&&text.Bounds.Height>text.FontSize*2&&text.TextLayout.Height<=text.Bounds.Height+1,"native long biography retains 14px, complete text and wrapping");foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Complete(page.RefreshAsync());Capture("own-long-bio-"+theme);}
            if(typographyOnly||freshOnly)return count;
            Complete(owner.SelectSkinAsync(owner.Skins.Single(x=>x.CanonicalBuiltIn&&x.OfficialCharacterId=="e-bao")));Complete(page.RefreshAsync());var field=typeof(CompanionHomePage).GetField("scenes",BindingFlags.Instance|BindingFlags.NonPublic)!;replaced=(RelationshipStageArtSource)field.GetValue(page)!;field.SetValue(page,new RelationshipStageArtSource(asset=>new MemoryStream(new byte[]{1,2,3})));Complete(page.RefreshAsync());Check(page.View!.ProfileArtStageId!=null&&!page.GetLogicalDescendants().OfType<Image>().Any(x=>x.Name=="CompanionProfileScene")&&page.GetLogicalDescendants().OfType<CompanionPortraitVisual>().Any(),"verified source failure falls back to the same partner portrait");Capture("bad-scene-fallback");
        }finally{window.Close();page.Dispose();replaced?.Dispose();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        return count;
    }
}
