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
        static void Capture(FrameworkElement w,string file){w.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(file))png.Save(f);}
        static async Task Run(string locale)
        {
            string captures=Path.GetFullPath(Path.Combine("work","relationship-validation","wpf"));Directory.CreateDirectory(captures);
            string root=Path.Combine(captures,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var settings=new PetSettings{UiLanguage=locale,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",ShowPredictedMessages=false};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
            pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);await Task.Run(()=>RelationshipHomeFixtures.Seed156(pet.RelationshipService,pet.RelationshipCompanionId));pet.ShowChat();await Task.Delay(100);var chat=app.Windows.OfType<ChatWindow>().Single(x=>x.IsVisible);
            var input=All<MarkdownEditor>(chat).Single(x=>x.Name=="MessageInput");input.Text="relationship route draft";
            Click(Button(chat,"ShellAvatar"));await Task.Delay(30);var menu=Button(chat,"ShellAvatar").ContextMenu;var entry=menu.Items.OfType<MenuItem>().Single(x=>x.Name=="ProfileCompanionHome");
            Check(entry.IsEnabled&&entry.Focusable&&AutomationProperties.GetName(entry).Contains(pet.Skin.DisplayName),"profile row is enabled, keyboard focusable and accessible");
            Check(((Image)entry.Icon).Source==SkinCatalog.GetAvatar(pet.Skin)&&entry.Opacity==1,"profile avatar retains canonical original color");
            entry.Focus();Check(entry.IsKeyboardFocused,"profile accepts real keyboard focus");entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(entry),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});await Task.Delay(80);Check(!menu.IsOpen&&chat.CurrentPage=="companion","profile click closes menu and opens companion route in same window");
            var page=All<CompanionHomePage>(chat).Single();await page.RefreshAsync();page.UpdateLayout();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            Check(page.View.Snapshot.State.Score==156&&All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipScore").Text=="156 / 240","home opening records actual saved visit");
            Check(All<TextBlock>(page).Single(x=>x.Name=="CompanionActiveDays").Text.Contains("16")&&All<TextBlock>(page).Single(x=>x.Name=="CompanionAcquaintanceDays").Text.Contains("21"),"active and acquaintance days have independent named fields");
            pet.ShowSettings();Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="companion","back from settings restores companion");Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="home"&&input.Text=="relationship route draft","back restores original chat draft");Click(Button(chat,"AppForward"));Check(chat.CurrentPage=="companion","forward restores home page");chat.ShowImagesPage();Click(Button(chat,"AppBack"));Check(chat.CurrentPage=="companion","image route participates in companion history");
            var originalSkin=pet.Skin;var originalId=pet.RelationshipCompanionId;var otherSkin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=originalSkin.PickerKey);
            string otherId=RelationshipIdentity.Resolve(root,otherSkin.PickerKey,otherSkin.IsOfficial?otherSkin.BuiltinCharacterId:null,otherSkin.Id);
            await Task.Run(()=>RelationshipHomeFixtures.SeedMaximum(pet.RelationshipService,otherId));
            Check(pet.SelectSkin(otherSkin),"actual committed companion selection succeeds");Check(chat.CurrentPage=="companion","skin change retains open companion route");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();
            Check(page.View.CompanionId==otherId&&page.View.Progress.Score==1000&&page.View.Progress.NextStageId==null&&((Image)All<Image>(page).Single(x=>x.Name=="CompanionHomeAvatar")).Source==SkinCatalog.GetAvatar(otherSkin),"identity switch binds canonical avatar to its own maxed saved score");
            Capture(chat,Path.Combine(captures,locale+"-maximum.png"));Check(pet.SelectSkin(originalSkin),"original companion restores");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.CompanionId==originalId&&page.View.Progress.Score==156,"switch back restores independent saved score");
            {
                await page.RefreshAsync();page.UpdateLayout();
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{520,1320}){ThemeManager.Apply(theme);chat.Width=width;chat.Height=880;await Task.Delay(90);var relation=(StackPanel)All<TextBlock>(page).Single(x=>x.Name=="CompanionRelationshipStage").Parent;Check(Grid.GetRow(relation)==(width<780?1:0),"responsive relation row "+width);Capture(chat,Path.Combine(captures,locale+"-"+theme+"-"+width+".png"));Check(page.ActualWidth<=chat.ActualWidth,"bounded "+locale+" "+theme+" "+width);}
            }
            var exp=All<Expander>(page).Single(x=>x.Name=="CompanionRelationshipSettings");exp.IsExpanded=true;await Task.Delay(30);var growth=All<CheckBox>(page).Single(x=>x.Name=="RelationshipGrowthEnabled");typeof(CheckBox).GetMethod("OnClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(growth,null);await page.PendingSettings;await page.RefreshAsync();page.UpdateLayout();
            Check(!pet.RelationshipService.GetSnapshot(pet.RelationshipCompanionId).Settings.GrowthEnabled,"growth checkbox persists shared global settings");
            var zeroSkin=pet.Catalog.Skins.First(x=>!x.IsPlaceholder&&x.PickerKey!=originalSkin.PickerKey&&x.PickerKey!=otherSkin.PickerKey);Check(pet.SelectSkin(zeroSkin),"unmet companion selects while growth is off");chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.Snapshot.State==null&&page.View.Progress.Score==0,"unmet companion shows true empty zero state without invented dates");Capture(chat,Path.Combine(captures,locale+"-zero.png"));pet.SelectSkin(originalSkin);chat.ShowCompanionPage();await pet.Relationships.FlushAsync();await page.RefreshAsync();page.UpdateLayout();Check(page.View.CompanionId==originalId,"zero-state return settles original identity");
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
            Console.WriteLine("CompanionHomeUiTests: "+checks+" passed");
        }
        [STAThread]public static int Main(string[] args){string locale=args.Length==1?args[0]:"en_US";TextResources.ConfigureStartupLanguage(locale);app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(locale);result=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
