using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class HolidayKeepsakeUiTests
    {
        static int count;static Application app;
        static readonly string[] Characters={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
        static HolidayCatalog Catalog(){return new HolidayCatalog(Characters.Select(c=>new OutfitDefinition(c,"halloween-2026","halloween","fixture","1.0.0",true,false,new OutfitClipDefinition[0],null,null,null,null)));}
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject
        {if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var value in All<T>(VisualTreeHelper.GetChild(root,i)))yield return value;}
        static void Click(DependencyObject root,string name){All<Button>(root).Single(x=>x.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static void Capture(FrameworkElement visual,string path){visual.UpdateLayout();var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);Console.WriteLine("SCREENSHOT "+path);}
        static async Task Run(string language,bool sample)
        {
            string output=Path.GetFullPath("work/h1b/wpf/"+language);Directory.CreateDirectory(output);string root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var settings=new PetSettings{UiLanguage=language,OnboardingState="completed",QuietMode=true,GreetingEnabled=false,ReadReplies=false,AutoUpdate=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.SetPaused(true);pet.InitializeHoliday(new HolidayController(new HolidayCalendar(),Catalog(),new HolidayState(),_=>{}));
            var earned=new RelationshipTime(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.FromHours(8));var time=earned.AddSeconds(-86400);pet.KeepsakeTime=()=>time;
            using(var page=new CompanionHomePage(pet)){
                var window=new Window{Width=1100,Height=980,Content=page,Background=ThemeManager.Surface};
                await page.RefreshAsync();Check((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==0,"unshown home construction never earns a holiday");
                window.Show();
                foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);await page.RefreshAsync();Click(page,"HolidayAlbumTab");await Task.Delay(40);Capture(window,Path.Combine(output,"locked-home-"+theme+".png"));}
                time=earned;var timer=System.Diagnostics.Stopwatch.StartNew();
                while((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==0){if(timer.ElapsedMilliseconds>10000)throw new TimeoutException("Actual shown home did not earn");await Task.Delay(50);}
                Check((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==1,"actual shown home saves silently in quiet mode with ordinary wardrobe");
                foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);window.Background=ThemeManager.Surface;await page.RefreshAsync();Click(page,"HolidayAlbumTab");await Task.Delay(40);Capture(window,Path.Combine(output,"owned-home-"+theme+".png"));}
                foreach(string character in (sample?Characters.Take(1):Characters)){
                    var observed=await pet.HolidayKeepsakes.ObserveAsync(new HolidayKeepsakeEncounter("official:"+character,character,"伙伴 "+character,earned),Catalog());
                    Console.WriteLine("OBSERVATION "+character+" "+observed.Status+" "+String.Join(",",observed.Results.Select(x=>x.Status)));
                    Check(observed.Status==HolidayKeepsakeObservationStatus.Completed&&observed.Results.Any(x=>x.Status==HolidayKeepsakeStatus.Saved||x.Status==HolidayKeepsakeStatus.AlreadyOwned),"actual aggregate publishes owned "+character);
                    var record=(await pet.HolidayKeepsakes.LoadAsync("official:"+character)).Records.Single();var scene=await pet.HolidayKeepsakes.ReadSceneAsync(record.CompanionId,record.HolidayId,record.InstanceKey);
                    foreach(bool dark in new[]{false,true}){
                        var data=HolidayKeepsakeCardData.Owned(record,HolidayKeepsakeRegistry.BuiltIn);using(var capture=HolidayCardCapture.Create(data,scene.CopyPngBytes(),dark)){
                            File.WriteAllBytes(Path.Combine(output,character+"-owned-"+(dark?"dark":"light")+".png"),capture.Png);
                            var visual=RelationshipCardRenderer.CreateHoliday(data,HolidayCardCapture.Decode(scene.CopyPngBytes()),dark);
                            foreach(var text in visual.Children.OfType<TextBlock>()){
                                var probe=new TextBlock{Text=text.Text,Width=text.Width,FontFamily=text.FontFamily,FontSize=text.FontSize,TextWrapping=text.TextWrapping,Language=text.Language};probe.Measure(new Size(text.Width,Double.PositiveInfinity));Check(probe.DesiredSize.Height<=text.Height+.5,"whole holiday text fits "+language+" "+character+" "+text.Name);
                            }
                            Check(data.YearText=="2026"&&visual.Children.OfType<TextBlock>().All(x=>x.Name!="RelationshipCardAcquaintance"),"saved holiday facts replace stage-only footer");
                            await CheckSave(capture,output,character,dark);
                        }
                    }
                }
                if(!sample&&language=="zh_CN")await States(pet,page,window,root,output);
                window.Close();
            }
            await pet.HolidayKeepsakes.ShutdownAsync();pet.Close();if(!sample&&language=="zh_CN")await Frames(output);Console.WriteLine("HolidayKeepsakeUiTests: "+count+" passed");
        }
        static async Task Frames(string output)
        {
            string root=Path.Combine(output,"frame-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var settings=new PetSettings{UiLanguage="zh_CN",OnboardingState="completed",QuietMode=true,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.SetPaused(true);var holidays=new HolidayController(new HolidayCalendar(),Catalog(),new HolidayState(),_=>{});pet.InitializeHoliday(holidays);holidays.SetEnabled(false);
            var time=new RelationshipTime(new DateTime(2026,10,30,15,59,59,DateTimeKind.Utc),TimeSpan.FromHours(8));pet.KeepsakeTime=()=>time;
            pet.Show();await Task.Delay(80);await pet.HolidayKeepsakes.FlushAsync();Check((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==0,"actual visible pre-midnight WPF buddy has no holiday receipt");
            var locked=typeof(PetWindow).GetField("sessionLocked",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);locked.SetValue(pet,true);time=time.AddSeconds(1);
            var image=(FrameworkElement)typeof(PetWindow).GetField("image",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);image.InvalidateVisual();await Task.Delay(80);await pet.HolidayKeepsakes.FlushAsync();Check((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==0,"actual rendered locked WPF frame at midnight cannot earn");
            pet.Hide();locked.SetValue(pet,false);image.InvalidateVisual();await Task.Delay(80);await pet.HolidayKeepsakes.FlushAsync();Check((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==0,"hidden WPF buddy remains ineligible at holiday boundary");
            pet.Show();image.InvalidateVisual();var deadline=System.Diagnostics.Stopwatch.StartNew();while((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==0){if(deadline.ElapsedMilliseconds>10000)throw new TimeoutException("Visible WPF frame");await Task.Delay(30);}
            Check((await pet.HolidayKeepsakes.LoadAsync(pet.RelationshipCompanionId)).Records.Count==1,"actual unlocked WPF frame earns silently with automatic wardrobe disabled");await pet.HolidayKeepsakes.ShutdownAsync();pet.Close();
        }
        static async Task States(PetWindow owner,CompanionHomePage page,Window window,string root,string output)
        {
            await owner.HolidayKeepsakes.FlushAsync();var original=HolidayKeepsakePresentationChecks.AddHistoricalTwin(root,false);await page.RefreshAsync();Click(page,"HolidayAlbumTab");page.UpdateLayout();
            var cards=All<HolidayCardPreview>(page).ToArray();Check(cards.Length==2&&cards.Select(x=>x.Tag).Distinct().Count()==2,"actual UI cache separates full historical holiday identities");
            var retained=(await owner.HolidayKeepsakes.LoadAsync(owner.RelationshipCompanionId)).Records.Single(x=>x.HolidayId=="historic-halloween");var retainedScene=await owner.HolidayKeepsakes.ReadSceneAsync(retained.CompanionId,retained.HolidayId,retained.InstanceKey);using(var historic=HolidayCardCapture.Create(HolidayKeepsakeCardData.Owned(retained,HolidayKeepsakeRegistry.BuiltIn),retainedScene.CopyPngBytes(),true)){File.WriteAllBytes(Path.Combine(output,"historic-owned-dark.png"),historic.Png);await CheckSave(historic,output,"historic",true);}
        retained.CompanionName=new string((char)0x957f,128)+" Long retained companion name";
        foreach(bool dark in new[]{false,true}){
            var longData=HolidayKeepsakeCardData.Owned(retained,HolidayKeepsakeRegistry.BuiltIn);
            var longImage=HolidayCardCapture.Decode(retainedScene.CopyPngBytes());
            var longVisual=RelationshipCardRenderer.CreateHoliday(longData,longImage,dark);
            var name=longVisual.Children.OfType<TextBlock>().Single(x=>x.Name=="HolidayCardName");
            Check(name.Text==retained.CompanionName&&name.TextWrapping==TextWrapping.NoWrap&&name.TextTrimming==TextTrimming.CharacterEllipsis,"long retained metadata stays bounded with full accessible name and separate ordinary-font year");
            File.WriteAllBytes(Path.Combine(output,"long-metadata-"+(dark?"dark":"light")+".png"),RelationshipCardRenderer.RenderHoliday(longData,longImage,dark));

        }

            var historical=cards.Single(x=>((string)x.Tag).Contains("historic-halloween"));var focus=HomeVisual.CaptureFocus(page,historical.Children.OfType<Button>().Single());var identity=historical.Tag;
            page.ScrollToVerticalOffset(160);await page.RefreshAsync();page.UpdateLayout();
            var resolved=HomeVisual.ResolveFocus(page,focus);Check(resolved!=null&&All<HolidayCardPreview>(page).Single(x=>x.IsAncestorOf(resolved)).Tag.Equals(identity),"refresh focus resolver uses full holiday identity on inactive desktop");Check(Math.Abs(page.VerticalOffset-160)<2,"same-owner refresh preserves scroll");
            foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);await page.RefreshAsync();page.ScrollToVerticalOffset(460);await Task.Delay(40);Capture(window,Path.Combine(output,"history-"+theme+".png"));window.Width=480;await Task.Delay(40);Capture(window,Path.Combine(output,"owned-home-narrow-"+theme+".png"));window.Width=1100;}
            HolidayKeepsakePresentationChecks.AddHistoricalTwin(root,true);await page.RefreshAsync();
            var album=All<HolidayKeepsakeAlbumView>(page).Single();Check(album.SavedCount==2&&album.DecodedCount==1&&All<TextBlock>(album).Any(x=>x.Text==TextResources.Get("holidayKeepsake.sceneError")),"bad blob keeps its count and error metadata without a replacement portrait");Capture(window,Path.Combine(output,"scene-error-dark.png"));
            File.WriteAllBytes(Path.Combine(root,"commemorations/holiday-state.json"),original);await page.RefreshAsync();File.WriteAllText(Path.Combine(root,"commemorations/holiday-state.json"),"{unreadable");await page.RefreshAsync();
            Check(All<TextBlock>(album).Any(x=>x.Text==TextResources.Get("holidayKeepsake.readError"))&&!All<TextBlock>(album).Any(x=>x.Text==TextResources.Format("holidayKeepsake.savedCount",0)),"unreadable album shows unknown count with preserved view");Capture(window,Path.Combine(output,"read-error-dark.png"));Check(File.ReadAllText(Path.Combine(root,"commemorations/holiday-state.json"))=="{unreadable","error UI preserves incompatible original");
            File.WriteAllBytes(Path.Combine(root,"commemorations/holiday-state.json"),original);Directory.CreateDirectory(Path.Combine(root,"relationships"));File.WriteAllText(Path.Combine(root,"relationships/state.json"),"{relationship broken");await page.RefreshAsync();
            Check(page.View.ReadFailed&&album.SavedCount==2&&album.DecodedCount==2&&album.Visibility==Visibility.Visible,"holiday album stays usable with an unreadable independent relationship ledger");Capture(window,Path.Combine(output,"independent-ledger-error-dark.png"));
            using(var fresh=new CompanionHomePage(owner)){window.Content=fresh;await fresh.RefreshAsync();Click(fresh,"HolidayAlbumTab");fresh.UpdateLayout();var independent=All<HolidayKeepsakeAlbumView>(fresh).Single();Check(fresh.View.Snapshot==null&&fresh.View.ReadFailed&&independent.SavedCount==2&&independent.DecodedCount==2,"cold relationship read failure still mounts independently saved holiday album");Capture(window,Path.Combine(output,"cold-ledger-error-dark.png"));window.Content=page;}
        }
        static async Task CheckSave(HolidayCardCapture capture,string output,string character,bool dark)
        {
            bool current=true;var picker=new TaskCompletionSource<string>();var preview=new HolidayCardPreview(capture,()=>current,()=>picker.Task);var save=preview.Children.OfType<Button>().Single();string path=Path.Combine(output,"saved-"+character+"-"+dark+"-"+Guid.NewGuid().ToString("N")+".png");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!File.Exists(path),"pending picker writes nothing");picker.SetResult(path);await preview.PendingSave;Check(File.ReadAllBytes(path).SequenceEqual(capture.Png),"save handler writes exact frozen PNG");
            picker=new TaskCompletionSource<string>();save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.SetResult(String.Empty);await preview.PendingSave;Check(save.IsEnabled&&preview.Children.OfType<TextBlock>().Single().Text==TextResources.Get("relationship.home.saveCancelled")&&File.ReadAllBytes(path).SequenceEqual(capture.Png),"cancel leaves existing file unchanged and allows retry");
        picker=new TaskCompletionSource<string>();save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.SetResult(Path.Combine(output,Guid.NewGuid().ToString("N"),"missing.png"));await preview.PendingSave;Check(save.IsEnabled&&preview.Children.OfType<TextBlock>().Single().Text==TextResources.Get("relationship.card.failed"),"save failure is truthful and retryable");
        picker=new TaskCompletionSource<string>();save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));picker.SetResult(path);await preview.PendingSave;Check(File.ReadAllBytes(path).SequenceEqual(capture.Png),"failed destination can retry exact same frozen card");
        picker=new TaskCompletionSource<string>();save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));current=false;picker.SetResult(path+".stale");await preview.PendingSave;Check(!File.Exists(path+".stale"),"late picker cannot write after owner/page invalidation");
        }
        [STAThread]public static int Main(string[] args)
        {
            if(args.Contains("--compile-only"))return 0;
            if(args.Contains("--contracts")){try{Console.WriteLine("HolidayPresentation: "+HolidayKeepsakePresentationChecks.Run(Path.GetFullPath("work/h1b/contracts-wpf/"+Guid.NewGuid().ToString("N")),new HolidayKeepsakeSceneValidator())+" passed");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}string language=args.FirstOrDefault(x=>x.StartsWith("--locale="));language=language==null?"zh_CN":language.Substring(9);TextResources.ConfigureStartupLanguage(language);
            app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int code=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(language,args.Contains("--sample"));code=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();app.Shutdown();}}));app.Run();return code;
        }
    }
}
