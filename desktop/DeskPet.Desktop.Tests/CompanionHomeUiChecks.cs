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
    internal static int RunHeartIcons(string output)
    {
        Directory.CreateDirectory(output);int checks=0;
        foreach(string theme in new[]{"light","dark"})foreach(int size in new[]{22,32}){
            DesktopTheme.Apply(theme);int n=size+16;var canvas=new Canvas{Width=n,Height=n};var icon=HomeVisual.Icon("heart",size);Canvas.SetLeft(icon,8);Canvas.SetTop(icon,8);canvas.Children.Add(icon);
            var window=new Window{Width=n,Height=n,Content=canvas,Background=Brushes.Transparent};window.Show();Dispatcher.UIThread.RunJobs();canvas.Measure(new Size(n,n));canvas.Arrange(new Rect(0,0,n,n));AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            try{using var bitmap=new RenderTargetBitmap(new PixelSize(n,n));bitmap.Render(canvas);bitmap.Save(Path.Combine(output,"native-"+theme+"-heart-"+size+".png"),new PngBitmapEncoderOptions());
                byte[] pixels=new byte[n*n*4];var pin=System.Runtime.InteropServices.GCHandle.Alloc(pixels,System.Runtime.InteropServices.GCHandleType.Pinned);try{bitmap.CopyPixels(new PixelRect(0,0,n,n),pin.AddrOfPinnedObject(),pixels.Length,n*4);}finally{pin.Free();}
                int edge=0,ink=0,minY=n,maxY=-1;var rows=new List<string>();bool lobes=false;
                for(int y=0;y<n;y++){int count=0,runs=0;bool prior=false;for(int x=0;x<n;x++){bool visible=pixels[(y*n+x)*4+3]>0;if(visible){ink++;count++;minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);if(!prior)runs++;if(x==8||x==8+size-1||y==8||y==8+size-1)edge++;}prior=visible;}if(count>0)rows.Add(y+":"+count);if(y<=12&&runs>=2)lobes=true;}
                Console.WriteLine("HEART_RASTER native "+theme+" size="+size+" edge="+edge+" y="+minY+".."+maxY+" rows="+String.Join(",",rows));
                if(ink<30||!lobes)throw new Exception("heart retains two curved lobes and visible silhouette");checks++;
                if(edge!=0)throw new Exception("heart design-space stroke has clear inset at every viewport edge");checks++;
            }finally{window.Close();}
        }return checks;
    }
    static bool StageWordsStayWhole(TextBlock label)
    {
        int start=0;foreach(string word in label.Text!.Split(' ')){if(word.Length>0&&Math.Abs(label.TextLayout.HitTestTextPosition(start).Y-label.TextLayout.HitTestTextPosition(start+word.Length-1).Y)>.5)return false;start+=word.Length+1;}return true;
    }
    internal static int RunStageTrackLayout(string directory,string language,string? captureDirectory=null)
    {
        int checks=0;void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Complete(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("Stage track fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false,QuietMode=true});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));using var page=new CompanionHomePage(owner);
        var window=new Window{Width=language=="de_DE"?900:1100,Height=960,Content=page};window.Show();
        try{
            Complete(page.RefreshAsync());Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
            var track=page.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single(x=>x.Name=="RelationshipStageNodes");
            int initialColumns=language is "de_DE" or "ja_JP"?3:6;
            Check(track.Columns==initialColumns&&track.Rows==6/initialColumns,"stage track uses fitting columns in actual "+window.Width+"px home");
            foreach(var label in track.Children.OfType<StackPanel>().SelectMany(item=>item.Children.OfType<TextBlock>()))Check(StageWordsStayWhole(label),"stage words do not split across rendered lines: "+label.Text);
            Console.WriteLine("STAGE_TRACK width="+track.Bounds.Width+" columns="+track.Columns+" rows="+track.Rows);
            string output=Path.GetFullPath(captureDirectory??"work/ui-consistency/stage-track-after");Directory.CreateDirectory(output);
            if(captureDirectory==null){using var image=window.CaptureRenderedFrame();image!.Save(Path.Combine(output,"native-"+language+"-"+window.Width+".png"),new PngBitmapEncoderOptions());}
            else foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Complete(page.RefreshAsync());Dispatcher.UIThread.RunJobs();using var image=new RenderTargetBitmap(new PixelSize((int)window.Width,(int)window.Height));image.Render(window);image.Save(Path.Combine(output,"native-home-"+theme+".png"),new PngBitmapEncoderOptions());}
            if(language=="ja_JP"){
                window.Width=880;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
                Check(track.Columns==2&&track.Rows==3,"880px hosted-width Japanese home uses two fitting columns: track="+track.Bounds.Width);
                foreach(var label in track.Children.OfType<StackPanel>().SelectMany(item=>item.Children.OfType<TextBlock>()))Check(StageWordsStayWhole(label),"880px hosted-width Japanese stage label stays whole: "+label.Text);
                var fixtureTrack=HomeVisual.Nodes(page.View!.Progress);fixtureTrack.Width=660;var fixtureWindow=new Window{Width=700,Height=400,Content=fixtureTrack};fixtureWindow.Show();
                try{foreach(var (width,columns) in new[]{(660d,6),(330d,3),(303d,2),(150d,1)}){
                    fixtureTrack.Width=width;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
                    Check(Math.Abs(fixtureTrack.Bounds.Width-width)<.5&&fixtureTrack.Columns==columns&&fixtureTrack.Rows==6/columns,"Japanese stage track fits "+columns+" columns at explicit "+width+"px: actual="+fixtureTrack.Bounds.Width);
                    foreach(var label in fixtureTrack.Children.OfType<StackPanel>().SelectMany(item=>item.Children.OfType<TextBlock>()))Check(StageWordsStayWhole(label),"Japanese stage label stays whole at "+width+"px: "+label.Text);
                }}finally{fixtureWindow.Close();}
                var evidence=Path.GetFullPath("work/ci-mac-home-stage-evidence");Directory.CreateDirectory(evidence);
                foreach(var theme in new[]{"light","dark"}){
                    DesktopTheme.Apply(theme);Complete(page.RefreshAsync());window.Width=880;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var image=window.CaptureRenderedFrame();image!.Save(Path.Combine(evidence,"ja_JP-home-880-"+theme+".png"),new PngBitmapEncoderOptions());
                }
            }
        }finally{window.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}return checks;
    }
    internal static int RunFirstVisit(string directory)
    {
        int checks=0;void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Complete(Task task){var deadline=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("First home Visit fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name) where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(x=>x.Name==name);
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{AutoUpdate=false,GreetingEnabled=false,QuietMode=true,ReadReplies=false,OnboardingState="completed",HideOnFullscreen=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));var chat=new ChatWindow(owner,false);var model=new RelationshipHomeModel(owner.Relationships);
        try{
            var disabled=model.UpdateSettingsAsync(false,false,true);Complete(disabled);Check(disabled.Result.Success,"first-visit fixture disables growth through actual settings request");
            Complete(owner.SelectSkinAsync(owner.Skins.First(x=>x.PickerKey!=owner.Skin.PickerKey)));Complete(owner.Relationships.FlushAsync());chat.Show();
            var enabled=model.UpdateSettingsAsync(true,false,true);Complete(enabled);Check(enabled.Result.Success,"first-visit fixture enables saved growth before home entry");Complete(owner.Relationships.FlushAsync());
            string id=owner.RelationshipCompanionId;Check(!owner.Pet.IsVisible&&chat.IsVisible&&chat.CurrentPage=="chat"&&owner.RelationshipService.GetSnapshot(id).State==null,"shown chat with hidden pet remains unmet immediately before home entry");
            for(int opening=0;opening<2;opening++){
                var avatar=Named<Button>(chat,"ShellAvatar");avatar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var menu=avatar.ContextMenu!;menu.Items.OfType<MenuItem>().Single(x=>x.Name=="ProfileCompanionHome").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Dispatcher.UIThread.RunJobs();
                Complete(owner.Relationships.FlushAsync());var page=chat.GetLogicalDescendants().OfType<CompanionHomePage>().Single();Complete(page.RefreshAsync());
                var saved=new RelationshipService(new RelationshipStore(directory)).GetSnapshot(id).State;
                Check(chat.CurrentPage=="companion"&&page.IsEffectivelyVisible&&!menu.IsOpen,"actual profile entry shows hosted companion home "+opening);
                Check(saved!=null&&saved.Score==2&&saved.Receipts.Count==opening+1&&saved.Receipts.All(r=>r.EventType==RelationshipEventType.Visit)&&saved.Receipts.Count(r=>r.AwardedPoints==2)==1&&saved.Receipts.Sum(r=>r.AwardedPoints)==2&&saved.Receipts.Select(r=>r.EventId).Distinct().Count()==saved.Receipts.Count,"actual home entry persists distinct Visits with exactly one two-point award "+opening);
                Check(saved!.FirstLocalDate==saved.Receipts[0].LocalDate&&saved.Milestones.Count==1,"first home encounter persists first meeting facts");
                if(opening==0){chat.NavigateHistory(-1);Dispatcher.UIThread.RunJobs();Check(chat.CurrentPage=="chat","first home entry can return to chat before reopening");}
            }
        }finally{chat.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}return checks;
    }
    internal static int RunMaintenanceSettings(string directory)
    {
        int checks=0;
        void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Complete(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("Settings maintenance fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name) where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(x=>x.Name==name);
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{OnboardingState="completed",AutoUpdate=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));
        using var page=new CompanionHomePage(owner);var window=new Window{Width=900,Height=780,Content=page};window.Show();
        try{
            Complete(page.RefreshAsync());Complete(owner.Relationships.FlushAsync());
            var exclusive=owner.DataMaintenance.BeginAsync();Complete(exclusive);
            using(exclusive.Result){
                Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;Dispatcher.UIThread.RunJobs();
                var toggle=Named<ToggleSwitch>(page,"RelationshipGrowthEnabled");toggle.IsChecked=false;toggle.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));Complete(page.PendingSettings);
                Check(Named<ToggleSwitch>(page,"RelationshipGrowthEnabled").IsEffectivelyEnabled,"busy settings refusal restores actual native controls");
                var retry=Named<Button>(page,"RelationshipRetry");Check(retry.IsVisible&&retry.IsEffectivelyEnabled,"busy settings refusal shows enabled actionable native retry");
                retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Complete(page.PendingRetry);
                Check(Named<Button>(page,"RelationshipRetry").IsEffectivelyEnabled,"busy native retry remains recoverable");
            }
            Named<Button>(page,"RelationshipRetry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Complete(page.PendingRetry);
            Check(!owner.Relationships.Service.GetSnapshot(owner.RelationshipCompanionId).Settings.GrowthEnabled,"native retry persists refused exact settings intent after rollback");
            using(owner.DataMaintenance.EnterActivity()){}owner.Session.SelectPersona(owner.Session.PersonaId);
            Check(Named<ToggleSwitch>(page,"RelationshipGrowthEnabled").IsEffectivelyEnabled,"native rollback retains usable controls and session");
        }finally{window.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        return checks;
    }
    internal static int Run(string directory,string language="en_US")
    {
        int checks=0;
        void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Complete(Task task,int timeout=15000){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>timeout)throw new TimeoutException("Companion fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name) where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(x=>x.Name==name);
        void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void CaptureRecovery(Window window,string state)
        {
            string output=Path.GetFullPath("work/ui-consistency/final");Directory.CreateDirectory(output);
            Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=window.CaptureRenderedFrame();
            image!.Save(Path.Combine(output,language+"-"+state+".png"),new PngBitmapEncoderOptions());
        }
        void CaptureFeedback(RelationshipFeedbackWindow window,string kind)
        {
            var captures=Path.GetFullPath(Path.Combine("work","relationship-validation","f3","avalonia"));Directory.CreateDirectory(captures);
            void Capture(string suffix){AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var rendered=window.CaptureRenderedFrame())rendered!.Save(Path.Combine(captures,suffix+".png"),new PngBitmapEncoderOptions());
                // The headless framebuffer is RGB. Preserve a transparent Skia visual
                // capture too, so the outer-corner alpha can be inspected directly.
                using(var surface=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width),(int)Math.Ceiling(window.Bounds.Height)))){surface.Render(window);surface.Save(Path.Combine(captures,suffix+"-surface.png"),new PngBitmapEncoderOptions());}}
            foreach(string theme in new[]{"light","dark"}){
                DesktopTheme.Apply(theme);Dispatcher.UIThread.RunJobs();Capture(language+"-"+theme+"-"+kind+"-feedback");
                var name=window.GetLogicalDescendants().OfType<TextBlock>().Single(t=>t.Parent is DockPanel&&ToolTip.GetTip(t) is string);string? original=name.Text;
                name.Text="很长的伙伴名字 · A companion with a long localized name";window.Width=260;Dispatcher.UIThread.RunJobs();Capture(language+"-"+theme+"-"+kind+"-feedback-narrow");name.Text=original;window.Width=350;
            }
        }
        Directory.CreateDirectory(directory);checks+=CompanionPortraitChecks.Run(directory,language,true);

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
            var hostedTrack=page.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Single(x=>x.Name=="RelationshipStageNodes");
            var splitStages=hostedTrack.Children.OfType<StackPanel>().SelectMany(item=>item.Children.OfType<TextBlock>()).Where(label=>!StageWordsStayWhole(label)).Select(label=>label.Text+" label="+label.Bounds.Width+"x"+label.Bounds.Height).ToArray();
            Check(splitStages.Length==0,"hosted home stage words do not split across rendered lines: track="+hostedTrack.Bounds.Width+" columns="+hostedTrack.Columns+" split="+String.Join(", ",splitStages));
            Check(page.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Name=="CompanionCurrentWelcome"&&x.Text==RelationshipContent.Welcome(page.View!.Progress.StageId,owner.RelationshipPack)),"current saved stage has its own truthful welcome message");
            Check(page.GetLogicalDescendants().OfType<Image>().Any(x=>x.Name=="CompanionProfileScene"&&x.Source!=null),"actual earned profile shows its original stage scene");
            checks+=ProfileArtworkChecks.CheckPage(owner,page,"familiar");
            chat.Width=1320;chat.Height=1040;DesktopTheme.Apply("dark");Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();string firstCapture=Path.GetFullPath("work/relationship-validation/f3/avalonia/first-home-dark.png");Directory.CreateDirectory(Path.GetDirectoryName(firstCapture)!);using(var firstFrame=chat.CaptureRenderedFrame())firstFrame!.Save(firstCapture,new PngBitmapEncoderOptions());Console.WriteLine("FIRST_ACTUAL_SCREENSHOT "+firstCapture);
            var fixCaptures=Path.GetFullPath(Path.Combine("work","relationship-validation","f3","fix1","avalonia"));Directory.CreateDirectory(fixCaptures);chat.Width=390;chat.Height=880;Named<Expander>(page,"CompanionGrowthRules").IsExpanded=true;
            foreach(string narrowTheme in new[]{"light","dark"}){
                DesktopTheme.Apply(narrowTheme);Dispatcher.UIThread.RunJobs();var narrowRules=Named<Expander>(page,"CompanionGrowthRules");narrowRules.BringIntoView();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var narrowImage=chat.CaptureRenderedFrame())narrowImage!.Save(Path.Combine(fixCaptures,language+"-rules-390-"+narrowTheme+".png"),new PngBitmapEncoderOptions());
                foreach(string ruleText in new[]{TextResources.Get("relationship.home.ruleInteraction"),TextResources.Get("relationship.home.ruleConversation")}){var rule=narrowRules.GetLogicalDescendants().OfType<TextBlock>().Single(x=>x.Text==ruleText);double right=rule.TranslatePoint(new Point(rule.Bounds.Width,0),narrowRules)!.Value.X;Check(right<=narrowRules.Bounds.Width+1,"localized expanded rule stays inside narrow group: "+language+" "+narrowTheme);}
            }
            Named<Expander>(page,"CompanionGrowthRules").IsExpanded=false;chat.Width=1100;page.Offset=new Vector();Dispatcher.UIThread.RunJobs();
            Check(typeof(HomeVisual).GetMethod("CaptureFocus",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!=null,"semantic focus capture identifies later milestone actions and disclosure headers");
            chat.Activate();Dispatcher.UIThread.RunJobs();Check(chat.IsActive,"owned headless chat is active for actual focus-manager regression");
            RelationshipCardPreview LaterCard()=>page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Single(x=>((HomeCardCapture)x.Tag!).Data.StageId=="familiar");
            var laterSave=Named<Button>(LaterCard(),"SaveRelationshipCard");laterSave.Focus();Check(ReferenceEquals(chat.FocusManager!.GetFocusedElement(),laterSave),"focus manager holds later milestone save action before refresh");Complete(page.RefreshAsync());Check(ReferenceEquals(chat.FocusManager!.GetFocusedElement(),Named<Button>(LaterCard(),"SaveRelationshipCard")),"actual focus manager preserves later milestone save action after rebuild");
            var laterThumb=Named<Button>(LaterCard(),"RelationshipCardThumbnail");laterThumb.Focus();Complete(page.RefreshAsync());Check(ReferenceEquals(chat.FocusManager!.GetFocusedElement(),Named<Button>(LaterCard(),"RelationshipCardThumbnail")),"actual focus manager preserves later milestone thumbnail after rebuild");
            var laterHeader=HomeVisual.DisclosureHeader(Named<Expander>(LaterCard(),"RelationshipCardPreview"))!;laterHeader.Focus();Check(ReferenceEquals(chat.FocusManager!.GetFocusedElement(),laterHeader),"focus manager holds actual later-card disclosure header");Complete(page.RefreshAsync());Check(ReferenceEquals(chat.FocusManager!.GetFocusedElement(),HomeVisual.DisclosureHeader(Named<Expander>(LaterCard(),"RelationshipCardPreview"))),"actual focus manager preserves later-card disclosure header after rebuild");
            var rulesHeader=HomeVisual.DisclosureHeader(Named<Expander>(page,"CompanionGrowthRules"))!;rulesHeader.Focus();Complete(page.RefreshAsync());Check(ReferenceEquals(chat.FocusManager!.GetFocusedElement(),HomeVisual.DisclosureHeader(Named<Expander>(page,"CompanionGrowthRules"))),"actual focus manager preserves global rules disclosure identity after rebuild");
            var cardData=RelationshipCardData.FromSnapshot(page.View!.Snapshot,"familiar",owner.Skin.DisplayName);
            using(var cardAssets=new SpriteAssets()){
                var captured=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).Single(x=>x.Data.StageId=="familiar");var png=captured.Png;Check(captured.Data.CompanionId==page.View!.CompanionId&&captured.Data.Name==owner.Skin.DisplayName&&captured.Data.EarnedLocalDate==cardData.EarnedLocalDate,"preview capture binds saved identity, name and milestone date");
                var exported=Path.GetFullPath(Path.Combine("work","relationship-validation","f3","avalonia","keepsake-"+language+".png"));Directory.CreateDirectory(Path.GetDirectoryName(exported)!);
                var save=RelationshipCardFile.SaveAsync(exported,png);Complete(save);Check(save.Result==RelationshipCardSaveResult.Saved,"actual PNG is atomically saved");Check(File.ReadAllBytes(exported).SequenceEqual(captured.Png),"export uses byte-for-byte captured preview PNG");
                using var decoded=new Bitmap(exported);Check(decoded.PixelSize==new PixelSize(800,480),"export decodes as 800 by 480 PNG");
                using var pixels=new WriteableBitmap(decoded.PixelSize,new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);
                using(var buffer=pixels.Lock()){
                    decoded.CopyPixels(buffer);var bytes=new byte[buffer.RowBytes*480];System.Runtime.InteropServices.Marshal.Copy(buffer.Address,bytes,0,bytes.Length);
                    bool Ink(int x,int y,int width,int height){int ink=0;for(int yy=y;yy<y+height;yy++)for(int xx=x;xx<x+width;xx++){int i=yy*buffer.RowBytes+xx*4;if(bytes[i]+bytes[i+1]+bytes[i+2]<650&&bytes[i+3]>200)ink++;}return ink>100;}
                    Check(Ink(32,64,296,360)&&Ink(360,100,408,110)&&Ink(360,350,408,100),"decoded PNG contains actual avatar, name/stage and date/footer ink");
                }
                using var visual=RelationshipCardRenderer.Create(cardData,owner.Skin,cardAssets);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
                Check(visual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==owner.Skin.DisplayName)&&visual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==cardData.StageName)&&visual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==cardData.DateText),"native rendered text comes from saved identity, stage and original date");foreach(var label in visual.GetLogicalDescendants().OfType<TextBlock>()){Check(label.Bounds.Width<=408.1&&label.Bounds.Height>0,"fixed keepsake text owns finite bounds: "+label.Name);}

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
            byte[] Pixel(HomeCardCapture capture,int x,int y){using var decoded=new Bitmap(new MemoryStream(capture.Png));using var pixels=new WriteableBitmap(decoded.PixelSize,new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);using var buffer=pixels.Lock();decoded.CopyPixels(buffer);var pixel=new byte[4];System.Runtime.InteropServices.Marshal.Copy(buffer.Address+y*buffer.RowBytes+x*4,pixel,0,4);return pixel;}
            Check(Pixel(lightCapture,100,200).SequenceEqual(Pixel(darkCapture,100,200)),"scene preserves original left-side colors across themes");Check(Pixel(darkCapture,790,240).Take(3).Sum(x=>(int)x)<Pixel(lightCapture,790,240).Take(3).Sum(x=>(int)x),"theme-specific gradient changes the right reading area");

            Check(themeOffset>0&&Math.Abs(page.Offset.Y-themeOffset)<2,"theme recapture preserves actual nonzero scroll");
            Check(!lightCapture.Png.SequenceEqual(darkCapture.Png),"theme change invalidates captured PNG cache");Check(page.GetLogicalDescendants().OfType<Expander>().First(x=>x.Name=="RelationshipCardPreview").IsExpanded,"theme recapture preserves expanded preview");
            var originalSkin=owner.Skin;string originalId=owner.RelationshipCompanionId;
            var otherSkin=new PetSkin{Id="home-fixture",PickerKey="custom-pack:home-fixture",Name="Second companion",Prompt="Fixture",Atlas=originalSkin.Atlas,Avatar=SpriteAssets.AvatarPath(originalSkin),Description=new string('x',220),Relationship=new RelationshipPackContent{Stages=new Dictionary<string,RelationshipStageContent>{{"best_friend",new RelationshipStageContent{Welcome=new Dictionary<string,string>{{language,String.Concat(Enumerable.Repeat("This is the saved welcome supplied by this companion. ",5))}}}}}}};owner.Skins.Add(otherSkin);
            string otherId=RelationshipIdentity.Resolve(directory,otherSkin.PickerKey,null,otherSkin.Id);
            Complete(Task.Run(()=>RelationshipHomeFixtures.SeedMaximum(owner.RelationshipService,otherId)),120000);
            Complete(owner.SelectSkinAsync(otherSkin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
            Check(page.View!.CompanionId==otherId&&page.View!.Progress.Score==1000&&page.View!.Progress.NextStageId==null&&ReferenceEquals(Named<CompanionPortraitVisual>(page,"CompanionHomeAvatar").Skin,otherSkin),"actual skin switch binds avatar to independent max state");
            var beforeAppearance=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).First();
            var refreshedSkin=new PetSkin{Id=otherSkin.Id,PickerKey=otherSkin.PickerKey,Name=otherSkin.Name,Prompt=otherSkin.Prompt,Atlas=otherSkin.Atlas,Avatar=otherSkin.Avatar,Description=otherSkin.Description,Relationship=otherSkin.Relationship};
            typeof(DesktopController).GetProperty("Skin")!.SetValue(owner,refreshedSkin);typeof(DesktopController).GetMethod("Notify",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,null);Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(Named<CompanionPortraitVisual>(page,"CompanionHomeAvatar").Skin,refreshedSkin)&&page.View!.CompanionId==otherId&&!ReferenceEquals(beforeAppearance,page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).First()),"same-identity source commit and actual owner notification refresh avatar capture");var beforeSource=page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).First();refreshedSkin.SourceKey=Guid.NewGuid().ToString("N");typeof(DesktopController).GetMethod("Notify",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,null);Dispatcher.UIThread.RunJobs();Check(!ReferenceEquals(beforeSource,page.GetLogicalDescendants().OfType<RelationshipCardPreview>().Select(x=>(HomeCardCapture)x.Tag!).First()),"same skin reference with new source version invalidates static portrait and PNG");
            var currentMessage=Named<Expander>(page,"CompanionWelcome");currentMessage.IsExpanded=true;var messageHeader=HomeVisual.DisclosureHeader(currentMessage)!;messageHeader.Focus();Complete(page.RefreshAsync());Check(Named<Expander>(page,"CompanionWelcome").IsExpanded&&ReferenceEquals(chat.FocusManager!.GetFocusedElement(),HomeVisual.DisclosureHeader(Named<Expander>(page,"CompanionWelcome"))),"long saved welcome keeps full accessible text expansion and focus across refresh");
            foreach(string matrixTheme in new[]{"light","dark"}){DesktopTheme.Apply(matrixTheme);Dispatcher.UIThread.RunJobs();foreach(var item in page.GetLogicalDescendants().OfType<RelationshipCardPreview>()){var card=(HomeCardCapture)item.Tag!;string matrix=Path.GetFullPath("work/relationship-validation/f3/avalonia/cards/"+language+"-"+card.Data.StageId+"-"+matrixTheme+".png");Directory.CreateDirectory(Path.GetDirectoryName(matrix)!);File.WriteAllBytes(matrix,card.Png);}AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var messageImage=chat.CaptureRenderedFrame();messageImage!.Save(Path.GetFullPath("work/relationship-validation/f3/avalonia/"+language+"-long-welcome-"+matrixTheme+".png"),new PngBitmapEncoderOptions());}
            Named<Expander>(page,"CompanionIntroduction").IsExpanded=true;Complete(page.RefreshAsync());Check(Named<Expander>(page,"CompanionIntroduction").IsExpanded,"long introduction expansion survives refresh");AvaloniaHeadlessPlatform.ForceRenderTimerTick();var maxCaptures=Path.GetFullPath(Path.Combine("work","relationship-validation","f3","avalonia"));Directory.CreateDirectory(maxCaptures);foreach(string maxTheme in new[]{"light","dark"}){DesktopTheme.Apply(maxTheme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var maxImage=chat.CaptureRenderedFrame();maxImage!.Save(Path.Combine(maxCaptures,language+"-maximum-"+maxTheme+".png"),new PngBitmapEncoderOptions());}
            Complete(owner.SelectSkinAsync(originalSkin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());Check(page.View!.CompanionId==originalId&&page.View!.Progress.Score==156,"switching back preserves saved identity score");
            var settings=Named<Expander>(page,"CompanionRelationshipSettings");settings.IsExpanded=true;Dispatcher.UIThread.RunJobs();
            var growth=Named<ToggleSwitch>(page,"RelationshipGrowthEnabled");growth.IsChecked=false;growth.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));
            Complete(page.PendingSettings);Complete(page.RefreshAsync());
            Check(!owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).Settings.GrowthEnabled,"growth checkbox saves global setting");
            var captures=Path.GetFullPath(Path.Combine("work","relationship-validation","f3","avalonia"));Directory.CreateDirectory(captures);
            {
                Complete(page.RefreshAsync());
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{390,520,1100,1320}){DesktopTheme.Apply(theme);chat.Width=width;chat.Height=1040;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();var stage=Named<TextBlock>(page,"CompanionRelationshipStage");var profileAvatar=Named<CompanionPortraitVisual>(page,"CompanionHomeAvatar");var stagePoint=stage.TranslatePoint(new Point(),page)!.Value;var avatarPoint=profileAvatar.TranslatePoint(new Point(),page)!.Value;Check(page.Bounds.Width<834?stagePoint.Y>avatarPoint.Y+profileAvatar.Bounds.Height:stagePoint.X>avatarPoint.X+profileAvatar.Bounds.Width,"responsive profile and relationship bounds "+width+" viewport="+page.Bounds.Width);var score=Named<TextBlock>(page,"CompanionRelationshipScore");Check(score.TranslatePoint(new Point(score.Bounds.Width,0),page)!.Value.X<=page.Bounds.Width,"score stays in viewport "+width);using var image=chat.CaptureRenderedFrame();Check(image!=null,"rendered "+language+" "+theme+" "+width);image!.Save(Path.Combine(captures,language+"-"+theme+"-"+width+".png"),new PngBitmapEncoderOptions());}
            }
            var savedBeforeZero=page.View;var zeroSkin=new PetSkin{Id="home-zero",PickerKey="custom-pack:home-zero",Name="Unmet companion",Prompt="Fixture",Atlas=originalSkin.Atlas,Avatar=SpriteAssets.AvatarPath(originalSkin)};owner.Skins.Add(zeroSkin);Complete(owner.SelectSkinAsync(zeroSkin));Complete(page.RefreshAsync());Check(page.View!.Snapshot.State==null&&page.View!.Progress.Score==0,"unmet companion has real empty zero state");Check(!page.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Name=="CompanionCurrentWelcome"),"uninitialized ledger never synthesizes a first-meeting welcome");typeof(CompanionHomePage).GetProperty("View")!.SetValue(page,savedBeforeZero);DesktopTheme.Apply(DesktopTheme.IsDark?"light":"dark");Dispatcher.UIThread.RunJobs();Check(Named<TextBlock>(page,"CompanionRelationshipScore").Text==RelationshipProgress.FromScore(0).ScoreText,"normal home suite rejects stale high facts during fresh-owner theme refresh");Complete(page.RefreshAsync());foreach(string zeroTheme in new[]{"light","dark"}){DesktopTheme.Apply(zeroTheme);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var zeroImage=chat.CaptureRenderedFrame();zeroImage!.Save(Path.Combine(captures,language+"-zero-"+zeroTheme+".png"),new PngBitmapEncoderOptions());}Complete(owner.SelectSkinAsync(originalSkin));Complete(page.RefreshAsync());
            Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());string file=Path.Combine(directory,"relationships","state.json");byte[] original=File.ReadAllBytes(file);using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllText(file,"{broken");var feedback=Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled");feedback.IsChecked=false;feedback.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));Complete(page.PendingSettings);Complete(page.RefreshAsync());Check(Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled").IsChecked==true&&File.ReadAllText(file)=="{broken","failed setting preserves checkbox and ledger bytes");
            var retained=page.View;var retainedState=retained?.Snapshot?.State;
            Check(retained?.ReadFailed==true&&retainedState!=null&&retainedState.Score==156&&Named<TextBlock>(page,"RelationshipWarning").IsVisible,"corruption retains saved score and retry warning");Check(Named<TextBlock>(page,"CompanionCurrentWelcome").Text==RelationshipContent.Welcome(retained!.Snapshot.StageId,owner.RelationshipPack),"cached saved welcome remains alongside explicit read warning");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var failed=chat.CaptureRenderedFrame())failed!.Save(Path.Combine(captures,language+"-read-failure.png"),new PngBitmapEncoderOptions());
            chat.Width=1100;DesktopTheme.Apply("dark");Dispatcher.UIThread.RunJobs();((Control)Named<Button>(page,"RelationshipRetry").Parent!).BringIntoView();CaptureRecovery(chat,"read-failure-dark");
            var focusedRetry=Named<Button>(page,"RelationshipRetry");focusedRetry.Focus(NavigationMethod.Tab);Dispatcher.UIThread.RunJobs();
            Check(focusedRetry.IsFocused&&ReferenceEquals(chat.FocusManager!.GetFocusedElement(),focusedRetry),"actual headless keyboard focus belongs to recovery retry");CaptureRecovery(chat,"retry-keyboard-focus-dark");
            chat.Width=520;DesktopTheme.Apply("light");Dispatcher.UIThread.RunJobs();((Control)Named<Button>(page,"RelationshipRetry").Parent!).BringIntoView();CaptureRecovery(chat,"read-failure-light-narrow");
            chat.Width=1320;DesktopTheme.Apply("dark");Dispatcher.UIThread.RunJobs();using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllBytes(file,original);Complete(page.RefreshAsync());
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
            savedNotice.SetInteraction(RelationshipContent.Interaction("first_meeting"),"settings-capture-observation");CaptureFeedback(savedNotice,"gain");Complete(page.RefreshAsync());Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;Dispatcher.UIThread.RunJobs();byte[] settingsBytes=File.ReadAllBytes(file);using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllText(file,"{broken");
            // Observe settings-driven dismissal on the same saved notice with a
            // readable response, independently of the short ordinary-gain timeout.
            savedNotice.SetInteraction(RelationshipContent.Interaction("first_meeting"),"settings-failure-observation");
            var failedFeedback=Named<ToggleSwitch>(page,"RelationshipFeedbackEnabled");failedFeedback.IsChecked=false;failedFeedback.RaiseEvent(new RoutedEventArgs(ToggleSwitch.ClickEvent));Complete(page.PendingSettings);
            Check(savedNotice.IsVisible,"failed feedback settings save does not dismiss a visible notice");using(var fixtureLease=new RelationshipStore(directory).Acquire())File.WriteAllBytes(file,settingsBytes);Complete(page.RefreshAsync());Named<Expander>(page,"CompanionRelationshipSettings").IsExpanded=true;Dispatcher.UIThread.RunJobs();
            savedNotice.SetInteraction(RelationshipContent.Interaction("first_meeting"),"settings-success-observation");Check(savedNotice.IsVisible,"saved feedback is still visible immediately before disabling it");
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
                    ((Control)Named<Button>(blockedPage,"RelationshipRestart").Parent!).BringIntoView();CaptureRecovery(blockedWindow,"restart-required-dark");
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
