using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class HolidaySettingsSmoke
    {
        static int passed;
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            try {
                var egg=new OutfitClipDefinition("egg.png",8,8,4,false,480,520,0,0,"egg","default");
                var outfit=new OutfitDefinition("e-bao","halloween","halloween","测试节日装","1",true,true,new OutfitClipDefinition[0],null,egg,null,"测试彩蛋");
                var state=new HolidayState();var controller=new HolidayController(new HolidayCalendar(),new HolidayCatalog(new[]{outfit}),state,s=>{});
                var context=new HolidayContext{PersonaId="e-bao",CharacterId="e-bao",LocalTime=new DateTime(2026,10,31,12,0,0),MonotonicSeconds=100,CanPlayEasterEgg=true,ResourceVersion="test"};controller.Refresh(context);
                using(var panel=new HolidayOutfitPanel(controller)) {
                    Check(panel.Visibility==Visibility.Collapsed,"detected holiday alone never reveals wardrobe");
                    var ticket=controller.TryBeginHover(0);Check(ticket!=null,"fixture begins allowed hover");
                    var playback=new HolidayPlayback();playback.Start(ticket);playback.MarkFirstFrameRendered();controller.MarkHoverFirstFrame(ticket);for(int i=0;i<8;i++)playback.Advance(.25);Check(controller.CompleteHover(ticket),"completed egg unlocks fixture");
                    Check(panel.Visibility==Visibility.Visible,"already open panel refreshes on unlock");
                    var picker=panel.Children.OfType<ComboBox>().Single();Check(picker.Items.Count==2 && picker.IsEnabled,"picker has original and current owned outfit");
                    controller.SetEnabled(false);Check(!picker.IsEnabled && controller.EffectiveOutfit==null,"disable restores original and disables picker");
                    context.PersonaId=context.CharacterId="dafeiyu";controller.Refresh(context);Check(panel.Visibility==Visibility.Visible && picker.Items.Count==1 && picker.Items[0].ToString()=="默认" && picker.IsEnabled,"global discovery keeps an enabled default option for an unowned companion");
                    Check(panel.Children.OfType<Button>().Count()==0,"no replay or unlock buttons");
                }
                VerifyLayout();Console.WriteLine("Holiday settings smoke: "+passed+" passed; screenshots in work/holiday-ui.");
                return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{app.Shutdown();}
        }
        static void VerifyLayout()
        {
            string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"holiday-ui"));Directory.CreateDirectory(folder);PetWindow pet=null;SettingsWindow window=null;
            try {
                var settings=new PetSettings{SkinId="e-bao",GreetingEnabled=false,Wander=false,FollowCursor=false,AutoUpdate=false,HideOnFullscreen=false,SaveChatHistory=false,ThemeMode="light"};foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                string data=Path.Combine(folder,"data-"+Guid.NewGuid().ToString("N"));pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);pet.Show();Pump();
                var definitions=new[]{Outfit("e-bao","绯夜小伯爵"),Outfit("dafeiyu","南瓜海盗船长")};var state=new HolidayState();var store=new HolidayStateStore(Path.Combine(data,"holiday-state.json"));var controller=new HolidayController(new HolidayCalendar(),new HolidayCatalog(definitions),state,store.Save);
                byte[] png=Sheet();pet.HolidayLocalTime=()=>new DateTime(2026,11,2,12,0,0);pet.InitializeHoliday(controller,clip=>new MemoryStream(png,false));
                window=new SettingsWindow(pet){Width=1180,Height=850};window.Show();Pump();
                var skins=(ComboBox)window.FindControl("SkinPicker");var wardrobe=(ComboBox)window.FindControl("HolidayOutfitPicker");var label=window.FindControl("HolidayWardrobeLabel");var import=(Button)window.FindControl("ImportSkin");
                Check(label!=null && wardrobe!=null,"settings exposes the inline 衣柜 heading and picker");
                Check(!wardrobe.IsVisible && !label.IsVisible,"undiscovered wardrobe and heading have no visible placeholder");
                foreach(string theme in new[]{"light","dark"})foreach(double width in new[]{1180d,820d}) {
                    ThemeManager.Apply(theme);window.Width=width;window.Height=width==820?650:850;Pump();
                    Check(!wardrobe.IsVisible && !label.IsVisible,"undiscovered wardrobe remains collapsed in "+width+" "+theme);
                    var row=Find(window,"CompanionWardrobeRow");var position=skins.TranslatePoint(new Point(),window);var action=import.TranslatePoint(new Point(),window);
                    double available=action.Y<position.Y+skins.ActualHeight? action.X-position.X-10:row.ActualWidth;
                    Check(Math.Abs(skins.ActualWidth-available)<2 && skins.ActualWidth>120,"undiscovered companion fills selector area before package actions in "+width+" "+theme);
                    PackageActions(window,skins,skins,import,"undiscovered "+width+" "+theme);
                    Capture(window,Path.Combine(folder,"wpf-before-unlock-"+width+"-"+theme+".png"));
                }
                ThemeManager.Apply("light");window.Width=1180;window.Height=850;Pump();double originalWidth=skins.ActualWidth;
                Capture(window,Path.Combine(folder,"wpf-before-unlock.png"));
                state.Unlock("e-bao","halloween-ui");Check(controller.SelectOwnedOutfit("halloween-ui"),"first completed unlock fixture selects the owned outfit");Pump();
                Check(wardrobe.IsVisible && label.IsVisible,"already-open settings reveals permanent inline wardrobe immediately");EqualRow(window,skins,wardrobe,import,"normal light");Check(skins.ActualWidth<originalWidth*.6,"discovery splits the existing selector area instead of adding a separate full-width picker");
                Check(wardrobe.Items.Count==2 && Text(wardrobe.Items[0])=="默认" && Id(wardrobe.SelectedItem)=="halloween-ui","wardrobe lists 默认 plus only current companion owned outfit");
                Check(All<ComboBox>(window).Count(c=>c.Name=="HolidayOutfitPicker")==1 && !All<TextBlock>(window).Any(t=>t.Text=="特殊服饰"),"wardrobe has no redundant independent selector section");
                Capture(window,Path.Combine(folder,"wpf-unlocked-light.png"));AssertThumbnails(wardrobe);
                wardrobe.IsDropDownOpen=true;Pump();var popup=wardrobe.Template.FindName("PART_Popup",wardrobe) as Popup;Check(popup!=null && popup.IsOpen,"wardrobe options use the existing themed dropdown popup");Capture((FrameworkElement)popup.Child,Path.Combine(folder,"wpf-wardrobe-options-light.png"));wardrobe.IsDropDownOpen=false;
                var prompt=(TextBox)window.FindControl("PersonaPrompt");string saved=prompt.Text;prompt.Text="衣柜更换不覆盖人格草稿";Choose(wardrobe,null);Check(prompt.Text=="衣柜更换不覆盖人格草稿" && state.ForCharacter("e-bao").PreferredOutfitId==null,"wardrobe selection preserves an unsaved persona draft");prompt.Text=saved;Choose(wardrobe,"halloween-ui");
                skins.SelectedItem=pet.Catalog.Find("marfu");Pump();Check(pet.Skin.BuiltinCharacterId=="marfu" && wardrobe.Items.Count==1 && Text(wardrobe.SelectedItem)=="默认" && wardrobe.IsEnabled,"another builtin without ownership keeps only 默认");EqualRow(window,skins,wardrobe,import,"unowned character");Capture(window,Path.Combine(folder,"wpf-unowned-companion.png"));
                state.Unlock("dafeiyu","halloween-ui");skins.SelectedItem=pet.Catalog.Find("dafeiyu");Pump();Choose(wardrobe,"halloween-ui");Check(state.ForCharacter("dafeiyu").PreferredOutfitId=="halloween-ui","second companion saves its own outfit preference");
                skins.SelectedItem=pet.Catalog.Find("e-bao");Pump();Check(Id(wardrobe.SelectedItem)=="halloween-ui","returning to first companion restores its outfit");Choose(wardrobe,null);
                skins.SelectedItem=pet.Catalog.Find("dafeiyu");Pump();Check(Id(wardrobe.SelectedItem)=="halloween-ui" && state.ForCharacter("e-bao").PreferredOutfitId==null,"choosing 默认 for one companion leaves the other companion preference intact");
                var persisted=store.Load();Check(persisted.WardrobeDiscovered && persisted.ForCharacter("e-bao").PreferredOutfitId==null && persisted.ForCharacter("dafeiyu").PreferredOutfitId=="halloween-ui","global discovery and independent preferences persist to app data");
                foreach(string theme in new[]{"light","dark"}) {
                    ThemeManager.Apply(theme);window.Width=820;window.Height=650;Pump();EqualRow(window,skins,wardrobe,import,"narrow "+theme);Check(Inside(skins,window) && Inside(wardrobe,window) && Inside(import,window),"both selectors and import stay inside narrow "+theme+" window");
                    Check(Object.ReferenceEquals(skins.Style,wardrobe.Style),"wardrobe inherits the exact companion ComboBox theme in "+theme);AssertThumbnails(wardrobe);Capture(window,Path.Combine(folder,"wpf-narrow-"+theme+".png"));
                }
                Check(controller.ReportUnavailable(definitions[1]),"runtime-unavailable owned outfit updates the existing wardrobe");wardrobe.IsDropDownOpen=true;Pump();
                var disabled=wardrobe.Items.Cast<object>().Single(item=>Id(item)=="halloween-ui");Check(!((ComboBoxItem)wardrobe.ItemContainerGenerator.ContainerFromItem(disabled)).IsEnabled && ((ComboBoxItem)wardrobe.ItemContainerGenerator.ContainerFromIndex(0)).IsEnabled,"unavailable outfit is disabled while 默认 stays selectable with the inherited item theme");
                Capture((FrameworkElement)((Popup)wardrobe.Template.FindName("PART_Popup",wardrobe)).Child,Path.Combine(folder,"wpf-wardrobe-options-dark-unavailable.png"));wardrobe.IsDropDownOpen=false;
                ThemeManager.Apply("light");window.Width=1180;window.Height=850;Pump();
                string custom=Path.Combine(data,"E包.png");using(var input=typeof(SkinCatalog).Assembly.GetManifestResourceStream("Dafeiyu.EbaoAtlas"))using(var output=File.Create(custom))input.CopyTo(output);var imported=pet.Catalog.Import(custom);
                typeof(SettingsPage).GetMethod("RefreshSkinChoices",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window.Page,null);skins.SelectedItem=imported;Pump();Check(pet.Skin.BuiltinCharacterId==null && wardrobe.Items.Count==1 && Text(wardrobe.SelectedItem)=="默认","same-named custom companion cannot borrow builtin wardrobe ownership");AssertThumbnails(wardrobe);
                Capture(window,Path.Combine(folder,"wpf-custom-companion.png"));
            }finally{if(window!=null)window.Close();if(pet!=null)pet.Close();Pump();}
        }
        static OutfitDefinition Outfit(string character,string name)
        {var clips=IllustratedPack.Actions.Concat(new[]{"stumble","special2"}).Select(action=>new OutfitClipDefinition(action+".png",8,8,4,action=="idle" || action=="sleeping" || action=="lifted",480,520,0,0,action,"default"));return new OutfitDefinition(character,"halloween-ui","halloween",name,"1",true,false,clips,null,new OutfitClipDefinition("egg.png",8,8,4,false,480,520,0,0,"egg","default"),"halloween/"+character+"/preview.png","夹具彩蛋");}
        static byte[] Sheet(){var pixels=new byte[3840*520*4];for(int i=0;i<8;i++)pixels[(120*3840+i*480+80)*4+3]=255;var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3840,520,96,96,PixelFormats.Bgra32,null,pixels,3840*4)));using(var output=new MemoryStream()){encoder.Save(output);return output.ToArray();}}
        static string Id(object item){return item==null?null:(string)item.GetType().GetProperty("Id").GetValue(item,null);}
        static string Text(object item){return item==null?null:item.ToString();}
        static void Choose(ComboBox combo,string id){combo.SelectedItem=combo.Items.Cast<object>().Single(item=>Id(item)==id);Pump();}
        static void EqualRow(Window window,ComboBox skins,ComboBox wardrobe,Button import,string mode)
        {Console.WriteLine("WIDTH "+mode+": companion="+skins.ActualWidth+", wardrobe="+wardrobe.ActualWidth);Check(Math.Abs(skins.ActualWidth-wardrobe.ActualWidth)<1.1 && skins.ActualWidth>120,"same-width companion and wardrobe dropdowns in "+mode);Check(Math.Abs(skins.TranslatePoint(new Point(),window).Y-wardrobe.TranslatePoint(new Point(),window).Y)<1 && Math.Abs(skins.ActualHeight-wardrobe.ActualHeight)<1,"dropdowns share top edge and height in "+mode);PackageActions(window,skins,wardrobe,import,mode);}
        static void PackageActions(Window window,ComboBox left,ComboBox right,Button import,string mode)
        {
            var bulk=Find(window,"DownloadAllOfficialCompanions");Check(import.IsVisible && bulk!=null && bulk.IsVisible,"import and full download remain visible in "+mode);
            var first=left.TranslatePoint(new Point(),window);var edge=right.TranslatePoint(new Point(),window);var location=import.TranslatePoint(new Point(),window);var download=bulk.TranslatePoint(new Point(),window);
            bool beside=Math.Abs(location.Y-edge.Y)<2 && Math.Abs(location.X-edge.X-right.ActualWidth-10)<2;
            bool below=location.Y>=edge.Y+right.ActualHeight && Math.Abs(location.X-first.X)<2;
            Console.WriteLine("LAYOUT "+mode+": left="+first+" / "+left.RenderSize+", right="+edge+" / "+right.RenderSize+", import="+location+", full="+download);
            Check(beside || below,"package actions fit beside or below filled selector area in "+mode);
            Check(download.X>=location.X+import.ActualWidth && Math.Abs(download.Y-location.Y)<2,"full download follows import on the same action row in "+mode);
            Check(Inside(left,window) && Inside(right,window) && Inside(import,window) && Inside(bulk,window),"selectors and both package actions stay inside window in "+mode);
        }
        static FrameworkElement Find(DependencyObject root,string name){var element=root as FrameworkElement;if(element!=null&&element.Name==name)return element;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;}
        static void AssertThumbnails(ComboBox combo)
        {Check(combo.ItemTemplate!=null,"wardrobe options have a thumbnail item template");bool open=combo.IsDropDownOpen;combo.IsDropDownOpen=true;Pump();foreach(object item in combo.Items){Check(item.GetType().GetProperty("Thumbnail").GetValue(item,null)!=null,"thumbnail data is loaded for "+Text(item));var container=combo.ItemContainerGenerator.ContainerFromItem(item) as ComboBoxItem;Check(container!=null && All<Image>(container).Any(image=>image.Source!=null),"actual dropdown renders a thumbnail for "+Text(item));}combo.IsDropDownOpen=open;Pump();}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static bool Inside(FrameworkElement element,Window window){Rect r=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));return r.Left>=0 && r.Top>=0 && r.Right<=window.ActualWidth && r.Bottom<=window.ActualHeight;}
        static void Capture(FrameworkElement element,string path){var window=element as Window;if(window!=null)element=(FrameworkElement)window.Content;element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);using(var output=File.Create(path)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);}}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
    }
}
