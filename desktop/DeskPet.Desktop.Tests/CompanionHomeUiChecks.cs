using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Automation;
using Avalonia.LogicalTree;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Dafeiyu;
using DeskPet.Desktop;

internal static class CompanionHomeUiChecks
{
    internal static int Run(string directory,string language="en_US")
    {
        int checks=0;
        void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Complete(Task task,int timeout=15000){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>timeout)throw new TimeoutException("Companion fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name) where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(x=>x.Name==name);
        void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void CaptureFeedback(RelationshipFeedbackWindow window,string kind)
        {
            var captures=Path.GetFullPath(Path.Combine("work","relationship-validation","s2","avalonia"));Directory.CreateDirectory(captures);
            void Capture(string suffix){AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var rendered=window.CaptureRenderedFrame())rendered!.Save(Path.Combine(captures,suffix+".png"),new PngBitmapEncoderOptions());
                // The headless framebuffer is RGB. Preserve a transparent Skia visual
                // capture too, so the outer-corner alpha can be inspected directly.
                using(var surface=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width),(int)Math.Ceiling(window.Bounds.Height)))){surface.Render(window);surface.Save(Path.Combine(captures,suffix+"-surface.png"),new PngBitmapEncoderOptions());}}
            foreach(string theme in new[]{"light","dark"}){
                DesktopTheme.Apply(theme);Dispatcher.UIThread.RunJobs();Capture(language+"-"+theme+"-"+kind+"-feedback");
                var name=window.GetLogicalDescendants().OfType<TextBlock>().Single(t=>ToolTip.GetTip(t) is string);string? original=name.Text;
                name.Text="很长的伙伴名字 · A companion with a long localized name";window.Width=260;Dispatcher.UIThread.RunJobs();Capture(language+"-"+theme+"-"+kind+"-feedback-narrow");name.Text=original;window.Width=350;
            }
        }
        Directory.CreateDirectory(directory);
        SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false});
        var lifetime=new ClassicDesktopStyleApplicationLifetime();using var owner=new DesktopController(lifetime,new FixtureRuntime(directory));
        Complete(Task.Run(()=>RelationshipHomeFixtures.Seed156(owner.RelationshipService,owner.RelationshipCompanionId)));
        owner.Pet.Paused=true;var chat=new ChatWindow(owner,false);chat.Show();owner.Pet.Show();
        try{
            var feedbackDeadline=System.Diagnostics.Stopwatch.StartNew();RelationshipFeedbackWindow? passive=null;
            while(passive==null){if(feedbackDeadline.ElapsedMilliseconds>10000)throw new TimeoutException("Actual feedback not displayed");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);passive=(RelationshipFeedbackWindow?)typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner);}
            Check(!passive.ShowActivated&&!passive.ShowInTaskbar,"actual saved stage window does not request focus or taskbar entry");
            Check((string)typeof(PetWindow).GetProperty("action",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.Pet)! == "idle","paused pet is not interrupted by automatic celebration");
            Check(owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).State.Score==156,"automatic popup and celebration add no points");
            CaptureFeedback(passive,"stage");
            // RunJobs deliberately ignores OS events; a real dispatcher frame is required
            // to exercise DispatcherTimer, matching the existing native job UI fixture.
            owner.Settings.QuietMode=true;feedbackDeadline.Restart();var frame=new DispatcherFrame();
            using(var pulse=new System.Threading.Timer(_=>Dispatcher.UIThread.Post(()=>{if(!passive.IsVisible||feedbackDeadline.ElapsedMilliseconds>1500)frame.Continue=false;}),null,0,10))Dispatcher.UIThread.PushFrame(frame);
            Check(!passive.IsVisible,"quiet mode dismisses an already-visible automatic notice");owner.Settings.QuietMode=false;owner.Pet.Paused=false;
            owner.Pet.CelebrateRelationship("waving");Complete(owner.Relationships.FlushAsync());
            Check((string)typeof(PetWindow).GetProperty("action",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.Pet)! == "waving"&&owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).State.Score==156,"actual automatic celebration plays without interaction rewards");
            var draft=Named<TextBox>(chat,"ChatDraft");draft.Text="preserved companion draft";var session=chat.BoundSession;
            var avatar=Named<Button>(chat,"ShellAvatar");Click(avatar);Dispatcher.UIThread.RunJobs();
            var entry=avatar.ContextMenu!.Items.OfType<MenuItem>().Single(x=>x.Name=="ProfileCompanionHome");
            Check(entry.IsEnabled&&entry.Focusable&&AutomationProperties.GetName(entry)!.Contains(owner.Skin.DisplayName),"profile is accessible and keyboard focusable");
            Check(entry.Icon is AvatarView a&&ReferenceEquals(a.Skin,owner.Skin)&&entry.Opacity==1,"original-color canonical avatar");
            entry.Focus();Check(entry.IsFocused,"profile accepts real keyboard focus");entry.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Dispatcher.UIThread.RunJobs();
            var page=chat.GetLogicalDescendants().OfType<CompanionHomePage>().Single();Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
            var cardData=RelationshipCardData.FromSnapshot(page.View!.Snapshot,"familiar",owner.Skin.DisplayName);
            using(var cardAssets=new SpriteAssets()){
                var captured=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).Single(x=>x.Data.StageId=="familiar");var png=captured.Png;Check(captured.Data.CompanionId==page.View!.CompanionId&&captured.Data.Name==owner.Skin.DisplayName&&captured.Data.EarnedLocalDate==cardData.EarnedLocalDate,"preview capture binds saved identity, name and milestone date");
                var exported=Path.GetFullPath(Path.Combine("work","relationship-validation","s2","avalonia","keepsake-"+language+".png"));Directory.CreateDirectory(Path.GetDirectoryName(exported)!);
                var save=RelationshipCardFile.SaveAsync(exported,png);Complete(save);Check(save.Result==RelationshipCardSaveResult.Saved,"actual PNG is atomically saved");Check(File.ReadAllBytes(exported).SequenceEqual(captured.Png),"export uses byte-for-byte captured preview PNG");
                using var decoded=new Bitmap(exported);Check(decoded.PixelSize==new PixelSize(800,480),"export decodes as 800 by 480 PNG");
                using var pixels=new WriteableBitmap(decoded.PixelSize,new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);
                using(var buffer=pixels.Lock()){
                    decoded.CopyPixels(buffer);var bytes=new byte[buffer.RowBytes*480];System.Runtime.InteropServices.Marshal.Copy(buffer.Address,bytes,0,bytes.Length);
                    bool Ink(int x,int y,int width,int height){int ink=0;for(int yy=y;yy<y+height;yy++)for(int xx=x;xx<x+width;xx++){int i=yy*buffer.RowBytes+xx*4;if(bytes[i]+bytes[i+1]+bytes[i+2]<650&&bytes[i+3]>200)ink++;}return ink>100;}
                    Check(Ink(42,95,168,168)&&Ink(242,95,500,168)&&Ink(42,280,700,90),"decoded PNG contains actual avatar, name/stage and date/footer ink");
                }
                var visual=RelationshipCardRenderer.Create(cardData,owner.Skin,cardAssets);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
                Check(visual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==owner.Skin.DisplayName)&&visual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==cardData.StageName)&&visual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==cardData.DateText),"native rendered text comes from saved identity, stage and original date");
            }
            Check(page.GetLogicalDescendants().OfType<Button>().Count(b=>b.Name=="SaveRelationshipCard")==page.View!.Snapshot.State.Milestones.Count,"every saved home card has export action");
            Check(chat.CurrentPage=="companion"&&!avatar.ContextMenu.IsOpen,"profile closes and navigates in main page host");
            Check(page.View!.Snapshot.State.Score==156&&Named<TextBlock>(page,"CompanionRelationshipScore").Text=="156 / 240","home visit renders committed actual values");
            chat.NavigateHistory(-1);Check(chat.CurrentPage=="chat"&&draft.Text=="preserved companion draft"&&ReferenceEquals(chat.BoundSession,session),"draft and session survive companion navigation");chat.NavigateHistory(1);chat.ShowImages();chat.NavigateHistory(-1);Check(chat.CurrentPage=="companion","images and companion share history");
            Check(page.View!.ActiveDays==16&&page.View!.AcquaintanceDays==21,"active days and acquaintance span have exact distinct meanings");
            var preview=page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview");
            Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
            preview=page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview");
            using(var previewLease=new RelationshipStore(directory).Acquire()){
                // Freeze autonomous ledger claims while observing a UI-only action.
                // Keep whole-directory equality: no ignored extensions or retry-until-green.
                string[] beforePreview=Directory.GetFiles(directory,"*",SearchOption.AllDirectories);byte[] beforeLedger=File.ReadAllBytes(Path.Combine(directory,"relationships","state.json"));
                var thumbnailAction=Named<Button>((Control)preview.Parent!,"RelationshipCardThumbnail");Check(AutomationProperties.GetName(thumbnailAction)!.Contains(owner.Skin.DisplayName),"thumbnail action describes the captured companion");Click(thumbnailAction);Dispatcher.UIThread.RunJobs();Check(preview.IsExpanded,"thumbnail button expands inline preview");
                var afterPreview=Directory.GetFiles(directory,"*",SearchOption.AllDirectories);
                Check(beforePreview.OrderBy(x=>x).SequenceEqual(afterPreview.OrderBy(x=>x)),"preview expansion never creates files in a settled owned fixture");
                Check(beforeLedger.SequenceEqual(File.ReadAllBytes(Path.Combine(directory,"relationships","state.json"))),"preview expansion never changes saved ledger bytes");
            }
            var thumbnail=Named<Image>((Control)preview.Parent!,"RelationshipCardImage");var previewDecoded=(Bitmap)thumbnail.Source!;
            Check(previewDecoded.PixelSize.Width==800&&previewDecoded.PixelSize.Height==480,"thumbnail decodes actual 800 by 480 PNG");
            Check(ReferenceEquals(thumbnail.Source,Named<Image>(preview,"RelationshipCardExpandedImage").Source),"thumbnail and expanded preview use same decoded PNG");
            Complete(page.RefreshAsync());Check(page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"refresh preserves expanded preview");
            DesktopTheme.Apply("light");Dispatcher.UIThread.RunJobs();var lightCapture=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).Single(x=>x.Data.StageId=="familiar");
            page.Offset=new Vector(0,120);Dispatcher.UIThread.RunJobs();double themeOffset=page.Offset.Y;DesktopTheme.Apply("dark");Dispatcher.UIThread.RunJobs();var darkCapture=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).Single(x=>x.Data.StageId=="familiar");
            using(var decodedDark=new Bitmap(new MemoryStream(darkCapture.Png)))using(var pixelsDark=new WriteableBitmap(decodedDark.PixelSize,new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul))using(var bufferDark=pixelsDark.Lock()){
                decodedDark.CopyPixels(bufferDark);var pixel=new byte[4];System.Runtime.InteropServices.Marshal.Copy(bufferDark.Address,pixel,0,4);Check(pixel[0]+pixel[1]+pixel[2]<200,"dark theme captures a dark PNG background, not a white card");var pageColor=((ISolidColorBrush)DesktopTheme.Surface).Color;Check(pixel[0]!=pageColor.B||pixel[1]!=pageColor.G||pixel[2]!=pageColor.R,"dark PNG card surface is visibly distinct from actual page surface");var allPixels=new byte[bufferDark.RowBytes*480];System.Runtime.InteropServices.Marshal.Copy(bufferDark.Address,allPixels,0,allPixels.Length);int ink=0;for(int y=95;y<263;y++)for(int x=242;x<742;x++){int i=y*bufferDark.RowBytes+x*4;if(allPixels[i]+allPixels[i+1]+allPixels[i+2]>600)ink++;}Check(ink>100,"dark PNG name and stage contain contrasting light ink");
            }
            using(var decodedLight=new Bitmap(new MemoryStream(lightCapture.Png)))using(var pixelsLight=new WriteableBitmap(decodedLight.PixelSize,new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul))using(var bufferLight=pixelsLight.Lock()){decodedLight.CopyPixels(bufferLight);var pixel=new byte[4];System.Runtime.InteropServices.Marshal.Copy(bufferLight.Address,pixel,0,4);Check(pixel[0]==252&&pixel[1]==247&&pixel[2]==244,"light PNG retains its original background palette");}
            Check(themeOffset>0&&Math.Abs(page.Offset.Y-themeOffset)<2,"theme recapture preserves actual nonzero scroll");
            Check(!lightCapture.Png.SequenceEqual(darkCapture.Png),"theme change invalidates captured PNG cache");Check(page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"theme recapture preserves expanded preview");
            var originalSkin=owner.Skin;string originalId=owner.RelationshipCompanionId;
            var otherSkin=new PetSkin{Id="home-fixture",PickerKey="custom-pack:home-fixture",Name="Second companion",Prompt="Fixture",Atlas=originalSkin.Atlas,Avatar=SpriteAssets.AvatarPath(originalSkin),Description=new string('x',220)};owner.Skins.Add(otherSkin);
            string otherId=RelationshipIdentity.Resolve(directory,otherSkin.PickerKey,null,otherSkin.Id);
            Complete(Task.Run(()=>RelationshipHomeFixtures.SeedMaximum(owner.RelationshipService,otherId)),120000);
            Complete(owner.SelectSkinAsync(otherSkin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
            Check(page.View!.CompanionId==otherId&&page.View!.Progress.Score==1000&&page.View!.Progress.NextStageId==null&&ReferenceEquals(Named<AvatarView>(page,"CompanionHomeAvatar").Skin,otherSkin),"actual skin switch binds avatar to independent max state");
            var beforeAppearance=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).First();
            var refreshedSkin=new PetSkin{Id=otherSkin.Id,PickerKey=otherSkin.PickerKey,Name=otherSkin.Name,Prompt=otherSkin.Prompt,Atlas=otherSkin.Atlas,Avatar=otherSkin.Avatar,Description=otherSkin.Description};
            typeof(DesktopController).GetProperty("Skin")!.SetValue(owner,refreshedSkin);typeof(DesktopController).GetMethod("Notify",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,null);Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(Named<AvatarView>(page,"CompanionHomeAvatar").Skin,refreshedSkin)&&page.View!.CompanionId==otherId&&!ReferenceEquals(beforeAppearance,page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).First()),"same-identity source commit and actual owner notification refresh avatar capture");
            Named<Expander>(page,"CompanionIntroduction").IsExpanded=true;Complete(page.RefreshAsync());Check(Named<Expander>(page,"CompanionIntroduction").IsExpanded,"long introduction expansion survives refresh");AvaloniaHeadlessPlatform.ForceRenderTimerTick();var maxCaptures=Path.GetFullPath(Path.Combine("work","relationship-validation","s2","avalonia"));Directory.CreateDirectory(maxCaptures);foreach(string maxTheme in new[]{"light","dark"}){DesktopTheme.Apply(maxTheme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var maxImage=chat.CaptureRenderedFrame();maxImage!.Save(Path.Combine(maxCaptures,language+"-maximum-"+maxTheme+".png"),new PngBitmapEncoderOptions());}
            Complete(owner.SelectSkinAsync(originalSkin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());Check(page.View!.CompanionId==originalId&&page.View!.Progress.Score==156,"switching back preserves saved identity score");
            var settings=Named<Expander>(page,"CompanionRelationshipSettings");settings.IsExpanded=true;Dispatcher.UIThread.RunJobs();
            var growth=Named<ToggleSwitch>(page,"RelationshipGrowthEnabled");growth.IsChecked=false;growth.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));
            Complete(page.PendingSettings);Complete(page.RefreshAsync());
            Check(!owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).Settings.GrowthEnabled,"growth checkbox saves global setting");
            var captures=Path.GetFullPath(Path.Combine("work","relationship-validation","s2","avalonia"));Directory.CreateDirectory(captures);
            {
                Complete(page.RefreshAsync());
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{390,520,1100,1320}){DesktopTheme.Apply(theme);chat.Width=width;chat.Height=1040;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();var stage=Named<TextBlock>(page,"CompanionRelationshipStage");var profileAvatar=Named<AvatarView>(page,"CompanionHomeAvatar");var stagePoint=stage.TranslatePoint(new Point(),page)!.Value;var avatarPoint=profileAvatar.TranslatePoint(new Point(),page)!.Value;Check(page.Bounds.Width<834?stagePoint.Y>avatarPoint.Y+profileAvatar.Bounds.Height:stagePoint.X>avatarPoint.X+profileAvatar.Bounds.Width,"responsive profile and relationship bounds "+width+" viewport="+page.Bounds.Width);var score=Named<TextBlock>(page,"CompanionRelationshipScore");Check(score.TranslatePoint(new Point(score.Bounds.Width,0),page)!.Value.X<=page.Bounds.Width,"score stays in viewport "+width);using var image=chat.CaptureRenderedFrame();Check(image!=null,"rendered "+language+" "+theme+" "+width);image!.Save(Path.Combine(captures,language+"-"+theme+"-"+width+".png"),new PngBitmapEncoderOptions());}
            }
            var zeroSkin=new PetSkin{Id="home-zero",PickerKey="custom-pack:home-zero",Name="Unmet companion",Prompt="Fixture",Atlas=originalSkin.Atlas,Avatar=SpriteAssets.AvatarPath(originalSkin)};owner.Skins.Add(zeroSkin);Complete(owner.SelectSkinAsync(zeroSkin));Complete(page.RefreshAsync());Check(page.View!.Snapshot.State==null&&page.View!.Progress.Score==0,"unmet companion has real empty zero state");foreach(string zeroTheme in new[]{"light","dark"}){DesktopTheme.Apply(zeroTheme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var zeroImage=chat.CaptureRenderedFrame();zeroImage!.Save(Path.Combine(captures,language+"-zero-"+zeroTheme+".png"),new PngBitmapEncoderOptions());}Complete(owner.SelectSkinAsync(originalSkin));Complete(page.RefreshAsync());
            Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());string file=Path.Combine(directory,"relationships","state.json");byte[] original=File.ReadAllBytes(file);using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllText(file,"{broken");var feedback=Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled");feedback.IsChecked=false;feedback.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));Complete(page.PendingSettings);Complete(page.RefreshAsync());Check(Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled").IsChecked==true&&File.ReadAllText(file)=="{broken","failed setting preserves checkbox and ledger bytes");
            var retained=page.View;var retainedState=retained?.Snapshot?.State;
            Check(retained?.ReadFailed==true&&retainedState!=null&&retainedState.Score==156&&Named<TextBlock>(page,"RelationshipWarning").IsVisible,"corruption retains saved score and retry warning");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var failed=chat.CaptureRenderedFrame())failed!.Save(Path.Combine(captures,language+"-read-failure.png"),new PngBitmapEncoderOptions());using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllBytes(file,original);Complete(page.RefreshAsync());
            var context=Named<ToggleSwitch>(page,"RelationshipAiContextEnabled");var priorSave=page.PendingSettings;
            using(var lease=new FileStream(Path.Combine(directory,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                Click(Named<Button>(page,"RelationshipRetry"));
                Check(!context.IsEffectivelyEnabled,"retry immediately disables the already-rendered settings controls");
                context.IsChecked=false;context.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));
                Check(ReferenceEquals(priorSave,page.PendingSettings)&&context.IsChecked==true,"settings handler rejects concurrent admission during retry");
                var admittedRetry=page.PendingRetry;Click(Named<Button>(page,"RelationshipRetry"));Check(ReferenceEquals(admittedRetry,page.PendingRetry),"retry handler rejects duplicate admission while busy");
            }
            Complete(page.PendingRetry);Complete(page.RefreshAsync());Check(!page.View!.ReadFailed&&!owner.RelationshipService.GetSnapshot(originalId).Settings.FeedbackEnabled,"retry recovers preserved ledger and failed settings intent");
            var settingsModel=new RelationshipHomeModel(owner.Relationships);var enable=settingsModel.UpdateSettingsAsync(true,true,true);Complete(enable);Check(enable.Result.Success,"feedback window fixture enables saved feedback");
            Named<Expander>(page,"CompanionGrowthRules").IsExpanded=true;Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview").IsExpanded=true;Dispatcher.UIThread.RunJobs();page.Offset=new Vector(0,160);Dispatcher.UIThread.RunJobs();double priorOffset=page.Offset.Y;Console.WriteLine("SCROLL_STATE bounds="+page.Bounds+" extent="+page.Extent+" viewport="+page.Viewport+" offset="+page.Offset);Check(priorOffset>50&&page.Extent.Height>page.Viewport.Height,"home has actual nonzero scrolling before refresh");
            Complete(owner.Relationships.Enqueue(originalId,RelationshipEventType.CompletedConversation,"feedback-window-"+Guid.NewGuid().ToString("N"),RelationshipTime.Current()));Complete(page.RefreshAsync());
            Check(Named<Expander>(page,"CompanionGrowthRules").IsExpanded&&Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded&&page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"real saved growth preserves rules, settings and preview expansion");
            Check(Math.Abs(page.Offset.Y-priorOffset)<2,"real saved growth preserves reasonable scroll offset");
            RelationshipFeedbackWindow? savedNotice=null;feedbackDeadline.Restart();while(savedNotice==null){if(feedbackDeadline.ElapsedMilliseconds>10000)throw new TimeoutException("Saved feedback notice not displayed");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);savedNotice=(RelationshipFeedbackWindow?)typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner);}
            CaptureFeedback(savedNotice,"gain");Complete(page.RefreshAsync());Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;Dispatcher.UIThread.RunJobs();byte[] settingsBytes=File.ReadAllBytes(file);using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllText(file,"{broken");
            var failedFeedback=Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled");failedFeedback.IsChecked=false;failedFeedback.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));Complete(page.PendingSettings);
            Check(savedNotice.IsVisible,"failed feedback settings save does not dismiss a visible notice");using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllBytes(file,settingsBytes);Complete(page.RefreshAsync());Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;Dispatcher.UIThread.RunJobs();
            var savedFeedback=Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled");savedFeedback.IsChecked=false;savedFeedback.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));Complete(page.PendingSettings);Dispatcher.UIThread.RunJobs();
            Check(!savedNotice.IsVisible,"successfully disabling feedback through home settings promptly closes the actual notice");
            var reenable=settingsModel.UpdateSettingsAsync(true,true,true);Complete(reenable);Check(reenable.Result.Success,"feedback can be re-enabled after dismissal");
            feedbackDeadline.Restart();var quietFrame=new DispatcherFrame();using(var pulse=new System.Threading.Timer(_=>Dispatcher.UIThread.Post(()=>{if(feedbackDeadline.ElapsedMilliseconds>=650)quietFrame.Continue=false;}),null,0,10))Dispatcher.UIThread.PushFrame(quietFrame);
            Check(typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)==null,"re-enabling feedback never replays the dismissed notice");
            Complete(Task.Run(()=>RelationshipHomeFixtures.ReachDailyLimit(owner.RelationshipService,originalId)));Complete(page.RefreshAsync());
            Check(page.View!.DailyLimitReached&&page.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Text==TextResources.Get("relationship.home.dailyLimit")),"saved 20-point daily cap has visible explanation");
            Complete(settingsModel.UpdateSettingsAsync(false,false,true));Complete(page.RefreshAsync());Check(page.View!.GrowthPaused&&page.View!.DailyLimitReached,"daily cap and global pause remain independently visible");
            foreach(string expandedTheme in new[]{"light","dark"}){
                DesktopTheme.Apply(expandedTheme);Named<Expander>(page,"CompanionGrowthRules").IsExpanded=true;Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;Dispatcher.UIThread.RunJobs();page.Offset=new Vector(0,page.Extent.Height);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var groupsImage=chat.CaptureRenderedFrame())groupsImage!.Save(Path.Combine(captures,language+"-expanded-groups-"+expandedTheme+".png"),new PngBitmapEncoderOptions());
                var expandedPreview=page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview");expandedPreview.IsExpanded=true;expandedPreview.BringIntoView();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var previewFrame=chat.CaptureRenderedFrame())previewFrame!.Save(Path.Combine(captures,language+"-expanded-preview-"+expandedTheme+".png"),new PngBitmapEncoderOptions());
            }
            page.Offset=new Vector();foreach(string capTheme in new[]{"light","dark"}){DesktopTheme.Apply(capTheme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var capImage=chat.CaptureRenderedFrame();capImage!.Save(Path.Combine(captures,language+"-paused-cap-"+capTheme+".png"),new PngBitmapEncoderOptions());}
            string blockedDirectory=Path.Combine(directory,"blocked-fixture");Directory.CreateDirectory(Path.Combine(blockedDirectory,"relationships"));string blockedFile=Path.Combine(blockedDirectory,"relationships","pending.json");File.WriteAllText(blockedFile,"{broken");
            new SettingsStore(Path.Combine(blockedDirectory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false,QuietMode=true});
            using(var blockedOwner=new DesktopController(lifetime,new FixtureRuntime(blockedDirectory)))using(var blockedPage=new CompanionHomePage(blockedOwner)){
                var blockedWindow=new Window{Width=900,Height=780,Content=blockedPage};blockedWindow.Show();
                try{Complete(blockedPage.RefreshAsync());Check(Named<Button>(blockedPage,"RelationshipRestart").IsVisible&&!Named<Button>(blockedPage,"RelationshipRetry").IsVisible,"blocked queue offers truthful restart recovery rather than unusable retry");Check(File.ReadAllText(blockedFile)=="{broken","blocked recovery view preserves the original queue");
                    foreach(string blockedTheme in new[]{"light","dark"}){DesktopTheme.Apply(blockedTheme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var blockedImage=blockedWindow.CaptureRenderedFrame();blockedImage!.Save(Path.Combine(captures,language+"-blocked-outbox-"+blockedTheme+".png"),new PngBitmapEncoderOptions());}
                }finally{blockedWindow.Close();blockedOwner.Pet.Close();}
            }
        }finally{chat.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        return checks;
    }
    private sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");
        public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
}
