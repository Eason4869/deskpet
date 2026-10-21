using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ThemeTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Capture(Window window,string name)
        {
            window.UpdateLayout();var root=Path.Combine(Directory.GetCurrentDirectory(),"work","theme-evidence");Directory.CreateDirectory(root);
            var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(window);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using(var file=File.Create(Path.Combine(root,name)))png.Save(file);
        }
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                var property=typeof(PetSettings).GetProperty("ThemeMode");
                Check(property!=null,"theme preference is persisted in settings");
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false};
                Check((string)property.GetValue(cfg,null)=="system","new settings follow system");
                property.SetValue(cfg,"invalid",null);cfg.Normalize();Check((string)property.GetValue(cfg,null)=="system","invalid preference falls back to system");
                string root=Path.Combine(Directory.GetCurrentDirectory(),"work","theme-tests-"+Guid.NewGuid().ToString("N"));var store=new SettingsStore(Path.Combine(root,"settings.json"));
                property.SetValue(cfg,"dark",null);store.Save(cfg);Check((string)property.GetValue(store.Load(),null)=="dark","dark preference survives reload");
                foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,store,null);pet.ShowSettings();Pump();
                app.Windows.Cast<Window>().SelectMany(w=>All<SettingsPage>(w)).Single().SelectNavigation(3);Pump();
                var picker=app.Windows.Cast<Window>().SelectMany(w=>All<ThemeModePicker>(w)).SingleOrDefault();
                Check(picker!=null,"general settings expose theme selection");
                var ink=Ui.Ink;Check(ink.Color.R>160,"startup applies saved dark palette");
                var main=Window.GetWindow(picker);
                Check(All<CheckBox>(main).Where(c=>c.IsVisible).All(c=>c.Foreground==Ui.Ink),"general switches use theme text instead of default black");Capture(main,"settings-dark.png");
                All<Button>(picker).Single(b=>b.Name=="ThemeChoicelight").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(ink.Color.R<100,"switch to light updates existing brushes");Capture(main,"settings-light.png");
                All<Button>(picker).Single(b=>b.Name=="ThemeChoicedark").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(ink.Color.R>160,"switch to dark updates existing brushes");
                Check((string)property.GetValue(store.Load(),null)=="dark","theme selection saves immediately");
                pet.ShowOnboarding();Pump();var guide=app.Windows.OfType<OnboardingWindow>().Single();var exit=All<Button>(guide).Single(b=>b.Name=="GuideSkip");
                Check(exit.FontSize>=13&&exit.FontWeight==FontWeights.SemiBold,"guide exit is larger and semibold");
                Check(exit.Foreground==Ui.Ink,"guide exit uses high contrast text");Capture(guide,"guide-dark.png");
                ThemeManager.Apply("system");ThemeManager.SetSystemTheme(false);Check(ink.Color.R<100,"system light applies without recreating controls");
                ThemeManager.SetSystemTheme(true);Check(ink.Color.R>160,"system dark applies without recreating controls");
                ThemeManager.Apply("light");ThemeManager.SetSystemTheme(true);Check(ink.Color.R<100,"explicit light ignores system dark");
                ThemeManager.Apply("dark");ThemeManager.SetSystemTheme(false);Check(ink.Color.R>160,"explicit dark ignores system light");
                Check(All<Button>(picker).Count()==3,"theme selector exposes three visual choices");
                ThemeManager.Apply("light");Capture(guide,"guide-light.png");
                exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(cfg.OnboardingState=="skipped","emphasized exit retains skip behavior");
                pet.ShowChat();Pump();var chat=app.Windows.OfType<ChatWindow>().Single();ThemeManager.Apply("dark");Pump();
                Check(ThemeManager.ActionSurface.Color.R<120,"white accent text keeps a dark action surface");
                var surface=(SolidColorBrush)ThemeManager.Surface;Check(surface.Color.B>surface.Color.R,"dark surface has a navy tint");
                var editor=All<MarkdownEditor>(chat).Single(e=>e.Name=="MessageInput");
                Check(((SolidColorBrush)editor.CaretBrush).Color==Colors.White,"dark chat caret is white");
                ThemeManager.Apply("light");Check(((SolidColorBrush)editor.CaretBrush).Color.R<100,"existing caret returns to dark in light mode");ThemeManager.Apply("dark");
                Capture(chat,"chat-dark.png");ThemeManager.Apply("light");Pump();Capture(chat,"chat-light.png");
                Console.WriteLine("Theme tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{foreach(Window window in app.Windows.Cast<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
