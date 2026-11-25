using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class SettingsOrganizationTests
    {
        static int checks;
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static T Named<T>(DependencyObject root,string name)where T:FrameworkElement{return All<T>(root).Single(e=>e.Name==name);}
        static void Capture(Window window,string name){window.UpdateLayout();var view=(FrameworkElement)window.Content;var png=new PngBitmapEncoder();var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png")))png.Save(stream);}
        static double Right(FrameworkElement element,FrameworkElement host){return element.TranslatePoint(new Point(element.ActualWidth,0),host).X;}
        static void CheckDetailedLayout(SettingsPage page,Window host)
        {
            foreach(int route in new[]{0,1,6,2,7,3,4})
            {
                page.SelectNavigation(route);Pump();host.UpdateLayout();
                var active=(FrameworkElement)Named<ContentControl>(page,"SettingsContentHost").Content;
                var body=All<ScrollViewer>(active).First(element=>element.IsVisible&&element.MaxWidth==SettingsSurface.BodyWidth);
                var header=All<SettingsPageHeader>(active).FirstOrDefault(element=>element.IsVisible);
                double left=body.TranslatePoint(new Point(),active).X;
                var available=Math.Min(SettingsSurface.BodyWidth,active.ActualWidth);
                Check(body.ActualWidth>0&&body.ActualWidth<=available+1&&Math.Abs(left+body.ActualWidth/2-active.ActualWidth/2)<2,
                    "settings body stays centered within its width cap on route "+route+
                    " (available "+active.ActualWidth+", body "+body.ActualWidth+", offset "+left+")");
                if(header!=null&&active.ActualWidth>SettingsSurface.BodyWidth+1)
                    Check(header.ActualWidth>body.ActualWidth,"settings header retains its wider layout on route "+route);
            }
            page.RestoreNavigation(3,0,null);Pump();
            var language=Named<Button>(page,"GeneralLanguage");var motion=Named<ComboBox>(page,"ThemeTransitionEffect");var volume=Named<Slider>(page,"ThemeTransitionSoundVolume");
            var sound=Named<CheckBox>(page,"ThemeTransitionSoundEnabled");var track=(FrameworkElement)sound.Template.FindName("track",sound);
            var picker=Named<ThemeModePicker>(page,"ThemeModePicker");
            var systemChoice=picker.Origin("system");var darkChoice=picker.Origin("dark");
            Check(Math.Abs(systemChoice.TranslatePoint(new Point(),picker).X-(picker.ActualWidth-Right(darkChoice,picker)))<2,
                "theme preview frame hugs its cards with balanced padding");
            Check(Math.Abs(Right(picker,page)-Right(language,page))<2,"theme preview frame aligns with the language control");
            Check(Math.Abs(Right(motion,page)-Right(language,page))<2&&Math.Abs(Right(volume,page)-Right(language,page))<2,"theme motion and volume align with the language control");
            var operationVolume=Named<Slider>(page,"CompanionOperationSoundVolume");
            Check(volume.AutoToolTipPlacement==AutoToolTipPlacement.TopLeft&&ReferenceEquals(volume.Template,operationVolume.Template),
                "theme and operation volume share the styled slider and drag value");
            var trackControl=(Track)volume.Template.FindName("PART_Track",volume);
            var knob=trackControl.Thumb;
            var savedValue=volume.Value;
            string originalMode=ThemeManager.Mode;
            ThemeManager.Apply("light");
            volume.Value=53;
            knob.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
            var tooltip=knob.ToolTip as ToolTip;
            Check(tooltip!=null&&tooltip.IsOpen&&Equals(tooltip.Content,"53"),"dragging the volume thumb shows the live number");
            var light=(SolidColorBrush)volume.Foreground;
            var lightColor=light.Color;
            var lightPopup=((SolidColorBrush)tooltip.Background).Color;
            ThemeManager.Apply("dark");
            Check(light.Color!=lightColor&&((SolidColorBrush)tooltip.Background).Color!=lightPopup&&tooltip.Foreground==Ui.Ink,
                "slider progress and value popup follow the active theme");
            knob.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});
            Check(!tooltip.IsOpen,"releasing the thumb hides the value popup");
            volume.Value=savedValue;
            ThemeManager.Apply(originalMode);
            Check(track.TranslatePoint(new Point(),sound).X>sound.ActualWidth/2,"theme sound keeps its switch at the trailing edge");
            Capture(host,"settings-general-wide");
            host.Width=560;Pump();
            Check(All<Button>(picker).Where(button=>button.Name.StartsWith("ThemeChoice",StringComparison.Ordinal)).All(button=>
                button.TranslatePoint(new Point(),picker).X>=0&&Right(button,picker)<=picker.ActualWidth+1),"all theme choices fit in compact settings");
            Check(Math.Abs(Right(picker,page)-Right(language,page))<2&&Math.Abs(Right(motion,page)-Right(language,page))<2&&Math.Abs(Right(volume,page)-Right(language,page))<2,"theme control edges stay aligned in compact settings");
            Capture(host,"settings-general-narrow");
            host.Width=1100;Pump();
        }
        [STAThread] public static int Main()
        {
            TextResources.ConfigureStartupLanguage("zh_CN");var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;SettingsPage page=null;
            try{
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-organization-"+Guid.NewGuid().ToString("N"));var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",AgentWorkspace=Path.Combine(folder,"workspace")};Directory.CreateDirectory(cfg.AgentWorkspace);foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);page=new SettingsPage(pet);host=new Window{Content=page,Width=1100,Height=800};host.Show();Pump();
                var navigation=Named<ListBox>(page,"SettingsNavigation");var actual=navigation.Items.Cast<ListBoxItem>().Select(i=>(string)i.Content).ToArray();
                Check(actual.SequenceEqual(new[]{"伙伴","模型配置","对话与输入","工作与权限","工具与连接","通用"}),"six settings categories have the approved labels and order");
                Check(Named<Button>(page,"SettingsAboutNavigation").IsVisible,"about and updates has a permanent footer entry");
                page.RestoreNavigation(0,0,null);Pump();
                var switchEffect=Named<ComboBox>(page,"CompanionTransitionEffect");
                Check(switchEffect.IsVisible&&switchEffect.Items.Cast<ComboBoxItem>().Select(item=>(string)item.Tag)
                    .SequenceEqual(new[]{"off","ripple","zipper","random"}),"partner settings show the four transition choices in order");
                Check((string)switchEffect.SelectedValue=="ripple","partner transition starts at ripple");
                switchEffect.SelectedValue="off";page.RefreshState();Pump();
                Check(pet.Store.Load().CompanionTransitionEffect=="off"&&(string)switchEffect.SelectedValue=="off","partner motion selection saves immediately");
                CheckDetailedLayout(page,host);
                page.RestoreNavigation(6,0,null);Pump();Check(new[]{"ConversationDefaultsTab","ConversationInputTab","ConversationDisplayTab"}.All(n=>Named<Button>(page,n).IsVisible),"conversation settings offer three real content tabs");page.RestoreNavigation(6,1,null);Pump();var shortcut=Named<ComboBox>(page,"SendShortcut");int choice=shortcut.SelectedIndex==0?1:0;shortcut.SelectedIndex=choice;
                page.SelectNavigation(2);Pump();Check(!All<ComboBox>(page).Any(c=>c.Name=="SendShortcut"&&c.IsVisible),"work and permissions does not contain global input preferences");
                page.SelectNavigation(6);Pump();Check(Named<ComboBox>(page,"SendShortcut").SelectedIndex==choice&&page.HasUnsavedEdits,"navigation preserves the unsaved conversation draft");Capture(host,"settings-conversation-layout");
                page.SelectNavigation(1);Pump();Check(!All<CheckBox>(page).Any(c=>c.Name=="WebSearchEnabled"),"web search is removed from model configuration");Check(Named<Button>(page,"AiTextTab").IsVisible,"existing model tabs are retained");
                page.SelectNavigation(7);Pump();Check(Named<CheckBox>(page,"WebSearchEnabled").IsVisible,"web search lives under tools and connections");Capture(host,"settings-tools-layout");
                Check(!All<Button>(page).Any(b=>b.Name=="ToolsPluginsTab"),"tools does not duplicate the primary plugins page");page.RestoreNavigation(7,2,"ToolsChannelsTab");Pump();Check(Named<Button>(page,"ToolsChannelsTab").IsVisible&&page.SelectedSecondarySection==2,"message channels retain their navigation route");page.SelectNavigation(7);Pump();
                var search=Named<TextBox>(page,"SettingsCategorySearch");search.Text="网页搜索";search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(search),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();
                Check(page.SelectedNavigationIndex==7&&Named<CheckBox>(page,"WebSearchEnabled").IsKeyboardFocusWithin,"search locates and focuses a specific setting without enabling it");Check(!pet.Settings.WebSearch.Enabled,"search navigation did not execute the toggle");
                search.Clear();Named<Button>(page,"SettingsAboutNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(page.IsOnAboutPage,"footer about entry preserves the existing about route");
                host.Width=640;page.SelectNavigation(7);Pump();Capture(host,"settings-tools-narrow");Check(Named<CheckBox>(page,"WebSearchEnabled").ActualWidth>0,"tools settings remain usable in a narrow window");
                pet.ShowSettings();Pump();var main=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);main.Width=1280;main.Height=850;var integrated=All<SettingsPage>(main).Single();integrated.RestoreNavigation(6,1,null);Pump();
                Check(new[]{"ShellHome","ShellImages","ShellUsage","ShellMcp","ShellSettings"}.All(n=>Named<Button>(main,n).IsVisible),"the full application keeps every original primary navigation icon");Capture(main,"settings-full-window");
                main.Width=main.MinWidth;integrated.RestoreNavigation(3,0,null);Pump();
                var appearance=(FrameworkElement)Named<ContentControl>(integrated,"SettingsContentHost").Content;
                var viewport=All<ScrollViewer>(appearance).First(element=>element.IsVisible);
                var choices=new[]{"system","light","dark"}.Select(mode=>Named<Button>(appearance,"ThemeChoice"+mode));
                Check(choices.All(option=>option.IsVisible&&option.Focusable&&option.TranslatePoint(new Point(),viewport).X>=0&&Right(option,viewport)<=viewport.ActualWidth+1),"theme choices fit at the embedded settings window minimum");
                Capture(main,"settings-embedded-narrow");
                main.Width=1280;Named<Button>(main,"ShellMcp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(All<PluginsPage>(main).Any(p=>p.IsVisible),"skills and MCP remain available from the primary plugins icon");
                Console.WriteLine("Settings organization tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(page!=null)page.Dispose();if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
