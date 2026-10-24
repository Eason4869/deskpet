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
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(root,i)))yield return x;}
        static Button Button(DependencyObject root,string name){return All<Button>(root).Single(x=>x.Name==name);}
        static void Click(Button b){b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
        static bool Ink(BitmapSource source,Int32Rect region)
        {var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var bytes=new byte[region.Width*region.Height*4];converted.CopyPixels(region,bytes,region.Width*4,0);int ink=0;for(int i=0;i<bytes.Length;i+=4)if(bytes[i]+bytes[i+1]+bytes[i+2]<650&&bytes[i+3]>200)ink++;return ink>100;}
        static bool LightTextInk(BitmapSource bitmap){var source=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var bytes=new byte[500*168*4];source.CopyPixels(new Int32Rect(242,95,500,168),bytes,500*4,0);int count=0;for(int i=0;i<bytes.Length;i+=4)if(bytes[i]+bytes[i+1]+bytes[i+2]>600)count++;return count>100;}
        static bool DarkPng(BitmapSource bitmap){var source=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var pixel=new byte[4];source.CopyPixels(new Int32Rect(0,0,1,1),pixel,4,0);return pixel[0]+pixel[1]+pixel[2]<200;}
        static void Capture(FrameworkElement w,string file){w.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(file))png.Save(f);}
        static async Task CaptureFeedback(RelationshipFeedbackWindow window,string captures,string locale,string kind)
        {
            foreach(string theme in new[]{"light","dark"}){
                ThemeManager.Apply(theme);await Task.Delay(60);Capture(window,Path.Combine(captures,locale+"-"+theme+"-"+kind+"-feedback.png"));
                var name=All<TextBlock>(window).Single(t=>t.ToolTip as string==pet.Skin.DisplayName);string original=name.Text;
                name.Text="很长的伙伴名字 · A companion with a long localized name";window.Width=260;await Task.Delay(30);Capture(window,Path.Combine(captures,locale+"-"+theme+"-"+kind+"-feedback-narrow.png"));name.Text=original;window.Width=350;
            }
        }
        static async Task Run(string locale)
        {
            string captures=Path.GetFullPath(Path.Combine("work","relationship-validation","s2","wpf"));Directory.CreateDirectory(captures);
            string root=Path.Combine(captures,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
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
            Check(DarkPng(darkCapture.Image),"dark theme captures a dark PNG background, not a white card");var cardPixel=new byte[4];new FormatConvertedBitmap(darkCapture.Image,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(0,0,1,1),cardPixel,4,0);var surface=((SolidColorBrush)ThemeManager.Surface).Color;Check(cardPixel[0]!=surface.B||cardPixel[1]!=surface.G||cardPixel[2]!=surface.R,"dark PNG card surface is visibly distinct from actual page surface");Check(!DarkPng(lightCapture.Image)&&LightTextInk(darkCapture.Image),"light card stays light and dark card name and stage have contrasting ink");Check(themeOffset>0&&Math.Abs(page.VerticalOffset-themeOffset)<2,"theme recapture preserves actual nonzero scroll");Check(!lightCapture.Png.SequenceEqual(darkCapture.Png),"theme change invalidates the captured PNG cache");Check(All<Expander>(page).First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"theme recapture preserves expanded preview");
            var capture=All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).Single(x=>x.Data.StageId=="familiar");
            var png=capture.Png;Check(capture.Data.CompanionId==page.View.CompanionId&&capture.Data.Name==pet.Skin.DisplayName&&capture.Data.EarnedLocalDate==cardData.EarnedLocalDate,"preview capture binds saved identity, name and milestone date");
            string exported=Path.Combine(captures,"keepsake-"+locale+".png");
            Check(await RelationshipCardFile.SaveAsync(exported,png)==RelationshipCardSaveResult.Saved,"actual PNG is atomically saved");Check(File.ReadAllBytes(exported).SequenceEqual(capture.Png),"export uses byte-for-byte captured preview PNG");
            using(var stream=File.OpenRead(exported)){var decoded=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoded.Frames[0];Check(frame.PixelWidth==800&&frame.PixelHeight==480,"export decodes as 800 by 480 PNG");Check(Ink(frame,new Int32Rect(42,95,168,168))&&Ink(frame,new Int32Rect(242,95,500,168))&&Ink(frame,new Int32Rect(42,280,700,90)),"decoded PNG contains actual avatar, name/stage and date/footer ink");}
            var cardVisual=RelationshipCardRenderer.Create(cardData,SkinCatalog.GetAvatar(pet.Skin));cardVisual.Measure(new Size(800,480));cardVisual.Arrange(new Rect(0,0,800,480));cardVisual.UpdateLayout();
            Check(All<TextBlock>(cardVisual).Any(t=>t.Text==pet.Skin.DisplayName)&&All<TextBlock>(cardVisual).Any(t=>t.Text==cardData.StageName)&&All<TextBlock>(cardVisual).Any(t=>t.Text==cardData.DateText),"native rendered text comes from saved identity, stage and original date");
            Check(All<Button>(page).Count(b=>b.Name=="SaveRelationshipCard")==page.View.Snapshot.State.Milestones.Count,"every saved home card has export action");
            Check(page.View.Snapshot.State.Score==156&&All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipScore").Text=="156 / 240","home opening records actual saved visit");
            Check(All<TextBlock>(page).Single(x=>x.Name=="CompanionActiveDays").Text.Contains("16")&&All<TextBlock>(page).Single(x=>x.Name=="CompanionAcquaintanceDays").Text.Contains("21"),"active and acquaintance days have independent named fields");
            var beforeAppearance=All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).First();var currentSkin=pet.Skin;var refreshedSkin=new SkinDefinition(currentSkin.Id,currentSkin.Name,currentSkin.Source,currentSkin.Preview,currentSkin.Embedded,currentSkin.SystemPrompt,currentSkin.Description){BuiltinCharacterId=currentSkin.BuiltinCharacterId,FixtureEmbedded=currentSkin.FixtureEmbedded};
            typeof(PetWindow).GetMethod("RefreshOfficialAppearance",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(pet,new object[]{refreshedSkin});page.UpdateLayout();
            Check(page.View.CompanionId==beforeAppearance.Data.CompanionId&&!ReferenceEquals(beforeAppearance,All<RelationshipCardPreview>(page).Select(x=>(HomeCardCapture)x.Tag).First()),"actual same-identity appearance notification refreshes captured avatar cards");
            pet.ShowSettings();Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="companion","back from settings restores companion");Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="home"&&input.Text=="relationship route draft","back restores original chat draft");Click(Button(chat,"AppForward"));Check(chat.CurrentPage=="companion","forward restores home page");chat.ShowImagesPage();Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="companion","image route participates in companion history");
            var originalSkin=pet.Skin;var originalId=pet.RelationshipCompanionId;var otherSkin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=originalSkin.PickerKey);
            string otherId=RelationshipIdentity.Resolve(root,otherSkin.PickerKey,otherSkin.IsOfficial?otherSkin.BuiltinCharacterId:null,otherSkin.Id);
            await Task.Run(()=>RelationshipHomeFixtures.SeedMaximum(pet.RelationshipService,otherId));
            Check(pet.SelectSkin(otherSkin),"actual committed companion selection succeeds");Check(chat.CurrentPage=="companion","skin change retains open companion route");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            Check(page.View.CompanionId==otherId&&page.View.Progress.Score==1000&&page.View.Progress.NextStageId==null&&((Image)All<Image>(page).Single(x=>x.Name=="CompanionHomeAvatar")).Source==SkinCatalog.GetAvatar(otherSkin),"identity switch binds canonical avatar to its own maxed saved score");
            foreach(string maximumTheme in new[]{"light","dark"}){ThemeManager.Apply(maximumTheme);await Task.Delay(40);Capture(chat,Path.Combine(captures,locale+"-maximum-"+maximumTheme+".png"));}Check(pet.SelectSkin(originalSkin),"original companion restores");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.CompanionId==originalId&&page.View.Progress.Score==156,"switch back restores independent saved score");
            string introRoot=Path.Combine(root,"introduction-source");Directory.CreateDirectory(introRoot);File.Copy(Path.Combine(Environment.CurrentDirectory,"assets","e-bao-spritesheet.png"),Path.Combine(introRoot,"spritesheet.png"));
            var introManifest=new SkinManifest{Version=1,Name="Long introduction fixture",Atlas="spritesheet.png",SystemPrompt="Fixture",Description=new string('x',220)};string introFile=Path.Combine(introRoot,"pet.json");using(var introStream=File.Create(introFile))new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SkinManifest)).WriteObject(introStream,introManifest);var longSkin=pet.Catalog.Import(introFile);
            Check(pet.SelectSkin(longSkin),"owned long-introduction companion selects");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();var intro=All<Expander>(page).Single(x=>x.Name=="CompanionIntroduction");intro.IsExpanded=true;await page.RefreshAsync();page.UpdateLayout();Check(All<Expander>(page).Single(x=>x.Name=="CompanionIntroduction").IsExpanded,"long introduction expansion survives refresh");pet.SelectSkin(originalSkin);chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            {
                await page.RefreshAsync();page.UpdateLayout();
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{520,1100,1320}){ThemeManager.Apply(theme);chat.Width=width;chat.Height=880;await Task.Delay(90);var stage=All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipStage");var avatar=All<Image>(page).Single(x=>x.Name=="CompanionHomeAvatar");var stageBounds=stage.TransformToAncestor(page).Transform(new Point(0,0));var avatarBounds=avatar.TransformToAncestor(page).Transform(new Point(0,0));Check(page.ActualWidth<834?stageBounds.Y>avatarBounds.Y+avatar.ActualHeight:stageBounds.X>avatarBounds.X+avatar.ActualWidth,"responsive profile and relationship bounds "+width+" actual="+page.ActualWidth);Capture(chat,Path.Combine(captures,locale+"-"+theme+"-"+(int)chat.ActualWidth+".png"));Check(page.ActualWidth<=chat.ActualWidth,"bounded "+locale+" "+theme+" "+width);}
            }
            var exp=All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings");exp.IsExpanded=true;await Task.Delay(30);var growth=All<CheckBox>(page).Single(x=>x.Name=="RelationshipGrowthEnabled");typeof(CheckBox).GetMethod("OnClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(growth,null);await page.PendingSettings;await page.RefreshAsync();page.UpdateLayout();
            Check(!pet.RelationshipService.GetSnapshot(pet.RelationshipCompanionId).Settings.GrowthEnabled,"growth checkbox persists shared global settings");
            var zeroSkin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=originalSkin.PickerKey&&x.PickerKey!=otherSkin.PickerKey);Check(pet.SelectSkin(zeroSkin),"unmet companion selects while growth is off");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.Snapshot.State==null&&page.View.Progress.Score==0,"unmet companion shows true empty zero state without invented dates");foreach(string zeroTheme in new[]{"light","dark"}){ThemeManager.Apply(zeroTheme);await Task.Delay(40);Capture(chat,Path.Combine(captures,locale+"-zero-"+zeroTheme+".png"));}pet.SelectSkin(originalSkin);chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.CompanionId==originalId,"zero-state return settles original identity");
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
        [STAThread]public static int Main(string[] args){string locale=args.Length==1?args[0]:"en_US";TextResources.ConfigureStartupLanguage(locale);app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(locale);result=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
