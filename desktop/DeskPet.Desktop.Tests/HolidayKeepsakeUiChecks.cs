using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Desktop;
namespace DeskPet.Desktop.Tests;
internal static class HolidayKeepsakeUiChecks
{
    static int count;
    static void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    static void Complete(Task task){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>30000)throw new TimeoutException("Holiday fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
    static T Result<T>(Task<T> task){Complete(task);return task.Result;}
    static readonly string[] Characters={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
    static HolidayCatalog Catalog()=>new(Characters.Select(c=>new OutfitDefinition(c,"halloween-2026","halloween","fixture", "1.0.0",true,false,Array.Empty<OutfitClipDefinition>(),null,null,null,null)));
    static readonly RelationshipTime Earned=new(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.FromHours(8));
    internal static int RunLifecycle(){TextResources.ConfigureStartupLanguage("zh_CN");string output=Path.GetFullPath("work/h1b/lifecycle-only");Directory.CreateDirectory(output);Frames(output);return count;}
    internal static int Run(string language="zh_CN",bool sample=false)
    {
        TextResources.ConfigureStartupLanguage(language);
        string output=Path.GetFullPath("work/h1b/avalonia/"+language);Directory.CreateDirectory(output);
        string root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        new SettingsStore(Path.Combine(root,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false,QuietMode=true,GreetingEnabled=false});
        var holidays=new HolidayController(new HolidayCalendar(),Catalog(),new HolidayState(),_=>{});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(root),holidays:holidays);
        var encounterTime=Earned.AddSeconds(-86400);owner.KeepsakeTime=()=>encounterTime;
        using var page=new CompanionHomePage(owner);var window=new Window{Width=1100,Height=980,Content=page};
        Complete(page.RefreshAsync());
        Check(Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Count==0,"constructing/reading an unshown home never earns a card");
        foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Complete(page.RefreshAsync());window.Show();Click(page,"HolidayAlbumTab");Capture(window,Path.Combine(output,"locked-home-"+theme+".png"));window.Hide();}
        bool earnedSeen=false;owner.HolidayKeepsakes.Changed+=_=>earnedSeen=true;encounterTime=Earned;window.Show();
        // This is the actual hosted shown-home entry: its one-second pulse uses injected time.
        var frame=new DispatcherFrame();var deadline=System.Diagnostics.Stopwatch.StartNew();
        using(var timer=new System.Threading.Timer(_=>Dispatcher.UIThread.Post(()=>{if(earnedSeen||deadline.ElapsedMilliseconds>10000)frame.Continue=false;}),null,0,20))Dispatcher.UIThread.PushFrame(frame);
        Complete(owner.HolidayKeepsakes.FlushAsync());Complete(page.RefreshAsync());
        var album=Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId));
        Check(album.Records.Count==1,"actual shown home silently saves one holiday card while quiet and ordinary wardrobe are active");
        foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Complete(page.RefreshAsync());Click(page,"HolidayAlbumTab");Capture(window,Path.Combine(output,"owned-home-"+theme+".png"));}
        var record=album.Records.Single();
        foreach(string character in (sample?Characters.Take(1):Characters)){
            var observed=Result(owner.HolidayKeepsakes.ObserveAsync(new HolidayKeepsakeEncounter("official:"+character,character,"伙伴 "+character,Earned),Catalog()));
            Console.WriteLine("OBSERVATION "+character+" "+observed.Status+" "+String.Join(",",observed.Results.Select(x=>x.Status)));
            Check(observed.Status==HolidayKeepsakeObservationStatus.Completed&&observed.Results.Any(x=>x.Status==HolidayKeepsakeStatus.Saved||x.Status==HolidayKeepsakeStatus.AlreadyOwned),"actual aggregate publishes owned "+character);
            var saved=Result(owner.HolidayKeepsakes.LoadAsync("official:"+character)).Records.Single();
            var scene=Result(owner.HolidayKeepsakes.ReadSceneAsync(saved.CompanionId,saved.HolidayId,saved.InstanceKey));
            foreach(bool dark in new[]{false,true}){
                var data=HolidayKeepsakeCardData.Owned(saved,HolidayKeepsakeRegistry.BuiltIn);using var capture=HolidayCardCapture.Create(data,scene.CopyPngBytes(),dark);
                File.WriteAllBytes(Path.Combine(output,character+"-owned-"+(dark?"dark":"light")+".png"),capture.Png);
                using var source=new MemoryStream(scene.CopyPngBytes());using var bitmap=Bitmap.DecodeToWidth(source,800);using var visual=RelationshipCardRenderer.CreateHoliday(data,bitmap,dark);
                foreach(var text in visual.Children.OfType<TextBlock>()){
                    var measure=new TextBlock{Text=text.Text,Width=text.Width,FontFamily=text.FontFamily,FontSize=text.FontSize,TextWrapping=text.TextWrapping};measure.Measure(new Size(text.Width,Double.PositiveInfinity));Check(measure.DesiredSize.Height<=text.Height+.5,"whole holiday text fits "+language+" "+character+" "+text.Name);
                }
                Check(visual.Children.OfType<TextBlock>().All(x=>x.Name!="RelationshipCardAcquaintance")&&data.YearText=="2026","holiday uses saved year/date without stage-only day footer");
                CheckSave(capture,output,character,dark);
            }
        }
        if(!sample&&language=="zh_CN")States(owner,page,window,root,output);
        window.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);
        if(!sample&&language=="zh_CN")Frames(output);
        return count;
    }
    static void States(DesktopController owner,CompanionHomePage page,Window window,string root,string output)
    {
        Complete(owner.HolidayKeepsakes.FlushAsync());var original=HolidayKeepsakePresentationChecks.AddHistoricalTwin(root,false);Complete(page.RefreshAsync());Click(page,"HolidayAlbumTab");
        var cards=page.GetLogicalDescendants().OfType<HolidayCardPreview>().ToArray();
        Check(cards.Length==2&&cards.Select(x=>x.Tag).Distinct().Count()==2,"actual UI cache keeps same-calendar historical holiday identities separate");
        var retained=Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Single(x=>x.HolidayId=="historic-halloween");var retainedScene=Result(owner.HolidayKeepsakes.ReadSceneAsync(retained.CompanionId,retained.HolidayId,retained.InstanceKey));using(var historic=HolidayCardCapture.Create(HolidayKeepsakeCardData.Owned(retained,HolidayKeepsakeRegistry.BuiltIn),retainedScene.CopyPngBytes(),true)){File.WriteAllBytes(Path.Combine(output,"historic-owned-dark.png"),historic.Png);CheckSave(historic,output,"historic",true);}
        retained.CompanionName=new string((char)0x957f,128)+" Long retained companion name";
        foreach(bool dark in new[]{false,true}){
            var longData=HolidayKeepsakeCardData.Owned(retained,HolidayKeepsakeRegistry.BuiltIn);
            using var longSource=new MemoryStream(retainedScene.CopyPngBytes());using var longImage=Bitmap.DecodeToWidth(longSource,800);
            var longVisual=RelationshipCardRenderer.CreateHoliday(longData,longImage,dark);
            var name=longVisual.Children.OfType<TextBlock>().Single(x=>x.Name=="HolidayCardName");
            Check(name.Text==retained.CompanionName&&name.TextWrapping==Avalonia.Media.TextWrapping.NoWrap&&name.TextTrimming==Avalonia.Media.TextTrimming.CharacterEllipsis,"long retained metadata stays bounded with full accessible name and separate ordinary-font year");
            File.WriteAllBytes(Path.Combine(output,"long-metadata-"+(dark?"dark":"light")+".png"),RelationshipCardRenderer.RenderHoliday(longData,longImage,dark));
            longVisual.Dispose();
        }

        var second=cards.Single(x=>((string)x.Tag!).Contains("historic-halloween"));var focused=second.Children.OfType<Button>().Single();focused.Focus();
        var fullKey=second.Tag;page.Offset=new Vector(0,160);Complete(page.RefreshAsync());
        var current=TopLevel.GetTopLevel(page)!.FocusManager!.GetFocusedElement() as Control;
        Check(current?.GetVisualAncestors().OfType<HolidayCardPreview>().Single().Tag?.Equals(fullKey)==true,"refresh restores focus to the full historical identity");
        Check(Math.Abs(page.Offset.Y-160)<2,"same-owner refresh preserves home scroll");
        foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Complete(page.RefreshAsync());page.Offset=new Vector(0,460);Capture(window,Path.Combine(output,"history-"+theme+".png"));window.Width=480;Dispatcher.UIThread.RunJobs();Capture(window,Path.Combine(output,"owned-home-narrow-"+theme+".png"));window.Width=1100;}
        HolidayKeepsakePresentationChecks.AddHistoricalTwin(root,true);Complete(page.RefreshAsync());
        var album=page.GetLogicalDescendants().OfType<HolidayKeepsakeAlbumView>().Single();
        Check(album.SavedCount==2&&album.DecodedCount==1&&album.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Text==TextResources.Get("holidayKeepsake.sceneError")),"one bad blob keeps actual count and error metadata without replacing the scene");
        Capture(window,Path.Combine(output,"scene-error-dark.png"));File.WriteAllBytes(Path.Combine(root,"commemorations/holiday-state.json"),original);Complete(page.RefreshAsync());
        File.WriteAllText(Path.Combine(root,"commemorations/holiday-state.json"),"{unreadable");Complete(page.RefreshAsync());
        Check(album.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Text==TextResources.Get("holidayKeepsake.readError"))&&!album.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Text==TextResources.Format("holidayKeepsake.savedCount",0)),"unreadable album shows unknown count and retained prior view");Capture(window,Path.Combine(output,"read-error-dark.png"));
        Check(File.ReadAllText(Path.Combine(root,"commemorations/holiday-state.json"))=="{unreadable","error view preserves original incompatible state");
        File.WriteAllBytes(Path.Combine(root,"commemorations/holiday-state.json"),original);Directory.CreateDirectory(Path.Combine(root,"relationships"));File.WriteAllText(Path.Combine(root,"relationships/state.json"),"{relationship broken");Complete(page.RefreshAsync());
        Check(page.View!.ReadFailed&&album.SavedCount==2&&album.DecodedCount==2&&album.IsVisible,"holiday album remains reachable/readable when independent relationship ledger fails");Capture(window,Path.Combine(output,"independent-ledger-error-dark.png"));
        using(var fresh=new CompanionHomePage(owner)){window.Content=fresh;Complete(fresh.RefreshAsync());Click(fresh,"HolidayAlbumTab");var independent=fresh.GetLogicalDescendants().OfType<HolidayKeepsakeAlbumView>().Single();Check(fresh.View!.Snapshot==null&&fresh.View.ReadFailed&&independent.SavedCount==2&&independent.DecodedCount==2,"cold home with unreadable relationship ledger independently mounts retained holiday album");Capture(window,Path.Combine(output,"cold-ledger-error-dark.png"));window.Content=page;}
    }
    static void Frames(string output)
    {
        string root=Path.Combine(output,"frame-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);new SettingsStore(Path.Combine(root,"settings.json")).Save(new PetSettings{UiLanguage="zh_CN",OnboardingState="completed",AutoUpdate=false,QuietMode=true,GreetingEnabled=false});
        var holidays=new HolidayController(new HolidayCalendar(),Catalog(),new HolidayState(),_=>{});holidays.SetEnabled(false);
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(root),holidays:holidays);var time=Earned.AddSeconds(-86400);owner.KeepsakeTime=()=>time;holidays.SetEnabled(false);Check(!holidays.Enabled,"fixture automatic wardrobe disabled");
        void Render(){((SpriteView)typeof(PetWindow).GetField("sprite",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.Pet)!).InvalidateVisual();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=owner.Pet.CaptureRenderedFrame();Complete(owner.HolidayKeepsakes.FlushAsync());}
        owner.Pet.Show();Render();Check(Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Count==0,"actual visible nonholiday frame has no holiday receipt");
        owner.Pet.Hide();time=Earned;Render();Check(Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Count==0,"hidden buddy crossing midnight earns nothing");
        var interactive=typeof(PetWindow).GetField("sessionInteractive",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;interactive.SetValue(owner.Pet,false);owner.Pet.Show();Render();Check(Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Count==0,"actual rendered locked buddy frame earns nothing");
        interactive.SetValue(owner.Pet,true);Render();Check(Result(owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Count==1,"actual visible unlocked frame earns while wardrobe disabled and quiet");
        Complete((Task)typeof(DesktopController).GetMethod("DrainApplicationForExitAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner,null)!);Check(Result(owner.HolidayKeepsakes.ObserveAsync(new HolidayKeepsakeEncounter(owner.RelationshipCompanionId,"e-bao","resumed after preparation",Earned),Catalog())).Status==HolidayKeepsakeObservationStatus.Completed,"ordinary exit preparation leaves admission available until final exit is accepted");
        owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);Check(Result(owner.HolidayKeepsakes.ObserveAsync(new HolidayKeepsakeEncounter(owner.RelationshipCompanionId,"e-bao","late",Earned),Catalog())).Status==HolidayKeepsakeObservationStatus.Closed,"application shutdown closes and awaits holiday runtime");
    }
    internal static int BetweenReadUi()
    {
        TextResources.ConfigureStartupLanguage("zh_CN");
        foreach(bool incompatible in new[]{true,false})using(var fixture=new HolidayKeepsakePresentationChecks.BetweenReadsFixture(Path.GetFullPath("work/h1b/r1/native-ui-"+Guid.NewGuid().ToString("N")),new HolidayKeepsakeSceneValidator()))
        using(var view=new HolidayKeepsakeAlbumView(fixture.Runtime,()=>fixture.CapturedCatalog,()=>Task.FromResult(true),()=>true,fixture.DirectoryPath)){
            var window=new Window{Width=900,Height=760,Content=new ScrollViewer{Content=view}};window.Show();
            try{
                Complete(view.SetIdentityAsync(fixture.Saved.CompanionId,fixture.Saved.CharacterId,fixture.Saved.CompanionName));
                Check(view.SavedCount==2&&view.DecodedCount==2,"healthy real-store page displays both retained scenes before phase failure");
                Complete(fixture.StartFailure(()=>view.RefreshAsync(),incompatible));
                Check(fixture.MutationRan&&view.SavedCount==2&&view.DecodedCount==0,"between-read failure preserves actual saved count without inventing images");
                var details=view.GetLogicalDescendants().OfType<TextBlock>().Where(x=>x.Name=="HolidayCardErrorMetadata").ToArray();
                Check(details.Length==2&&details.Select(x=>x.Tag).Distinct().Count()==2,"actual native error view retains both full historical record identities");
                Check(details.Any(x=>x.Text!.Contains(fixture.Saved.CompanionName)&&x.Text.Contains("2026")&&x.Text.Contains(RelationshipCardData.FormatDate(fixture.Saved.EarnedLocalDate))),"actual native error metadata keeps saved name/year/date");
                Check(view.GetLogicalDescendants().OfType<TextBlock>().Count(x=>x.Text==TextResources.Get("holidayKeepsake.sceneError"))==2&&view.GetLogicalDescendants().OfType<Button>().Count(x=>x.Name=="SaveHolidayCard"&&!x.IsEnabled)==2,"actual native state-read errors are explicit and saved actions disabled");
                fixture.Restore();Complete(view.RefreshAsync());
                var restored=view.GetLogicalDescendants().OfType<HolidayCardPreview>().Single(x=>x.Capture.Data.HolidayId=="halloween").Capture.Data.Record;
                Check(view.SavedCount==2&&view.DecodedCount==2&&restored.SceneSha256==fixture.Saved.SceneSha256&&restored.EarnedLocalDate==fixture.Saved.EarnedLocalDate,"restored native page uses the original saved scene hash and facts");
            }finally{window.Close();}
        }
        return count;
    }
    internal static int FolderFailureUi()
    {
        string root=Path.GetFullPath("work/m6a/folder-native-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"commemorations"));
        string file=Path.Combine(root,"commemorations/holiday-state.json");File.WriteAllText(file,"{broken-owned-fixture");byte[] original=File.ReadAllBytes(file);
        var runtime=new HolidayKeepsakeRuntime(root,new Dafeiyu.Maintenance.DataMaintenanceGate(),new HolidayCalendar(),HolidayKeepsakeRegistry.BuiltIn,new RelationshipSceneProvider(asset=>File.OpenRead(Path.Combine("assets",asset.RelativePath))),new HolidayKeepsakeSceneValidator());
        try{foreach(var error in new Exception[]{new IOException("owned IO"),new UnauthorizedAccessException("owned access"),new System.ComponentModel.Win32Exception(5),new InvalidOperationException("owned shell")}){
            int launches=0;using var view=new HolidayKeepsakeAlbumView(runtime,()=>Catalog(),()=>Task.FromResult(true),()=>true,root,path=>{Check(path==root,"folder launcher receives exact owned data directory");launches++;if(launches<=2)throw error;});
            var window=new Window{Content=view,Width=800,Height=600};window.Show();
            try{Complete(view.SetIdentityAsync("official:e-bao","e-bao","Owned"));
                var button=view.GetLogicalDescendants().OfType<Button>().Single(x=>x.Content as string==TextResources.Get("relationship.home.openDataFolder"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(view.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.IsEffectivelyVisible&&x.Text==TextResources.Get("relationship.home.folderFailed")),"album visibly reports expected folder launch failure "+error.GetType().Name);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(launches==2&&button.IsEnabled,"folder recovery action remains retryable");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(launches==3&&view.GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Name=="HolidayFolderStatus"&&x.Text==""),"successful recovery retry clears stale folder failure status");
                Check(File.ReadAllBytes(file).SequenceEqual(original),"folder launch failure preserves original holiday bytes");
            }finally{window.Close();}
        }}finally{Complete(runtime.ShutdownAsync());}return count;
    }
    static void CheckSave(HolidayCardCapture capture,string output,string character,bool dark)
    {
        bool current=true;var picker=new TaskCompletionSource<string>();var preview=new HolidayCardPreview(capture,()=>current,()=>picker.Task);var button=preview.Children.OfType<Button>().Single();
        string destination=Path.Combine(output,"saved-"+character+"-"+dark+"-"+Guid.NewGuid().ToString("N")+".png");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!File.Exists(destination),"pending picker writes nothing");picker.SetResult(destination);Complete(preview.PendingSave);Check(File.ReadAllBytes(destination).SequenceEqual(capture.Png),"real save handler writes exact preview PNG");
        picker=new TaskCompletionSource<string>();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.SetResult(String.Empty);Complete(preview.PendingSave);Check(button.IsEnabled&&preview.Children.OfType<TextBlock>().Single().Text==TextResources.Get("relationship.home.saveCancelled")&&File.ReadAllBytes(destination).SequenceEqual(capture.Png),"cancel leaves existing file unchanged and allows retry");
        picker=new TaskCompletionSource<string>();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.SetResult(Path.Combine(output,Guid.NewGuid().ToString("N"),"missing.png"));Complete(preview.PendingSave);Check(button.IsEnabled&&preview.Children.OfType<TextBlock>().Single().Text==TextResources.Get("relationship.card.failed"),"save failure is truthful and retryable");
        picker=new TaskCompletionSource<string>();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.SetResult(destination);Complete(preview.PendingSave);Check(File.ReadAllBytes(destination).SequenceEqual(capture.Png),"failed destination can retry exact same frozen card");
        picker=new TaskCompletionSource<string>();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));current=false;string stale=destination+".stale";picker.SetResult(stale);Complete(preview.PendingSave);Check(!File.Exists(stale),"late picker callback cannot save after identity/page invalidation");
    }
    static void Click(Control root,string name)=>root.GetLogicalDescendants().OfType<Button>().Single(x=>x.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Capture(Window window,string path){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=window.CaptureRenderedFrame();image!.Save(path,new PngBitmapEncoderOptions());Console.WriteLine("SCREENSHOT "+path);}
    sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException();public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException();
    }
}
