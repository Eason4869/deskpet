using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Automation;
namespace Dafeiyu.Tests
{
    public static class CompanionHomeUiTests
    {
        static Application app;static PetWindow pet;static int checks;
        static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        static async Task SelectCommitted(SkinDefinition skin,Window owner,string reason)
        {
            Check(await pet.RequestSkinAsync(skin,CompanionSelectionOrigin.MainWindow,owner),reason+" request succeeds");
            var deadline=DateTime.UtcNow.AddSeconds(20);while(pet.Skin.PickerKey!=skin.PickerKey&&DateTime.UtcNow<deadline)await Task.Delay(20);
            Check(pet.Skin.PickerKey==skin.PickerKey,reason+" commits selected identity");
        }
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(root,i)))yield return x;}
        static Button Button(DependencyObject root,string name){return All<Button>(root).Single(x=>x.Name==name);}
        static void Click(Button b){b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
        static bool Ink(BitmapSource source,Int32Rect region)
        {var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var bytes=new byte[region.Width*region.Height*4];converted.CopyPixels(region,bytes,region.Width*4,0);int ink=0;for(int i=0;i<bytes.Length;i+=4)if(bytes[i]+bytes[i+1]+bytes[i+2]<650&&bytes[i+3]>200)ink++;return ink>100;}
        static bool LightTextInk(BitmapSource bitmap){var source=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var bytes=new byte[408*110*4];source.CopyPixels(new Int32Rect(360,100,408,110),bytes,408*4,0);int count=0;for(int i=0;i<bytes.Length;i+=4)if(bytes[i]+bytes[i+1]+bytes[i+2]>600)count++;return count>100;}
        static byte[] Pixel(BitmapSource bitmap,int x,int y){var source=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var pixel=new byte[4];source.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel;}
        static bool DarkPng(BitmapSource bitmap){var source=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var pixel=new byte[4];source.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);return pixel[0]+pixel[1]+pixel[2]<200;}
        static void Capture(FrameworkElement w,string file){w.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(file))png.Save(f);}
        static void HeartIcons()
        {
            string output=Path.GetFullPath("work/relationship-validation/evidence/m6c/heart-wpf");Directory.CreateDirectory(output);
            foreach(string theme in new[]{"light","dark"})foreach(int size in new[]{22,32}){
                ThemeManager.Apply(theme);int n=size+16;var canvas=new Canvas{Width=n,Height=n};var icon=HomeVisual.Icon("heart",size);Canvas.SetLeft(icon,8);Canvas.SetTop(icon,8);canvas.Children.Add(icon);canvas.Measure(new Size(n,n));canvas.Arrange(new Rect(0,0,n,n));canvas.UpdateLayout();
                var bitmap=new RenderTargetBitmap(n,n,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);var bytes=new byte[n*n*4];bitmap.CopyPixels(bytes,n*4,0);Capture(canvas,Path.Combine(output,"wpf-"+theme+"-heart-"+size+".png"));
                int edge=0,ink=0,minY=n,maxY=-1;bool lobes=false;var rows=new System.Collections.Generic.List<string>();
                for(int y=0;y<n;y++){int count=0,runs=0;bool prior=false;for(int x=0;x<n;x++){bool visible=bytes[(y*n+x)*4+3]>0;if(visible){ink++;count++;minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);if(!prior)runs++;if(x==8||x==8+size-1||y==8||y==8+size-1)edge++;}prior=visible;}if(count>0)rows.Add(y+":"+count);if(y<=12&&runs>=2)lobes=true;}
                Console.WriteLine("HEART_RASTER WPF "+theme+" size="+size+" edge="+edge+" y="+minY+".."+maxY+" rows="+String.Join(",",rows));Check(ink>30&&lobes,"heart retains two curved lobes and visible silhouette");Check(edge==0,"heart design-space stroke has clear inset at every viewport edge");
            }
        }
        static async Task CaptureFeedback(RelationshipFeedbackWindow window,string captures,string locale,string kind)
        {
            foreach(string theme in new[]{"light","dark"}){
                ThemeManager.Apply(theme);await Task.Delay(60);Capture(window,Path.Combine(captures,locale+"-"+theme+"-"+kind+"-feedback.png"));
                var name=All<TextBlock>(window).Single(t=>t.ToolTip as string==pet.Skin.DisplayName);string original=name.Text;
                name.Text="很长的伙伴名字 · A companion with a long localized name";window.Width=260;await Task.Delay(30);Capture(window,Path.Combine(captures,locale+"-"+theme+"-"+kind+"-feedback-narrow.png"));name.Text=original;window.Width=350;
            }
        }
        static bool StageWordsStayWhole(TextBlock label)
        {
            var text=label.Inlines.FirstInline.ContentStart;int start=0;foreach(string word in label.Text.Split(' ')){if(word.Length>0&&Math.Abs(text.GetPositionAtOffset(start).GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).Y-text.GetPositionAtOffset(start+word.Length-1).GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).Y)>.5)return false;start+=word.Length+1;}return true;
        }
        static async Task<System.Windows.Controls.Primitives.UniformGrid> LoadedTrack(CompanionHomePage page)
        {
            page.UpdateLayout();var track=All<System.Windows.Controls.Primitives.UniformGrid>(page).Single(x=>x.Name=="RelationshipStageNodes");var timer=System.Diagnostics.Stopwatch.StartNew();
            while(!track.IsLoaded){if(timer.ElapsedMilliseconds>2000)throw new TimeoutException("Hosted stage track did not load");await Task.Delay(10);track=All<System.Windows.Controls.Primitives.UniformGrid>(page).Single(x=>x.Name=="RelationshipStageNodes");}page.UpdateLayout();return track;
        }
        static byte[] ProfilePixels(BitmapSource image)
        {var normalized=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var bytes=new byte[normalized.PixelWidth*normalized.PixelHeight*4];normalized.CopyPixels(bytes,normalized.PixelWidth*4,0);return bytes;}
        static void CheckProfile(CompanionHomePage page,SkinDefinition skin,string stage)
        {
            var left=All<Border>(page).Single(x=>x.Name=="CompanionProfileCard");var right=All<Border>(page).Single(x=>x.Name=="CompanionRelationshipCard");
            var allowed=new[]{skin.DisplayName,skin.DisplayDescription??"",TextResources.Get("relationship.home.introduction"),""};
            Check(All<TextBlock>(left).All(x=>allowed.Contains(x.Text)),"left profile text contains character identity/bio only");
            Check(!All<TextBlock>(left).Any(x=>x.Name=="CompanionCurrentWelcome")&&(page.View.Snapshot.State==null?!All<TextBlock>(right).Any(x=>x.Name=="CompanionCurrentWelcome"):All<TextBlock>(right).Any(x=>x.Name=="CompanionCurrentWelcome"&&x.Text==RelationshipContent.Welcome(page.View.Snapshot.StageId,skin.Manifest==null?null:skin.Manifest.Relationship))),"welcome remains saved-state-only and belongs to right relation card");
            var image=All<Image>(left).Single(x=>x.Name=="CompanionProfileScene");var actual=(BitmapSource)image.Source;
            using(var originals=new RelationshipStageArtSource()){var expected=originals.Get(page.View.CompanionId,stage);Check(expected!=null&&actual.PixelWidth==expected.PixelWidth&&actual.PixelHeight==expected.PixelHeight&&ProfilePixels(actual).SequenceEqual(ProfilePixels(expected)),"profile uses undecorated verified original scene pixels: "+page.View.CompanionId+"/"+stage);}
            Check(Math.Abs(image.ActualWidth/image.ActualHeight-(double)actual.PixelWidth/actual.PixelHeight)<.025,"profile keeps whole landscape aspect ratio");
            var position=image.TransformToAncestor(left).Transform(new Point());var bio=All<StackPanel>(left).Single(x=>x.Name=="CompanionProfileBio");var below=bio.TransformToAncestor(left).Transform(new Point());
            Check(position.X>=0&&position.X+image.ActualWidth<=left.ActualWidth+1&&position.Y+image.ActualHeight<=below.Y+1,"complete landscape is above identity/bio inside the same card");
        }
        static async Task ProfileArtwork(string locale,bool freshOnly=false)
        {
            string output=Path.GetFullPath("work/relationship-validation/evidence/m6c/profile-art/"+(freshOnly?"fresh-wpf":"wpf"));Directory.CreateDirectory(output);string root=Path.GetFullPath("work/pf-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);
            var cfg=new PetSettings{UiLanguage=locale,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",Wander=false,FollowCursor=false,QuietMode=true};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            var owned=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);owned.RelationshipService.UpdateSettings(false,true,true);
            using(var page=new CompanionHomePage(owned)){
                var window=new Window{Width=1280,Height=960,Content=page};window.Show();RelationshipStageArtSource replaced=null;RelationshipHomeView savedHigh=null;
                try{
                    byte[] before=File.ReadAllBytes(Path.Combine(root,"relationships","state.json"));await page.RefreshAsync();await LoadedTrack(page);
                    Check(page.View.Snapshot.State==null&&page.View.Progress.Score==0&&!All<RelationshipCardPreview>(page).Any(),"introductory official art creates no earned state or unlocked album card");
                    if(freshOnly)foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{520,1280}){window.Width=width;ThemeManager.Apply(theme);await page.RefreshAsync();await LoadedTrack(page);CheckProfile(page,owned.Skin,"first_meeting");Capture(window,Path.Combine(output,locale+"-official-fresh-"+theme+"-"+width+".png"));}
                    Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(root,"relationships","state.json"))),"fresh profile render does not write relationship score dates milestones or receipts");
                    var ids=freshOnly?new string[0]:locale=="en_US"?new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}:new[]{"e-bao"};
                    foreach(string id in ids){
                        var skin=owned.Catalog.Skins.Single(x=>x.IsOfficial&&x.BuiltinCharacterId==id);owned.RelationshipService.UpdateSettings(true,true,true);
                        if(id=="e-bao"){owned.RelationshipService.Record("official:"+id,RelationshipEventType.Visit,"profile-first",new RelationshipTime(DateTime.UtcNow.AddDays(-60),TimeSpan.Zero));Check(owned.SelectSkin(skin),"profile selects first saved identity");await page.RefreshAsync();await LoadedTrack(page);CheckProfile(page,skin,"first_meeting");}
                        await Task.Run(()=>{if(id=="marfu")RelationshipHomeFixtures.SeedMaximum(owned.RelationshipService,"official:"+id);else RelationshipHomeFixtures.Seed156(owned.RelationshipService,"official:"+id);});owned.RelationshipService.UpdateSettings(false,true,true);Check(owned.SelectSkin(skin),"profile switches to own official identity: "+id);await owned.Relationships.FlushAsync();await page.RefreshAsync();await LoadedTrack(page);
                        string stage=id=="marfu"?"best_friend":"familiar";CheckProfile(page,skin,stage);if(savedHigh==null||id=="marfu")savedHigh=page.View;
                        foreach(string theme in id=="e-bao"?new[]{"light","dark"}:new[]{"light"})foreach(int width in id=="e-bao"?new[]{520,1280}:new[]{1280}){window.Width=width;ThemeManager.Apply(theme);await page.RefreshAsync();await LoadedTrack(page);CheckProfile(page,skin,stage);Capture(window,Path.Combine(output,locale+"-"+id+"-"+theme+"-"+width+".png"));}
                        var cache=(RelationshipStageArtSource)typeof(CompanionHomePage).GetField("scenes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(page);Check(cache.Count<=6,"hero plus album keeps page scene cache bounded at six");
                    }
                    string ownFolder=Path.Combine(root,"own-bio");Directory.CreateDirectory(ownFolder);string atlas=Path.Combine(ownFolder,"spritesheet.png");File.Copy("assets/e-bao-spritesheet.png",atlas);
                    string description=RelationshipHomeFixtures.ProfileBiography(locale);var manifest=new SkinManifest{Version=1,Name=RelationshipHomeFixtures.ProfileName(locale),Atlas="spritesheet.png",SystemPrompt="Fixture",Description=description};string manifestFile=Path.Combine(ownFolder,"pet.json");using(var stream=File.Create(manifestFile))SkinCatalog.Serializer().WriteObject(stream,manifest);var custom=owned.Catalog.Import(manifestFile);
                    Check(owned.SelectSkin(custom),"custom profile uses its own supplied atlas");await page.RefreshAsync();await LoadedTrack(page);Check(!All<Image>(page).Any(x=>x.Name=="CompanionProfileScene")&&All<CompanionPortraitVisual>(page).Any(),"custom character never borrows official stage art");
                    if(!freshOnly)typeof(CompanionHomePage).GetProperty("View").SetValue(page,savedHigh,null);ThemeManager.Apply(ThemeManager.IsDark?"light":"dark");page.UpdateLayout();Check(!All<Image>(page).Any(x=>x.Name=="CompanionProfileScene")&&All<TextBlock>(page).Single(x=>x.Name=="CompanionHomeName").Text==custom.DisplayName,"theme refresh cannot lend a saved high partner scene to a fresh current owner");await page.RefreshAsync();await LoadedTrack(page);
                    var disclosure=All<Expander>(page).Single(x=>x.Name=="CompanionIntroduction");disclosure.IsExpanded=true;window.Width=520;await page.RefreshAsync();await LoadedTrack(page);var text=All<TextBlock>(page).Single(x=>x.Name=="CompanionHomeDescription");Check(text.Text==description&&text.ActualHeight>text.FontSize*2,"long localized character bio remains complete and wraps");foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);await page.RefreshAsync();var track=await LoadedTrack(page);Check(All<TextBlock>(track).All(StageWordsStayWhole),"settled fallback stage labels stay whole beside long bio");Capture(window,Path.Combine(output,locale+"-own-long-bio-"+theme+".png"));}
                    if(freshOnly)return;
                    var originalSkin=owned.Catalog.Skins.Single(x=>x.IsOfficial&&x.BuiltinCharacterId=="e-bao");owned.SelectSkin(originalSkin);await page.RefreshAsync();await LoadedTrack(page);
                    var field=typeof(CompanionHomePage).GetField("scenes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);replaced=(RelationshipStageArtSource)field.GetValue(page);field.SetValue(page,new RelationshipStageArtSource(asset=>new MemoryStream(new byte[]{1,2,3})));await page.RefreshAsync();var fallbackTrack=await LoadedTrack(page);Check(All<TextBlock>(fallbackTrack).All(StageWordsStayWhole),"settled source-failure fallback stage labels stay whole");Check(page.View.ProfileArtStageId!=null&&!All<Image>(page).Any(x=>x.Name=="CompanionProfileScene")&&All<CompanionPortraitVisual>(page).Any(),"verified source failure falls back to the same partner portrait");Capture(window,Path.Combine(output,locale+"-bad-scene-fallback.png"));
                }finally{window.Close();page.Dispose();if(replaced!=null)replaced.Dispose();owned.Close();}
                await owned.Relationships.FlushAsync();
            }Console.WriteLine("ProfileArtwork: "+checks+" passed");
        }
        static async Task StageTrackLayout(string locale)
        {
            string root=Path.GetFullPath("work/ui-consistency/stage-track-after/wpf-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var settings=new PetSettings{UiLanguage=locale,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",QuietMode=true};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);
            using(var page=new CompanionHomePage(pet)){
                var window=new Window{Width=locale=="de_DE"?900:1100,Height=960,Content=page};window.Show();
                try{
                    await page.RefreshAsync();await Task.Delay(60);page.UpdateLayout();
                    var track=All<System.Windows.Controls.Primitives.UniformGrid>(page).Single(x=>x.Name=="RelationshipStageNodes");
                    Check(track.Columns==(locale=="de_DE"?3:6)&&track.Rows==(locale=="de_DE"?2:1),"stage track uses fitting columns in actual "+window.Width+"px home");
                    foreach(var label in All<TextBlock>(track))Check(StageWordsStayWhole(label),"stage words do not split across rendered lines: "+label.Text);
                    Console.WriteLine("STAGE_TRACK width="+track.ActualWidth+" columns="+track.Columns+" rows="+track.Rows);
                    Capture(window,Path.GetFullPath("work/ui-consistency/stage-track-after/wpf-"+locale+"-"+window.Width+".png"));
                }finally{window.Close();}
            }
            Console.WriteLine("StageTrackLayout: "+checks+" passed");
        }
        static async Task FirstVisit()
        {
            string root=Path.GetFullPath("work/m6a/first-home-wpf-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var settings=new PetSettings{AutoUpdate=false,GreetingEnabled=false,QuietMode=true,ReadReplies=false,OnboardingState="completed",HideOnFullscreen=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);var model=new RelationshipHomeModel(pet.Relationships);
            Check((await model.UpdateSettingsAsync(false,false,true)).Success,"first-visit fixture disables growth through actual settings request");
            var skin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=pet.Skin.PickerKey);Check(pet.SelectSkin(skin),"unmet identity selected with growth off");await pet.Relationships.FlushAsync();
            pet.ShowChat();var chat=app.Windows.OfType<ChatWindow>().Single();chat.UpdateLayout();
            Check((await model.UpdateSettingsAsync(true,false,true)).Success,"first-visit fixture enables saved growth before home entry");await pet.Relationships.FlushAsync();
            string id=pet.RelationshipCompanionId;Check(!pet.IsVisible&&chat.IsVisible&&chat.CurrentPage=="home"&&pet.RelationshipService.GetSnapshot(id).State==null,"shown chat with hidden pet remains unmet immediately before home entry");
            for(int opening=0;opening<2;opening++){
                Click(Button(chat,"ShellAvatar"));var menu=Button(chat,"ShellAvatar").ContextMenu;var entry=menu.Items.OfType<MenuItem>().Single(x=>x.Name=="ProfileCompanionHome");entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await pet.Relationships.FlushAsync();var page=All<CompanionHomePage>(chat).Single();await page.RefreshAsync();page.UpdateLayout();
                var saved=new RelationshipService(new RelationshipStore(root)).GetSnapshot(id).State;
                Check(chat.CurrentPage=="companion"&&page.IsVisible&&!menu.IsOpen,"actual profile entry shows hosted companion home "+opening);
                Check(saved!=null&&saved.Score==2&&saved.Receipts.Count==opening+1&&saved.Receipts.All(r=>r.EventType==RelationshipEventType.Visit)&&saved.Receipts.Count(r=>r.AwardedPoints==2)==1&&saved.Receipts.Sum(r=>r.AwardedPoints)==2&&saved.Receipts.Select(r=>r.EventId).Distinct().Count()==saved.Receipts.Count,"actual home entry persists distinct Visits with exactly one two-point award "+opening);
                Check(saved.FirstLocalDate==saved.Receipts[0].LocalDate&&saved.Milestones.Count==1,"first home encounter persists first meeting facts");
                if(opening==0){Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="home","first home entry can return to chat before reopening");}
            }
            chat.Close();await pet.Relationships.FlushAsync();Console.WriteLine("FirstHomeVisitUi: "+checks+" passed");
        }
        static async Task Run(string locale,bool hostedOnly=false)
        {
            HeartIcons();
            var sparseBytes=new byte[40*60*4];sparseBytes[(10*40+12)*4+3]=255;sparseBytes[(49*40+27)*4+3]=255;var sparse=BitmapSource.Create(40,60,96,96,PixelFormats.Bgra32,null,sparseBytes,40*4);var trimmed=CompanionPortraitSource.Trim(sparse);Check(trimmed.PixelWidth==20&&trimmed.PixelHeight==44,"transparent exterior is removed with safe margin around every opaque pixel");Check(CompanionPortraitSource.Trim(BitmapSource.Create(20,20,96,96,PixelFormats.Bgra32,null,new byte[20*20*4],20*4))==null,"fully transparent artwork enters fallback");
            string captures=Path.GetFullPath(Path.Combine("work","relationship-validation","f3","wpf"));Directory.CreateDirectory(captures);
            string root=Path.Combine(captures,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string sourceRoot=Path.Combine(root,"portrait-source");Directory.CreateDirectory(sourceRoot);File.WriteAllText(Path.Combine(sourceRoot,"bad.png"),"broken");var avatarBytes=new byte[64*64*4];for(int y=12;y<52;y++)for(int x=20;x<44;x++){avatarBytes[(y*64+x)*4]=220;avatarBytes[(y*64+x)*4+3]=255;}var ownAvatar=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,avatarBytes,64*4);var avatarPng=new PngBitmapEncoder();avatarPng.Frames.Add(BitmapFrame.Create(ownAvatar));using(var stream=File.Create(Path.Combine(sourceRoot,"own.png")))avatarPng.Save(stream);
            var damagedSkin=new SkinDefinition("portrait-fixture",sourceRoot,new SkinManifest{Version=2,Name="Imported portrait",Preview="bad.png",Avatar="own.png",CellWidth=480,CellHeight=520,Animations=new System.Collections.Generic.Dictionary<string,IllustratedAnimation>{{"idle",new IllustratedAnimation{File="bad.png",Columns=1,Frames=1}}}});var ownPortrait=CompanionPortraitSource.Load(damagedSkin);Check(ownPortrait.Image!=null&&ownPortrait.Image.PixelWidth<ownPortrait.Image.PixelHeight,"damaged preview and idle fall back to the imported companion own avatar");damagedSkin.Manifest.Avatar="bad.png";var neutralPortrait=CompanionPortraitSource.Load(damagedSkin);Check(neutralPortrait.Image==null,"unreadable artwork produces neutral fallback without borrowing official art");foreach(string fallbackTheme in new[]{"light","dark"}){var fallback=new StackPanel{Width=640,Height=300,Orientation=Orientation.Horizontal};fallback.Children.Add(new CompanionPortraitVisual(ownPortrait,fallbackTheme=="dark"){Width=300,Height=280});fallback.Children.Add(new CompanionPortraitVisual(neutralPortrait,fallbackTheme=="dark"){Width=300,Height=280});fallback.Measure(new Size(640,300));fallback.Arrange(new Rect(0,0,640,300));Capture(fallback,Path.Combine(captures,locale+"-imported-fallback-"+fallbackTheme+".png"));}

            var settings=new PetSettings{UiLanguage=locale,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",ShowPredictedMessages=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);await Task.Run(()=>RelationshipHomeFixtures.Seed156(pet.RelationshipService,pet.RelationshipCompanionId));pet.SetPaused(true);pet.ShowChat();pet.Show();
            var deadline=System.Diagnostics.Stopwatch.StartNew();while(!app.Windows.OfType<RelationshipFeedbackWindow>().Any()){if(deadline.ElapsedMilliseconds>10000)throw new TimeoutException("Actual feedback not displayed");await Task.Delay(10);}
            var feedbackWindow=app.Windows.OfType<RelationshipFeedbackWindow>().Single();
            Check(!feedbackWindow.ShowActivated&&!feedbackWindow.ShowInTaskbar,"actual saved stage window does not request focus or taskbar entry");
            Check(pet.Animation.State=="idle","paused pet is not interrupted by automatic celebration");
            Check(pet.RelationshipService.GetSnapshot(pet.RelationshipCompanionId).State.Score==156,"automatic popup and celebration add no points");
            await CaptureFeedback(feedbackWindow,captures,locale,"stage");pet.Settings.QuietMode=true;deadline.Restart();while(feedbackWindow.IsVisible&&deadline.ElapsedMilliseconds<1500)await Task.Delay(10);Check(!feedbackWindow.IsVisible,"quiet mode dismisses an already-visible automatic notice");pet.Settings.QuietMode=false;pet.SetPaused(false);
            pet.PlayAction("waiting",100);pet.CelebrateRelationship("familiar");Check(pet.Animation.State=="waiting","celebration never preempts an existing manual action");
            pet.Animation.Play("idle",0);pet.CelebrateRelationship("familiar");await pet.Relationships.FlushAsync();Check(pet.Animation.State=="waving"&&pet.RelationshipService.GetSnapshot(pet.RelationshipCompanionId).State.Score==156,"actual automatic celebration plays without interaction rewards");
            await Task.Delay(100);var chat=app.Windows.OfType<ChatWindow>().Single(x=>x.IsVisible);
            var input=All<MarkdownEditor>(chat).Single(x=>x.Name=="MessageInput");input.Text="relationship route draft";
            Click(Button(chat,"ShellAvatar"));await Task.Delay(30);var menu=Button(chat,"ShellAvatar").ContextMenu;var entry=menu.Items.OfType<MenuItem>().Single(x=>x.Name=="ProfileCompanionHome");
            Check(entry.IsEnabled&&entry.Focusable&&AutomationProperties.GetName(entry).Contains(pet.Skin.DisplayName),"profile row is enabled, keyboard focusable and accessible");
            Check(((Image)entry.Icon).Source==SkinCatalog.GetAvatar(pet.Skin)&&entry.Opacity==1,"profile avatar retains canonical original color");
            entry.Focus();Check(entry.IsKeyboardFocused,"profile accepts real keyboard focus");entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(entry),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});await Task.Delay(80);Check(!menu.IsOpen&&chat.CurrentPage=="companion","profile click closes menu and opens companion route in same window");
            var page=All<CompanionHomePage>(chat).Single();await page.RefreshAsync();page.UpdateLayout();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            var hostedTrack=await LoadedTrack(page);Check(All<TextBlock>(hostedTrack).All(StageWordsStayWhole),"hosted home stage words do not split across rendered lines");
            Check(All<Image>(page).Any(x=>x.Name=="CompanionProfileScene"&&x.Source!=null),"actual earned profile shows its original stage scene");
            CheckProfile(page,pet.Skin,"familiar");
            if(hostedOnly){foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{520,1280}){chat.Width=width;ThemeManager.Apply(theme);await page.RefreshAsync();var ready=await LoadedTrack(page);Check(All<TextBlock>(ready).All(StageWordsStayWhole),"settled hosted stage words stay whole "+locale+" "+theme+" "+width);Capture(chat,Path.Combine(captures,locale+"-hosted-ready-"+theme+"-"+width+".png"));}Console.WriteLine("HostedHomeReady: "+checks+" passed");return;}
            Check(All<TextBlock>(page).Any(x=>x.Name=="CompanionCurrentWelcome"&&x.Text==RelationshipContent.Welcome(page.View.Snapshot.StageId,pet.RelationshipPack)),"current saved stage has its own truthful welcome message");
            chat.Width=520;chat.Height=880;var narrowRules=All<Expander>(page).Single(x=>x.Name=="CompanionGrowthRules");narrowRules.IsExpanded=true;
            foreach(string narrowTheme in new[]{"light","dark"}){
                ThemeManager.Apply(narrowTheme);await Task.Delay(60);page.UpdateLayout();narrowRules=All<Expander>(page).Single(x=>x.Name=="CompanionGrowthRules");narrowRules.BringIntoView();await Task.Delay(30);Capture(chat,Path.Combine(captures,"fix1-"+locale+"-rules-520-"+narrowTheme+".png"));
                foreach(string ruleText in new[]{TextResources.Get("relationship.home.ruleInteraction"),TextResources.Get("relationship.home.ruleConversation")}){var rule=All<TextBlock>(narrowRules).Single(x=>x.Text==ruleText);double right=rule.TransformToAncestor(narrowRules).Transform(new Point(rule.ActualWidth,0)).X;Check(right<=narrowRules.ActualWidth+1,"localized expanded rule stays inside narrow group: "+locale+" "+narrowTheme);}
            }
            narrowRules.IsExpanded=false;chat.Width=1100;page.ScrollToTop();await Task.Delay(30);
            var focusApi=typeof(HomeVisual).GetMethod("CaptureFocus",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Check(focusApi!=null,"semantic focus capture identifies later milestone actions and disclosure headers");
            var laterCard=All<RelationshipCardPreview>(page).Single(x=>((HomeCardCapture)x.Tag).Data.StageId=="familiar");
            var laterSave=All<Button>(laterCard).Single(x=>x.Name=="SaveRelationshipCard");var laterThumb=All<Button>(laterCard).Single(x=>x.Name=="RelationshipCardThumbnail");var laterDisclosure=All<Expander>(laterCard).Single(x=>x.Name=="RelationshipCardPreview");
            var saveFocus=HomeVisual.CaptureFocus(page,laterSave);var thumbFocus=HomeVisual.CaptureFocus(page,laterThumb);var cardHeaderFocus=HomeVisual.CaptureFocus(page,HomeVisual.DisclosureHeader(laterDisclosure));var rulesHeaderFocus=HomeVisual.CaptureFocus(page,HomeVisual.DisclosureHeader(All<Expander>(page).Single(x=>x.Name=="CompanionGrowthRules")));
            Check(HomeVisual.CaptureFocus(page,input)==null,"focus capture rejects controls outside the companion page");
            await page.RefreshAsync();page.UpdateLayout();laterCard=All<RelationshipCardPreview>(page).Single(x=>((HomeCardCapture)x.Tag).Data.StageId=="familiar");
            Check(ReferenceEquals(HomeVisual.ResolveFocus(page,saveFocus),All<Button>(laterCard).Single(x=>x.Name=="SaveRelationshipCard"))&&ReferenceEquals(HomeVisual.ResolveFocus(page,thumbFocus),All<Button>(laterCard).Single(x=>x.Name=="RelationshipCardThumbnail")),"rebuilt visual tree resolves later milestone save and thumbnail, never the first card");
            Check(ReferenceEquals(HomeVisual.ResolveFocus(page,cardHeaderFocus),HomeVisual.DisclosureHeader(All<Expander>(laterCard).Single(x=>x.Name=="RelationshipCardPreview"))),"rebuilt visual tree resolves the later card disclosure header");
            Check(ReferenceEquals(HomeVisual.ResolveFocus(page,rulesHeaderFocus),HomeVisual.DisclosureHeader(All<Expander>(page).Single(x=>x.Name=="CompanionGrowthRules"))),"rebuilt visual tree resolves the rules disclosure header");
            Console.WriteLine("WPF_RESOLVER_ONLY: actual rebuilt controls checked; real input-desktop keyboard focus remains the separate SKIP.");
            var cardData=RelationshipCardData.FromSnapshot(page.View.Snapshot,"familiar",pet.Skin.DisplayName);
            var previews=All<Expander>(page).Where(x=>x.Name=="RelationshipCardPreview").ToArray();
            Check(previews.Length==page.View.Snapshot.State.Milestones.Count,"every saved milestone shows an in-page PNG preview");
            string[] beforePreview=Directory.GetFiles(root,"*",SearchOption.AllDirectories);var thumbnailAction=All<Button>((DependencyObject)previews[0].Parent).Single(x=>x.Name=="RelationshipCardThumbnail");Check(AutomationProperties.GetName(thumbnailAction).Contains(pet.Skin.DisplayName),"thumbnail action describes the captured companion");Click(thumbnailAction);page.UpdateLayout();Check(previews[0].IsExpanded,"thumbnail button expands inline preview");
            Check(beforePreview.OrderBy(x=>x).SequenceEqual(Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(x=>x)),"expanding a keepsake never creates files");
            var previewImage=All<Image>((DependencyObject)previews[0].Parent).First(x=>x.Name=="RelationshipCardImage");
            Check(ReferenceEquals(previewImage.Source,All<Image>(previews[0]).Single(x=>x.Name=="RelationshipCardExpandedImage").Source),"thumbnail and expansion share decoded PNG");
            Check(((BitmapSource)previewImage.Source).PixelWidth==800&&((BitmapSource)previewImage.Source).PixelHeight==480,"displayed preview decodes actual 800 by 480 PNG");
            await page.RefreshAsync();page.UpdateLayout();Check(All<Expander>(page).First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"background refresh preserves expanded preview");
            ThemeManager.Apply("light");await Task.Delay(30);var lightCapture=All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).Single(x=>x.Data.StageId=="familiar");
            page.UpdateLayout();page.ScrollToVerticalOffset(120);page.UpdateLayout();double themeOffset=page.VerticalOffset;ThemeManager.Apply("dark");await Task.Delay(30);page.UpdateLayout();var darkCapture=All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).Single(x=>x.Data.StageId=="familiar");
            var leftLight=Pixel(lightCapture.Image,100,200);var leftDark=Pixel(darkCapture.Image,100,200);var readingLight=Pixel(lightCapture.Image,790,240);var readingDark=Pixel(darkCapture.Image,790,240);
            Check(leftLight.SequenceEqual(leftDark),"scene preserves original left-side colors across themes");Check(readingDark.Take(3).Sum(x=>(int)x)<readingLight.Take(3).Sum(x=>(int)x),"theme-specific gradient changes the right reading area");Check(LightTextInk(darkCapture.Image),"dark scene contains contrasting title ink");Check(themeOffset>0&&Math.Abs(page.VerticalOffset-themeOffset)<2,"theme recapture preserves actual nonzero scroll");Check(!lightCapture.Png.SequenceEqual(darkCapture.Png),"theme change invalidates the captured PNG cache");Check(All<Expander>(page).First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"theme recapture preserves expanded preview");
            var capture=All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).Single(x=>x.Data.StageId=="familiar");
            var png=capture.Png;Check(capture.Data.CompanionId==page.View.CompanionId&&capture.Data.Name==pet.Skin.DisplayName&&capture.Data.EarnedLocalDate==cardData.EarnedLocalDate,"preview capture binds saved identity, name and milestone date");
            string exported=Path.Combine(captures,"keepsake-"+locale+".png");
            Check(await RelationshipCardFile.SaveAsync(exported,png)==RelationshipCardSaveResult.Saved,"actual PNG is atomically saved");Check(File.ReadAllBytes(exported).SequenceEqual(capture.Png),"export uses byte-for-byte captured preview PNG");
            using(var stream=File.OpenRead(exported)){var decoded=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoded.Frames[0];Check(frame.PixelWidth==800&&frame.PixelHeight==480,"export decodes as 800 by 480 PNG");Check(Ink(frame,new Int32Rect(32,64,296,360))&&Ink(frame,new Int32Rect(360,100,408,110))&&Ink(frame,new Int32Rect(360,350,408,100)),"decoded PNG contains actual avatar, name/stage and date/footer ink");}
            var cardVisual=RelationshipCardRenderer.Create(cardData,SkinCatalog.GetAvatar(pet.Skin));cardVisual.Measure(new Size(800,480));cardVisual.Arrange(new Rect(0,0,800,480));cardVisual.UpdateLayout();
            Check(All<TextBlock>(cardVisual).Any(t=>t.Text==pet.Skin.DisplayName)&&All<TextBlock>(cardVisual).Any(t=>t.Text==cardData.StageName)&&All<TextBlock>(cardVisual).Any(t=>t.Text==cardData.DateText),"native rendered text comes from saved identity, stage and original date");foreach(var label in All<TextBlock>(cardVisual)){Check(label.ActualWidth<=408.1&&label.ActualHeight>0,"fixed keepsake text owns finite bounds: "+label.Name);}
            Check(All<Button>(page).Count(b=>b.Name=="SaveRelationshipCard")==page.View.Snapshot.State.Milestones.Count,"every saved home card has export action");
            Check(page.View.Snapshot.State.Score==156&&All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipScore").Text=="156 / 240","home opening records actual saved visit");
            Check(All<TextBlock>(page).Single(x=>x.Name=="CompanionActiveDays").Text.Contains("16")&&All<TextBlock>(page).Single(x=>x.Name=="CompanionAcquaintanceDays").Text.Contains("21"),"active and acquaintance days have independent named fields");
            var beforeAppearance=All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).First();var currentSkin=pet.Skin;var refreshedSkin=new SkinDefinition(currentSkin.Id,currentSkin.Name,currentSkin.Source,currentSkin.Preview,currentSkin.Embedded,currentSkin.SystemPrompt,currentSkin.Description){BuiltinCharacterId=currentSkin.BuiltinCharacterId,FixtureEmbedded=currentSkin.FixtureEmbedded};
            // This owned appearance fixture uses embedded art, independent of background official download receipts.
            refreshedSkin.FixtureEmbedded=true;
            typeof(PetWindow).GetMethod("RefreshOfficialAppearance",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(pet,new object[]{refreshedSkin});page.UpdateLayout();
            Check(page.View.CompanionId==beforeAppearance.Data.CompanionId&&!ReferenceEquals(beforeAppearance,All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).First()),"actual same-identity appearance notification refreshes captured avatar cards");
            pet.ShowSettings();Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="companion","back from settings restores companion");Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="home"&&input.Text=="relationship route draft","back restores original chat draft");Click(Button(chat,"AppForward"));Check(chat.CurrentPage=="companion","forward restores home page");chat.ShowImagesPage();Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="companion","image route participates in companion history");
            var originalSkin=pet.Skin;var originalId=pet.RelationshipCompanionId;var otherSkin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=originalSkin.PickerKey);
            string otherId=RelationshipIdentity.Resolve(root,otherSkin.PickerKey,otherSkin.IsOfficial?otherSkin.BuiltinCharacterId:null,otherSkin.Id);
            await Task.Run(()=>RelationshipHomeFixtures.SeedMaximum(pet.RelationshipService,otherId));
            await SelectCommitted(otherSkin,chat,"actual companion selection");Check(chat.CurrentPage=="companion","skin change retains open companion route");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            Check(page.View.CompanionId==otherId&&page.View.Progress.Score==1000&&page.View.Progress.NextStageId==null&&((Image)All<Image>(page).Single(x=>x.Name=="CompanionHomeAvatar")).Source==SkinCatalog.GetAvatar(otherSkin),"identity switch binds canonical avatar to its own maxed saved score");
            foreach(string matrixTheme in new[]{"light","dark"}){ThemeManager.Apply(matrixTheme);await Task.Delay(30);foreach(var item in All<RelationshipCardPreview>(page)){var card=(HomeCardCapture)item.Tag;string matrix=Path.Combine(captures,"cards",locale+"-"+card.Data.StageId+"-"+matrixTheme+".png");Directory.CreateDirectory(Path.GetDirectoryName(matrix));File.WriteAllBytes(matrix,card.Png);}}

            foreach(string maximumTheme in new[]{"light","dark"}){ThemeManager.Apply(maximumTheme);await Task.Delay(40);Capture(chat,Path.Combine(captures,locale+"-maximum-"+maximumTheme+".png"));}
            await SelectCommitted(originalSkin,chat,"original companion restore");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.CompanionId==originalId&&page.View.Progress.Score==156,"switch back restores independent saved score");
            string introRoot=Path.Combine(root,"introduction-source");Directory.CreateDirectory(introRoot);File.Copy(Path.Combine(Environment.CurrentDirectory,"assets","e-bao-spritesheet.png"),Path.Combine(introRoot,"spritesheet.png"));
            var introManifest=new SkinManifest{Version=1,Name="Long introduction fixture",Atlas="spritesheet.png",SystemPrompt="Fixture",Description=new string('x',220),Relationship=new RelationshipPackContent{Stages=new System.Collections.Generic.Dictionary<string,RelationshipStageContent>{{"first_meeting",new RelationshipStageContent{Welcome=new System.Collections.Generic.Dictionary<string,string>{{locale,String.Concat(Enumerable.Repeat("This is the saved welcome supplied by this companion. ",5))}}}}}}};string introFile=Path.Combine(introRoot,"pet.json");using(var introStream=File.Create(introFile))SkinCatalog.Serializer().WriteObject(introStream,introManifest);var longSkin=pet.Catalog.Import(introFile);// Legacy import intentionally drops extensions; attach a validated owned content fixture to test presentation, not import support.
            RelationshipContent.Validate(introManifest.Relationship,AnimationPlayer.States);typeof(SkinDefinition).GetProperty("Manifest").SetValue(longSkin,introManifest,null);
            Check(pet.SelectSkin(longSkin),"owned long-introduction companion selects");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await Task.Run(()=>pet.RelationshipService.Record(pet.RelationshipCompanionId,RelationshipEventType.Visit,"f3-long-welcome",RelationshipTime.Current()));await page.RefreshAsync();page.UpdateLayout();var intro=All<Expander>(page).Single(x=>x.Name=="CompanionIntroduction");intro.IsExpanded=true;await page.RefreshAsync();page.UpdateLayout();Check(All<Expander>(page).Single(x=>x.Name=="CompanionIntroduction").IsExpanded,"long introduction expansion survives refresh");var welcome=All<Expander>(page).Single(x=>x.Name=="CompanionWelcome");welcome.IsExpanded=true;var welcomeFocus=HomeVisual.CaptureFocus(page,HomeVisual.DisclosureHeader(welcome));await page.RefreshAsync();page.UpdateLayout();Check(All<Expander>(page).Single(x=>x.Name=="CompanionWelcome").IsExpanded&&ReferenceEquals(HomeVisual.ResolveFocus(page,welcomeFocus),HomeVisual.DisclosureHeader(All<Expander>(page).Single(x=>x.Name=="CompanionWelcome"))),"long saved welcome keeps accessible full text expansion and semantic focus target");foreach(string welcomeTheme in new[]{"light","dark"}){ThemeManager.Apply(welcomeTheme);await Task.Delay(30);Capture(chat,Path.Combine(captures,locale+"-long-welcome-"+welcomeTheme+".png"));}await SelectCommitted(originalSkin,chat,"later original companion restore");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            {
                await page.RefreshAsync();page.UpdateLayout();
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{520,1100,1320}){ThemeManager.Apply(theme);chat.Width=width;chat.Height=880;await Task.Delay(90);var stage=All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipStage");var avatar=All<Image>(page).Single(x=>x.Name=="CompanionHomeAvatar");var stageBounds=stage.TransformToAncestor(page).Transform(new Point(0,0));var avatarBounds=avatar.TransformToAncestor(page).Transform(new Point(0,0));Check(page.ActualWidth<834?stageBounds.Y>avatarBounds.Y+avatar.ActualHeight:stageBounds.X>avatarBounds.X+avatar.ActualWidth,"responsive profile and relationship bounds "+width+" actual="+page.ActualWidth);if(width==1320){var realCards=All<RelationshipCardPreview>(page).ToArray();var first=realCards[0];var second=realCards[1];var firstPoint=first.TransformToAncestor(page).Transform(new Point());var secondPoint=second.TransformToAncestor(page).Transform(new Point());Console.WriteLine("CARD_LAYOUT "+theme+" wrap="+((FrameworkElement)first.Parent).ActualWidth+" first="+first.ActualWidth+" width="+first.Width+" expanded="+((HomeCardCapture)first.Tag).Expanded+" second="+second.ActualWidth+" y="+firstPoint.Y+","+secondPoint.Y);Check(first.ActualWidth>=320&&second.ActualWidth>=320&&Math.Abs(firstPoint.Y-secondPoint.Y)<2,"wide album has two actually readable cards on the same row");}Capture(chat,Path.Combine(captures,locale+"-"+theme+"-"+(int)chat.ActualWidth+".png"));Check(page.ActualWidth<=chat.ActualWidth,"bounded "+locale+" "+theme+" "+width);}
            }
            var exp=All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings");exp.IsExpanded=true;await Task.Delay(30);var growth=All<CheckBox>(page).Single(x=>x.Name=="RelationshipGrowthEnabled");typeof(CheckBox).GetMethod("OnClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(growth,null);await page.PendingSettings;await page.RefreshAsync();page.UpdateLayout();
            Check(!pet.RelationshipService.GetSnapshot(pet.RelationshipCompanionId).Settings.GrowthEnabled,"growth checkbox persists shared global settings");
            var savedBeforeZero=page.View;var zeroSkin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=originalSkin.PickerKey&&x.PickerKey!=otherSkin.PickerKey);Check(pet.SelectSkin(zeroSkin),"unmet companion selects while growth is off");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.Snapshot.State==null&&page.View.Progress.Score==0,"unmet companion shows true empty zero state without invented dates");Check(!All<TextBlock>(page).Any(x=>x.Name=="CompanionCurrentWelcome"),"uninitialized ledger never synthesizes first-meeting welcome");typeof(CompanionHomePage).GetProperty("View").SetValue(page,savedBeforeZero,null);ThemeManager.Apply(ThemeManager.IsDark?"light":"dark");page.UpdateLayout();Check(All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipScore").Text==RelationshipProgress.FromScore(0).ScoreText,"normal home suite rejects stale high facts during fresh-owner theme refresh");await page.RefreshAsync();page.UpdateLayout();foreach(string zeroTheme in new[]{"light","dark"}){ThemeManager.Apply(zeroTheme);await Task.Delay(40);Capture(chat,Path.Combine(captures,locale+"-zero-"+zeroTheme+".png"));}await SelectCommitted(originalSkin,chat,"later original companion restore");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.CompanionId==originalId,"zero-state return settles original identity");
            page.UpdateLayout();All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings").IsExpanded=true;page.UpdateLayout();string file=Path.Combine(root,"relationships","state.json");byte[] original=File.ReadAllBytes(file);File.WriteAllText(file,"{broken");var feedback=All<CheckBox>(page).Single(x=>x.Name=="RelationshipFeedbackEnabled");typeof(CheckBox).GetMethod("OnClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(feedback,null);await page.PendingSettings;await page.RefreshAsync();page.UpdateLayout();Check(All<CheckBox>(page).Single(x=>x.Name=="RelationshipFeedbackEnabled").IsChecked==true&&File.ReadAllText(file)=="{broken","failed settings save retains committed checkbox and original bytes");
            Check(page.View.ReadFailed&&page.View.Snapshot.State.Score==156&&All<TextBlock>(page).Any(x=>x.Name=="RelationshipWarning"&&x.Visibility==Visibility.Visible),"corrupt ledger preserves saved view and shows retry warning");
            Capture(chat,Path.Combine(captures,locale+"-read-failure.png"));File.WriteAllBytes(file,original);await page.RefreshAsync();page.UpdateLayout();
            var context=All<CheckBox>(page).Single(x=>x.Name=="RelationshipAiContextEnabled");var priorSave=page.PendingSettings;
            using(var lease=new FileStream(Path.Combine(root,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                Click(Button(page,"RelationshipRetry"));
                Check(!context.IsEnabled,"retry immediately disables the already-rendered settings controls");
                context.IsChecked=false;context.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Check(ReferenceEquals(priorSave,page.PendingSettings)&&context.IsChecked==true,"settings handler rejects concurrent admission during retry");
                var admittedRetry=page.PendingRetry;Click(Button(page,"RelationshipRetry"));Check(ReferenceEquals(admittedRetry,page.PendingRetry),"retry handler rejects duplicate admission while busy");
            }
            await page.PendingRetry;await page.RefreshAsync();page.UpdateLayout();Check(!page.View.ReadFailed&&!pet.RelationshipService.GetSnapshot(originalId).Settings.FeedbackEnabled,"retry recovers preserved ledger and requested settings");
            var settingsModel=new RelationshipHomeModel(pet.Relationships);Check((await settingsModel.UpdateSettingsAsync(true,true,true)).Success,"feedback window fixture enables saved feedback");
            All<Expander>(page).Single(x=>x.Name=="CompanionGrowthRules").IsExpanded=true;All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings").IsExpanded=true;All<Expander>(page).First(x=>x.Name=="RelationshipCardPreview").IsExpanded=true;page.UpdateLayout();page.ScrollToVerticalOffset(160);page.UpdateLayout();double priorOffset=page.VerticalOffset;
            await pet.Relationships.Enqueue(originalId,RelationshipEventType.CompletedConversation,"feedback-window-"+Guid.NewGuid().ToString("N"),RelationshipTime.Current());await page.RefreshAsync();page.UpdateLayout();
            Check(All<Expander>(page).Single(x=>x.Name=="CompanionGrowthRules").IsExpanded&&All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings").IsExpanded&&All<Expander>(page).First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"real saved growth refresh preserves rules, settings and preview expansion");
            Check(Math.Abs(page.VerticalOffset-priorOffset)<2,"real growth refresh preserves reasonable scroll offset");Capture(chat,Path.Combine(captures,locale+"-expanded-refresh.png"));
            deadline.Restart();while(!app.Windows.OfType<RelationshipFeedbackWindow>().Any()){if(deadline.ElapsedMilliseconds>10000)throw new TimeoutException("Saved feedback notice not displayed");await Task.Delay(10);}
            var savedNotice=app.Windows.OfType<RelationshipFeedbackWindow>().Single();await CaptureFeedback(savedNotice,captures,locale,"gain");await page.RefreshAsync();page.UpdateLayout();All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings").IsExpanded=true;page.UpdateLayout();
            byte[] settingsBytes=File.ReadAllBytes(file);File.WriteAllText(file,"{broken");
            var failedFeedback=All<CheckBox>(page).Single(x=>x.Name=="RelationshipFeedbackEnabled");typeof(CheckBox).GetMethod("OnClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(failedFeedback,null);await page.PendingSettings;
            Check(savedNotice.IsVisible,"failed feedback settings save does not dismiss a visible notice");File.WriteAllBytes(file,settingsBytes);await page.RefreshAsync();page.UpdateLayout();All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings").IsExpanded=true;page.UpdateLayout();
            var savedFeedback=All<CheckBox>(page).Single(x=>x.Name=="RelationshipFeedbackEnabled");typeof(CheckBox).GetMethod("OnClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(savedFeedback,null);await page.PendingSettings;
            deadline.Restart();while(savedNotice.IsVisible&&deadline.ElapsedMilliseconds<1500)await Task.Delay(10);
            Check(!savedNotice.IsVisible,"successfully disabling feedback through home settings promptly closes the actual notice");
            Check((await settingsModel.UpdateSettingsAsync(true,true,true)).Success,"feedback can be re-enabled after dismissal");await Task.Delay(650);
            Check(!app.Windows.OfType<RelationshipFeedbackWindow>().Any(),"re-enabling feedback never replays the dismissed notice");
            await Task.Run(()=>RelationshipHomeFixtures.ReachDailyLimit(pet.RelationshipService,originalId));await page.RefreshAsync();page.UpdateLayout();
            Check(page.View.DailyLimitReached&&All<TextBlock>(page).Any(x=>x.Text==TextResources.Get("relationship.home.dailyLimit")),"saved 20-point daily cap has a visible explanation");
            await settingsModel.UpdateSettingsAsync(false,false,true);await page.RefreshAsync();page.UpdateLayout();Check(page.View.GrowthPaused&&page.View.DailyLimitReached,"daily cap and global pause remain independently visible");
            page.ScrollToTop();foreach(string capTheme in new[]{"light","dark"}){ThemeManager.Apply(capTheme);await Task.Delay(40);Capture(chat,Path.Combine(captures,locale+"-paused-cap-"+capTheme+".png"));}
            string blockedRoot=Path.Combine(root,"blocked-fixture");Directory.CreateDirectory(Path.Combine(blockedRoot,"relationships"));string blockedFile=Path.Combine(blockedRoot,"relationships","pending.json");File.WriteAllText(blockedFile,"{broken");
            var blockedPet=new PetWindow(null,new PetSettings{UiLanguage=locale,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",QuietMode=true},new SettingsStore(Path.Combine(blockedRoot,"settings.json")),null);
            using(var blockedPage=new CompanionHomePage(blockedPet)){
                var blockedWindow=new Window{Width=900,Height=780,Content=blockedPage};blockedWindow.Show();
                try{await blockedPage.RefreshAsync();blockedPage.UpdateLayout();Check(Button(blockedPage,"RelationshipRestart").Visibility==Visibility.Visible&&Button(blockedPage,"RelationshipRetry").Visibility!=Visibility.Visible,"blocked queue offers truthful restart recovery rather than unusable retry");Check(File.ReadAllText(blockedFile)=="{broken","blocked recovery view preserves the original queue");foreach(string blockedTheme in new[]{"light","dark"}){ThemeManager.Apply(blockedTheme);await Task.Delay(40);Capture(blockedWindow,Path.Combine(captures,locale+"-blocked-outbox-"+blockedTheme+".png"));}}
                finally{blockedWindow.Close();blockedPet.Close();}
            }
            Console.WriteLine("CompanionHomeUiTests: "+checks+" passed");
        }
        [STAThread]public static int Main(string[] args){if(args.Contains("--compile-only"))return 0;string profile=args.SingleOrDefault(x=>x.StartsWith("--profile-art=")||x.StartsWith("--profile-fresh="));string stage=args.SingleOrDefault(x=>x.StartsWith("--stage-track-layout=")),hosted=args.SingleOrDefault(x=>x.StartsWith("--hosted-ready="));string locale=profile!=null?profile.Split('=')[1]:hosted!=null?hosted.Split('=')[1]:stage!=null?stage.Split('=')[1]:args.Length==1&&!args[0].StartsWith("--")?args[0]:"en_US";TextResources.ConfigureStartupLanguage(locale);app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{if(profile!=null)await ProfileArtwork(locale,profile.StartsWith("--profile-fresh="));else if(args.Contains("--heart-icons"))HeartIcons();else if(hosted!=null)await Run(locale,true);else if(stage!=null)await StageTrackLayout(locale);else if(args.Contains("--first-visit"))await FirstVisit();else await Run(locale);result=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
