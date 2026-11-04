using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class SettingsExpanderTests
    {
        static int checks;
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static ToggleButton Header(Expander e){return All<ToggleButton>(e).First();}
        static void Layout(Expander e)
        {
            var toggle=Header(e);var header=e.Header as FrameworkElement;var arrow=All<System.Windows.Shapes.Path>(toggle).FirstOrDefault();
            Check(toggle.ActualHeight>=44&&Math.Abs(toggle.ActualWidth-e.ActualWidth)<3,e.Name+" has a full-width, at least 44px header target");
            Check(arrow!=null&&Math.Abs(arrow.ActualWidth-14)<.5,e.Name+" has a 14px vector disclosure");
            var p=arrow.TranslatePoint(new Point(7,arrow.ActualHeight/2),toggle);
            Check(Math.Abs(p.Y-toggle.ActualHeight/2)<1&&p.X>toggle.ActualWidth-30,e.Name+" arrow is centered vertically at the right edge");
            if(header!=null){var h=header.TranslatePoint(new Point(0,header.ActualHeight/2),toggle);Check(h.X>=12&&h.X<=15&&Math.Abs(h.Y-p.Y)<1,e.Name+" title and description share the arrow center and left inset");}
            var hit=toggle.InputHitTest(new Point(toggle.ActualWidth*.6,toggle.ActualHeight/2)) as DependencyObject;
            Check(hit!=null,e.Name+" whitespace in the header receives pointer input");
        }
        static void Activate(ToggleButton toggle){typeof(ToggleButton).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(toggle,null);Pump();}
        static void Space(ToggleButton toggle){toggle.Focus();Pump();var source=PresentationSource.FromVisual(toggle);toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Space){RoutedEvent=Keyboard.KeyDownEvent});toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Space){RoutedEvent=Keyboard.KeyUpEvent});Pump();}
        static void Capture(Window host,string name){host.UpdateLayout();var view=(FrameworkElement)host.Content;var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png")))encoder.Save(stream);}
        static void ScrollToGroup(DependencyObject root,string text){var heading=All<TextBlock>(root).First(x=>x.Text==text);DependencyObject ancestor=heading;while(ancestor!=null&&!(ancestor is ScrollViewer))ancestor=VisualTreeHelper.GetParent(ancestor);var scroll=ancestor as ScrollViewer;if(scroll!=null){scroll.ScrollToVerticalOffset(Math.Max(0,heading.TranslatePoint(new Point(),(UIElement)scroll.Content).Y-12));Pump();}}
        [STAThread]public static int Main()
        {
            TextResources.ConfigureStartupLanguage("zh_CN");var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window host=null;PetWindow pet=null;
            try{
                ThemeManager.Apply("light");var summary=new StackPanel();summary.Children.Add(Ui.Text("连接设置",12));summary.Children.Add(Ui.Text("https://example.invalid/v1",10,Ui.Muted));var field=Ui.Field("retained draft");var e=new Expander{Name="TwoLineConnection",Header=summary,Content=field,Margin=new Thickness(16)};
                // The missing shared styling must fail as a real layout regression before it exists.
                var style=typeof(SettingsSurface).GetMethod("StyleExpander",BindingFlags.Static|BindingFlags.NonPublic);if(style!=null)style.Invoke(null,new object[]{e});
                var body=new StackPanel{Background=ThemeManager.Surface};body.Children.Add(e);host=new Window{Content=body,Width=600,Height=300,Background=ThemeManager.Surface};host.Show();Pump();Layout(e);
                Keyboard.ClearFocus();Pump();var focusBorder=All<Border>(Header(e)).First();var restingStroke=((SolidColorBrush)focusBorder.BorderBrush).Color;Header(e).Focus();Pump();Check(Header(e).IsKeyboardFocused&&((SolidColorBrush)focusBorder.BorderBrush).Color!=restingStroke,"keyboard focus is visibly drawn on the rounded header border");
                int expanded=0,collapsed=0;e.Expanded+=delegate{expanded++;};e.Collapsed+=delegate{collapsed++;};Activate(Header(e));Check(e.IsExpanded&&field.IsVisible&&expanded==1,"header activation expands the original content and invokes Expanded once");Space(Header(e));Check(!e.IsExpanded&&!field.IsVisible&&collapsed==1,"Space collapses the body and invokes Collapsed once");Space(Header(e));Check(e.IsExpanded&&field.Text=="retained draft"&&expanded==2,"Space reopens the same editable content without losing the draft");
                var border=All<Border>(Header(e)).First();var light=((SolidColorBrush)border.Background).Color;ThemeManager.Apply("dark");Pump();Check(((SolidColorBrush)border.Background).Color!=light,"the existing header updates when the theme changes");
                foreach(var mode in new[]{"light","dark"})foreach(int width in new[]{600,300}){ThemeManager.Apply(mode);host.Width=width;Pump();Layout(e);Capture(host,"settings-expander-"+mode+"-"+width);}
                string folder=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-expander-"+Guid.NewGuid().ToString("N"));var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",AgentWorkspace=System.IO.Path.Combine(folder,"workspace")};Directory.CreateDirectory(cfg.AgentWorkspace);foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                cfg.Text=new ConnectionSettings{BaseUrl="https://example.invalid/v1",Model="fixture"};var imageProvider=new ImageGenerationProvider{Name="Image fixture",Connection=new ConnectionSettings{BaseUrl="https://example.invalid/v1",Model="fixture-image"}};cfg.ImageProviders.Add(imageProvider);cfg.ActiveImageProviderId=imageProvider.Id;pet=new PetWindow(null,cfg,new SettingsStore(System.IO.Path.Combine(folder,"settings.json")),null);
                var page=new SettingsPage(pet);host.Width=1100;host.Height=850;host.Content=page;
                foreach(int route in new[]{3,4,1,2,7,0}){page.SelectNavigation(route);Pump();foreach(var disclosure in All<Expander>(page).Where(x=>x.IsVisible).ToArray())Layout(disclosure);Capture(host,"settings-disclosures-route-"+route);}
                var visited=new HashSet<string>();foreach(var mode in new[]{"light","dark"})foreach(int width in new[]{1100,640}){
                    ThemeManager.Apply(mode);host.Width=width;
                    page.SelectGeneralSection(2);Pump();var archived=All<Expander>(page).Single(x=>x.Name=="GeneralArchivedConversations");archived.BringIntoView();Pump();Layout(archived);Activate(Header(archived));Check(archived.IsExpanded&&((FrameworkElement)archived.Content).IsVisible,"archived conversation content opens after its original refresh handler");archived.BringIntoView();Pump();Capture(host,"settings-archives-"+mode+"-"+width);Activate(Header(archived));
                    page.SelectNavigation(4);Pump();var network=All<Expander>(page).Single(x=>x.Name=="UpdateNetwork");Layout(network);Capture(host,"settings-about-"+mode+"-"+width);Activate(Header(network));Capture(host,"settings-about-expanded-"+mode+"-"+width);Activate(Header(network));
                    for(int section=0;section<5;section++){page.SelectAiSection(section);Pump();foreach(var disclosure in All<Expander>(page).Where(x=>x.IsVisible)){visited.Add(disclosure.Name);Layout(disclosure);}if(section==0||section==4)Capture(host,"settings-provider-"+section+"-"+mode+"-"+width);}
                    page.SelectCompanionSection(2);Pump();foreach(var disclosure in All<Expander>(page).Where(x=>x.IsVisible)){visited.Add(disclosure.Name);disclosure.BringIntoView();Pump();Layout(disclosure);}Capture(host,"settings-observation-"+mode+"-"+width);
                    page.SelectNavigation(2);Pump();Capture(host,"settings-work-"+mode+"-"+width);ScrollToGroup(page,TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeB"));Capture(host,"settings-work-instructions-"+mode+"-"+width);ScrollToGroup(page,TextResources.Get("workSettingsPanel.workSettingsPanel.addGroup.alternativeH"));Capture(host,"settings-work-capabilities-"+mode+"-"+width);
                }
                Check(new[]{"TextConnectionDetails","AsrConnectionDetails","SpeechConnectionDetails","LiveConnectionDetails","ImageConnectionDetails","DesktopObservationDetails"}.All(visited.Contains),"all five provider sections and desktop-observation settings were exercised");
                page.SelectNavigation(2);Pump();var work=All<WorkSettingsPanel>(page).Single();Check(!All<Expander>(work).Any(x=>x.Name=="AdvancedWorkDirectories"||x.Name=="AdvancedWorkCapabilities"),"work instructions and capability groups have no enclosing disclosure");
                Check(All<TextBox>(work).Any(x=>x.Name=="AgentSkillsDirectory"&&x.IsVisible)&&All<TextBlock>(work).Any(x=>x.IsVisible&&x.Text==TextResources.Get("workSettingsPanel.workSettingsPanel.text.alternativeH")),"work skills fields and full capability boundaries are visible immediately");
                page.Dispose();
                var valid=new PetSettings();valid.Text=new ConnectionSettings{BaseUrl="https://example.invalid/v1",ApiKey="offline-fixture-key",Model="fixture"};var editor=new TextProviderEditor(valid);host.Content=editor;host.Width=1100;Pump();var lazy=All<Expander>(editor).Single(x=>x.Name=="TextConnectionDetails");Check(!lazy.IsExpanded&&!All<TextBox>(editor).Any(x=>x.Name=="TextServiceUrl"),"saved provider initially keeps connection fields deferred");Activate(Header(lazy));var url=All<TextBox>(editor).Single(x=>x.Name=="TextServiceUrl");Check(url.Text=="https://example.invalid/v1","styled header still invokes the actual lazy connection-field loader");url.Text="https://changed.invalid/v1";Space(Header(lazy));Space(Header(lazy));Check(All<TextBox>(editor).Single(x=>x.Name=="TextServiceUrl")==url&&url.Text=="https://changed.invalid/v1","actual provider connection fields keep their draft across keyboard reopen");
                host.Content=null;var im=new ImChannelSettingsPanel(pet);var detail=(StackPanel)typeof(ImChannelSettingsPanel).GetField("details",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(im);var restrictions=(Expander)LogicalTreeHelper.GetChildren(detail).OfType<StackPanel>().SelectMany(x=>LogicalTreeHelper.GetChildren(x).OfType<Expander>()).Single(x=>x.Name=="ImAccessOptions");((Panel)restrictions.Parent).Children.Remove(restrictions);host.Content=restrictions;Pump();Layout(restrictions);Activate(Header(restrictions));Check(restrictions.IsExpanded&&((FrameworkElement)restrictions.Content).IsVisible,"IM access restrictions preserve their actual editable content");im.Dispose();
                Console.WriteLine("Settings expander tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
