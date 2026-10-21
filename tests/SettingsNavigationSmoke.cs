using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class SettingsNavigationSmoke
    {
        static int passed;
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-navigation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            PetWindow pet=null;SettingsWindow settings=null;
            try
            {
                var cfg=new PetSettings {GreetingEnabled=false,Wander=false,AutoUpdate=false,HideOnFullscreen=false,SaveChatHistory=false};
                foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                cfg.Text=new ConnectionSettings{BaseUrl="https://example.com/v1",Model="configured"};
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump();
                settings=new SettingsWindow(pet);settings.Show();Pump();
                var nav=(ListBox)settings.FindControl("SettingsNavigation");
                foreach(int pageIndex in new[]{0,1,2,3})
                {
                    settings.SelectNavigation(pageIndex);Pump();var page=(ContentControl)settings.FindControl("SettingsContentHost");string pageTitle=new[]{"伙伴","模型配置","工作设置","通用"}[pageIndex];var title=All<TextBlock>(page).Single(t=>t.Text==pageTitle);
                    Check(title.FontSize==26&&title.FontWeight==FontWeights.Bold,"settings page uses the shared title typography: "+pageTitle);
                    Check(All<FrameworkElement>(page).Any(e=>e.Name=="SettingsPageTabs"),"settings page uses the shared full-width tab rail: "+pageTitle);
                }
                settings.SelectNavigation(0);Pump();
                foreach(string theme in new[]{"light","dark"})
                {
                    ThemeManager.Apply(theme);Point? titleOrigin=null,railOrigin=null;double railHeight=0;
                    for(int pageIndex=0;pageIndex<4;pageIndex++)
                    {
                        settings.SelectNavigation(pageIndex);Pump();var page=(ContentControl)settings.FindControl("SettingsContentHost");var title=All<TextBlock>(page).Single(t=>t.Name=="SettingsPageTitle");var rail=All<Border>(page).Single(b=>b.Name=="SettingsPageTabs");var titlePoint=title.TranslatePoint(new Point(),page);var railPoint=rail.TranslatePoint(new Point(),page);
                        if(titleOrigin==null){titleOrigin=titlePoint;railOrigin=railPoint;railHeight=rail.ActualHeight;}
                        Check(Math.Abs(titlePoint.X-titleOrigin.Value.X)<1&&Math.Abs(titlePoint.Y-titleOrigin.Value.Y)<1&&Math.Abs(railPoint.Y-railOrigin.Value.Y)<1&&Math.Abs(rail.ActualHeight-railHeight)<1,"four settings headers align in "+theme+" mode, page "+pageIndex);
                        var background=((SolidColorBrush)rail.Background).Color;Check(theme=="light"?background.R>200:background.R<100,"shared tab rail follows live "+theme+" theme");Capture(settings,Path.Combine(folder,"unified-"+theme+"-"+pageIndex+".png"));
                    }
                    settings.SelectGeneralSection(1);Pump();Capture(settings,Path.Combine(folder,"shortcuts-"+theme+".png"));
                    settings.SelectGeneralSection(2);Pump();Capture(settings,Path.Combine(folder,"history-"+theme+".png"));
                    settings.SelectGeneralSection(0);
                }
                settings.SelectGeneralSection(1);Pump();Check(settings.FindControl("Hotkeyquick")!=null&&settings.FindControl("Hotkeytab")!=null,"general shortcut tab exposes global and window shortcut editors");
                var shortcut=(TextBox)settings.FindControl("Hotkeytab");string savedShortcut=shortcut.Text;shortcut.Text="Ctrl+Alt+Shift+F12";settings.SelectGeneralSection(0);settings.SelectGeneralSection(1);settings.RefreshState();Pump();Check(((TextBox)settings.FindControl("Hotkeytab")).Text=="Ctrl+Alt+Shift+F12","switching general tabs and refreshing retains a shortcut draft");shortcut.Text=savedShortcut;
                settings.SelectGeneralSection(2);Pump();Check(settings.FindControl("SaveChatHistory")!=null&&settings.FindControl("Hotkeyquick")==null,"chat history is separate from shortcut editing");
                settings.SelectGeneralSection(0);ThemeManager.Apply(cfg.ThemeMode);settings.SelectNavigation(0);Pump();
                Check(nav.Items.Count==5,"five primary settings destinations with MCP moved to the shell");
                Check(nav.Items.Cast<ListBoxItem>().Select(i=>(string)i.Content).SequenceEqual(new[]{"伙伴","模型配置","工作设置","通用","关于"}),"navigation combines companion and persona");
                var categorySearch=settings.FindControl("SettingsCategorySearch") as TextBox;Check(categorySearch!=null,"settings sidebar provides category search");categorySearch.Text="模型";Pump();Check(nav.Items.Cast<ListBoxItem>().Where(i=>i.Visibility==Visibility.Visible).Select(i=>(string)i.Content).SequenceEqual(new[]{"模型配置"}),"sidebar search filters categories without changing their route indices");categorySearch.Text="";Pump();Check(nav.Items.Cast<ListBoxItem>().All(i=>i.Visibility==Visibility.Visible),"clearing category search restores all settings destinations");
                bool guide=false;settings.GuideRequested+=delegate {guide=true;};Click(settings,"OpenOnboarding");Check(guide,"sidebar guide entry invokes guide event");
                settings.SelectPage(2);Pump();var model=(TextBox)settings.FindControl("TextProviderName");string original=model.Text;model.Text="navigation-draft";
                settings.SelectPage(3);Pump();Check(settings.FindControl("TranscriptionModel")!=null && nav.SelectedIndex==1,"legacy ASR route opens AI secondary page");
                settings.SelectPage(4);Pump();Check(settings.FindControl("SpeechVoice")!=null,"legacy TTS route opens voice field");
                settings.SelectPage(5);Pump();Check(settings.FindControl("LiveServiceUrl")!=null,"legacy Live route opens realtime settings");
                settings.SelectPage(2);Pump();Check(((TextBox)settings.FindControl("TextProviderName")).Text=="navigation-draft","secondary navigation preserves unsaved draft");
                settings.SelectPage(0);Pump();var picker=(ComboBox)settings.FindControl("SkinPicker");string skinBefore=pet.Skin.Id;picker.SelectedItem=pet.Catalog.Skins.First(s=>s.Id!=skinBefore);Pump();
                Check(pet.Skin.Id==skinBefore && ((SkinDefinition)picker.SelectedItem).Id==skinBefore,"switching partner protects AI draft");
                settings.SelectPage(2);Pump();((TextBox)settings.FindControl("TextProviderName")).Text=original;
                string[] tabs={"AiTextTab","AiAsrTab","AiSpeechTab","AiLiveTab"};string[] fields={"TextProviderName","TranscriptionModel","SpeechVoice","LiveVoice"};
                for(int index=0;index<4;index++)
                {
                    Click(settings,tabs[index]);Pump();Check(settings.FindControl(fields[index])!=null,"secondary button opens "+fields[index]);
                    Capture(settings,Path.Combine(folder,"ai-"+index+"-normal.png"));
                }
                settings.Width=700;settings.Height=430;Pump();
                for(int index=0;index<4;index++)
                {
                    settings.SelectAiSection(index);Pump();
                    Check(tabs.All(name=>Inside(settings.FindControl(name),settings)),"all AI tabs visible at minimum size "+index);
                    Check(Inside(settings.FindControl("SaveConnections"),settings),"save remains visible at minimum size "+index);
                    var target=settings.FocusGuideTarget(fields[index]);Pump();Check(target!=null && target.IsVisible,"guide locates and scrolls to real field "+index);
                    Capture(settings,Path.Combine(folder,"ai-"+index+"-minimum.png"));
                }
                settings.SelectPage(6);Pump();Check(settings.IsOnAboutPage && nav.SelectedIndex==4,"legacy About route uses fifth navigation item");
                nav.ScrollIntoView(nav.Items[4]);Pump();Check(Inside((FrameworkElement)nav.Items[4],settings),"About item reachable at minimum window height");
                settings.SelectPage(7);Pump();Check(settings.FindControl("QuietMode")!=null && nav.SelectedIndex==3 && !settings.IsOnAboutPage,"legacy quiet route uses fourth navigation item");
                string[] companionTabs={"CompanionPersonaTab","CompanionBehaviorTab","CompanionGreetingsTab"};string[] companionFields={"SkinPicker","SizeSlider","GreetingMinMinutes"};
                for(int index=0;index<3;index++)
                {
                    settings.SelectCompanionSection(index);Pump();Check(companionTabs.All(tab=>Inside(settings.FindControl(tab),settings)),"all companion tabs fit minimum size "+index);
                    Check(settings.FocusGuideTarget(companionFields[index])!=null,"companion section exposes its real control "+index);
                    Capture(settings,Path.Combine(folder,"companion-"+index+"-minimum.png"));
                }
                settings.SelectCompanionSection(0);Pump();var prompt=(TextBox)settings.FindControl("PersonaPrompt");string savedPrompt=prompt.Text;prompt.Text="保留人格草稿";
                settings.SelectCompanionSection(1);settings.SelectCompanionSection(2);settings.SelectCompanionSection(0);Pump();
                Check(((TextBox)settings.FindControl("PersonaPrompt")).Text=="保留人格草稿","companion tabs preserve persona draft");prompt.Text=savedPrompt;
                var appearance=pet.ContextMenu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="外观");
                Check(appearance.Items.Count==pet.Catalog.Skins.Count && pet.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="设置"),"context menu separates quick appearance from detailed settings");
                var switchTo=appearance.Items.OfType<MenuItem>().First(i=>(string)i.Tag!=pet.Skin.Id);string expected=(string)switchTo.Tag;switchTo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(pet.Skin.Id==expected,"appearance submenu switches companion directly");
                settings.Width=960;settings.Height=790;settings.SelectPage(0);Pump();Capture(settings,Path.Combine(folder,"companion-normal.png"));
                Console.WriteLine("Settings navigation smoke: "+passed+" passed; previews: "+folder);return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(settings!=null)settings.Close();if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Click(SettingsWindow window,string name){var button=window.FindControl(name) as Button;if(button==null)throw new Exception("Missing "+name);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static bool Inside(FrameworkElement element,Window window){if(element==null || !element.IsVisible)return false;Rect rect=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));return rect.Left>=0 && rect.Top>=0 && rect.Right<=window.ActualWidth && rect.Bottom<=window.ActualHeight;}
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Capture(Window window,string path){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);using(var stream=File.Create(path)){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}}
    }
}
