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
        static void Pump(int milliseconds=80){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static Color Painted(Brush brush)
        {
            var visual=new DrawingVisual();using(var context=visual.RenderOpen()){context.DrawRectangle(ThemeManager.Surface,null,new Rect(0,0,24,24));context.DrawRectangle(brush,null,new Rect(0,0,24,24));}
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(24,24,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(12,12,1,1),pixel,4,0);return Color.FromRgb(pixel[2],pixel[1],pixel[0]);
        }
        static double Light(Color color){Func<byte,double> linear=c=>{double v=c/255.0;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);};return .2126*linear(color.R)+.7152*linear(color.G)+.0722*linear(color.B);}
        static double Contrast(Color a,Color b){double x=Light(a),y=Light(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void CheckLiquidGeometry()
        {
            foreach (var size in new[] { new Size(420, 490), new Size(1280, 820) })
            {
                var origin = new Point(size.Width - 160, 40);
                var shape = new LiquidThemeShape(size.Width, size.Height, origin.X, origin.Y);
                var stream = Geometry.Parse(shape.PathAt(.14));
                Check(stream.FillContains(new Point(origin.X, origin.Y + 2)) &&
                    !stream.FillContains(new Point(5, size.Height - 4)), size + " liquid starts at the button");
                var rising = Geometry.Parse(shape.PathAt(.62));
                Check(rising.FillContains(new Point(size.Width / 2, size.Height - 4)) &&
                    !rising.FillContains(new Point(size.Width / 2, 5)), size + " liquid fills from the bottom");
                foreach(double progress in new[]{.70,.84,.90})
                {
                    var pouring=Geometry.Parse(shape.PathAt(progress));
                    bool connected=true;
                    for(double y=origin.Y+4;y<size.Height;y+=8)
                    {
                        bool wet=false;
                        for(int dx=-18;dx<=18&&!wet;dx++)
                            wet=pouring.FillContains(new Point(origin.X+dx,y));
                        connected&=wet;
                    }
                    Check(connected,size+" the falling stream connects to the rising surface at "+progress);
                }
                var full = Geometry.Parse(shape.PathAt(1));
                Check(full.FillContains(new Point(0, 0)) && full.FillContains(new Point(size.Width - 1, size.Height - 1)),
                    size + " liquid covers the viewport");
            }
        }
        static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static bool HasOverlay(Window window){return All<FrameworkElement>(window).Any(e=>e.Name=="LiquidThemeOverlay");}
        static void PumpReveal(string effect=ThemeTransitionMotion.Flow)
        {Pump((int)ThemeTransitionMotion.Duration(effect,LiquidThemeShape.DefaultDuration).TotalMilliseconds+210);}
        static void CompleteReveal(Window window)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            while (HasOverlay(window) && started.ElapsedMilliseconds < 7500) Pump(65);
            if (HasOverlay(window)) throw new TimeoutException("Theme overlay did not finish for " + ThemeManager.Mode);
        }
        static void Preview(ChatWindow chat,string effect,bool manualOnly,bool settingsPicker)
        {
            chat.Title="DeskPet Liquid Theme Preview";
            chat.WindowState=WindowState.Normal;
            chat.Width=1120;
            chat.Height=740;
            chat.Left=50;
            chat.Top=42;
            chat.Activate();
            if (settingsPicker) All<SettingsPage>(chat).Single().SelectNavigation(3);
            chat.UpdateLayout();
            Console.WriteLine("PREVIEW_WINDOW="+new System.Windows.Interop.WindowInteropHelper(chat).Handle.ToInt64());
            Pump(920);
            if (settingsPicker)
            {
                var picker = All<ThemeModePicker>(chat).Single();
                Click(picker.Origin("dark"));
                PumpReveal(effect);
                Click(picker.Origin("light"));
                PumpReveal(effect);
                Pump(850);
                return;
            }
            var dark=All<Button>(chat).Single(b=>b.Name=="AppThemeDark");
            var light=All<Button>(chat).Single(b=>b.Name=="AppThemeLight");
            var system=All<Button>(chat).Single(b=>b.Name=="AppThemeSystem");
            Click(dark);
            PumpReveal(effect);
            Click(light);
            PumpReveal(effect);
            if(manualOnly){Pump(850);return;}
            Click(system);
            PumpReveal(effect);
            bool systemDark=ThemeManager.IsDark;
            ThemeManager.SetSystemTheme(!systemDark);
            PumpReveal(effect);
            ThemeManager.SetSystemTheme(systemDark);
            PumpReveal(effect);
            Pump(900);
        }
        [STAThread]public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                if(args.Contains("--preview"))
                {
                    var previewRoot=Path.Combine("work","theme-preview-"+Guid.NewGuid().ToString("N"));
                    var preview=new PetSettings{ThemeMode="light",UiLanguage="zh_CN",AutoUpdate=false,GreetingEnabled=false,
                        ReadReplies=false,AgentEnabled=true,OnboardingState="completed",ShowPredictedMessages=false};
                    var effectArg=args.FirstOrDefault(arg=>arg.StartsWith("--effect=",StringComparison.Ordinal));
                    if(effectArg!=null)preview.ThemeTransitionEffect=effectArg.Substring("--effect=".Length);
                    foreach(var key in preview.Hotkeys.Keys.ToArray())preview.Hotkeys[key]="";
                    pet=new PetWindow(null,preview,new SettingsStore(Path.Combine(previewRoot,"settings.json")),null);
                    bool settingsPicker=args.Contains("--settings-picker");
                    if (settingsPicker) pet.ShowSettings(); else pet.ShowChat();
                    Preview(app.Windows.OfType<ChatWindow>().Single(window=>window.IsVisible),preview.ThemeTransitionEffect,args.Contains("--manual-only"),settingsPicker);
                    return 0;
                }
                CheckLiquidGeometry();
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
                var motion=All<ComboBox>(main).SingleOrDefault(b=>b.Name=="ThemeTransitionEffect");
                Check(motion!=null&&Equals(motion.SelectedValue,ThemeTransitionMotion.RandomSelection),"visual style shows Random as the default theme animation");
                Check(motion.Items.Cast<ComboBoxItem>().Select(item=>(string)item.Tag).SequenceEqual(ThemeTransitionMotion.Choices),
                    "the theme dropdown contains the four effects, Off and Random in order");
                Check(Enumerable.Range(0,32).All(_=>ThemeTransitionMotion.AnimatedEffects.Contains(ThemeTransitionMotion.Resolve(ThemeTransitionMotion.RandomSelection))),
                    "Random resolves only to an animated preset");
                var themeSound=All<CheckBox>(main).Single(b=>b.Name=="ThemeTransitionSoundEnabled");
                var themeVolume=All<Slider>(main).Single(s=>s.Name=="ThemeTransitionSoundVolume");
                Check(themeSound.IsChecked==false&&themeVolume.Value==30,"appearance settings expose opt-in sound and 30 percent volume");
                themeSound.IsChecked=true;themeVolume.Value=41;
                Check(store.Load().ThemeTransitionSoundEnabled&&store.Load().ThemeTransitionSoundVolume==41,"appearance controls persist both theme audio preferences");
                var titleSwitch=All<ThemeTitleSwitch>(main).Single();
                var titleLight=All<Button>(titleSwitch).Single(b=>b.Name=="AppThemeLight");
                var titleDark=All<Button>(titleSwitch).Single(b=>b.Name=="AppThemeDark");
                var titleSystem=titleSwitch.SystemButton;
                var thumb=All<FrameworkElement>(titleSwitch).Single(e=>e.Name=="AppThemeThumb");
                Check(titleLight.Focusable&&titleDark.Focusable&&titleSystem.Focusable,"main title bar provides direct keyboard-accessible theme choices");
                var behavior=All<Border>(main).Single(b=>b.Child is Panel&&((Panel)b.Child).Children.OfType<CheckBox>().Any(c=>c.Name=="QuietMode"));
                Check(Contrast(Painted(behavior.Background),Painted(ThemeManager.Surface))>=1.18,"dark behavior card visibly separates from the settings page");
                Check(Contrast(Painted(behavior.BorderBrush),Painted(behavior.Background))>=1.5,"dark behavior card has a visible perimeter");
                var off=All<CheckBox>(behavior).First(c=>c.IsChecked==false);var offTrack=(Border)off.Template.FindName("track",off);
                Check(Contrast(Painted(offTrack.Background),Painted(behavior.Background))>=1.7,"dark off-state toggle track visibly separates from its behavior card");
                Check(Contrast(Painted(((Border)picker.Content).Background),Painted(ThemeManager.Surface))>=1.18,"dark mode picker surface visibly separates from the page");
                var generic=Ui.Card(Ui.Field());Check(Contrast(Painted(generic.Background),Painted(((TextBox)generic.Child).Background))>=1.2,"dark input fields separate from their containing cards");
                var agentCard=AgentUi.Card(Ui.Text("工作卡片"));Check(Contrast(Painted(agentCard.Background),Painted(ThemeManager.Surface))>=1.18,"shared work cards visibly separate from the dark page");
                Check(All<CheckBox>(main).Where(c=>c.IsVisible).All(c=>c.Foreground==Ui.Ink),"general switches use theme text instead of default black");Capture(main,"settings-dark.png");
                var lightChoice=picker.Origin("light");var darkChoice=picker.Origin("dark");
                Click(lightChoice);
                Check(ink.Color.R<100&&HasOverlay(main)==SystemParameters.ClientAreaAnimation,"settings choice reveals the light palette from its preview card");
                if(SystemParameters.ClientAreaAnimation)Check(store.Load().ThemeMode=="dark","settings selection keeps the stored mode while its snapshot is active");
                CompleteReveal(main);Capture(main,"settings-light.png");
                Click(darkChoice);
                Check(ink.Color.R>160&&HasOverlay(main)==SystemParameters.ClientAreaAnimation,"settings choice also reveals the dark palette");
                CompleteReveal(main);
                Check(store.Load().ThemeMode=="dark"&&!HasOverlay(main),"settings selection persists the resulting mode and releases its snapshot");
                Click(lightChoice);CompleteReveal(main);
                var slide=(TranslateTransform)thumb.RenderTransform;
                double lightPosition=slide.X;
                Click(titleDark);
                if(SystemParameters.ClientAreaAnimation)
                {
                    Check(HasOverlay(main)&&store.Load().ThemeMode=="light","transition retains the previous stored mode while the reveal is active");
                    var overlay=All<FrameworkElement>(main).Single(e=>e.Name=="LiquidThemeOverlay");
                    var capsule=titleSwitch.Capsule;
                    var switchCenter=capsule.TranslatePoint(new Point(capsule.ActualWidth/2,capsule.ActualHeight/2),overlay);
                    var switchCorner=capsule.TranslatePoint(new Point(1,1),overlay);
                    var monitorCenter=titleSystem.TranslatePoint(new Point(titleSystem.ActualWidth/2,titleSystem.ActualHeight/2),overlay);
                    Check(overlay.Clip!=null&&!overlay.Clip.FillContains(switchCenter),"the snapshot leaves the live title thumb visible");
                    Check(overlay.Clip.FillContains(switchCorner)&&overlay.Clip.FillContains(monitorCenter),"the snapshot covers the title background outside the rounded capsule");
                    Check(DependencyPropertyHelper.GetValueSource(slide,TranslateTransform.XProperty).IsAnimated,"the title thumb slides during the reveal");
                    Click(titleDark);
                    Check(ThemeManager.Mode=="dark","selecting the same side coalesces into the current reveal");
                    CompleteReveal(main);
                }
                Check(!HasOverlay(main)&&store.Load().ThemeMode=="dark"&&picker.Value=="dark"&&slide.X>lightPosition,"dark side saves the mode and rests with the thumb on the right");
                Click(titleLight);CompleteReveal(main);
                Check(store.Load().ThemeMode=="light"&&slide.X==lightPosition,"light side switches directly from dark and returns the thumb to the left");
                Click(titleSystem);CompleteReveal(main);
                Check(store.Load().ThemeMode=="system"&&picker.Value=="system","the separate monitor selects follow-system mode");
                foreach(bool systemDark in new[]{!ThemeManager.IsDark,ThemeManager.IsDark})
                {
                    ThemeManager.SetSystemTheme(systemDark);
                    Check(ThemeManager.IsDark==systemDark&&HasOverlay(main)==SystemParameters.ClientAreaAnimation,
                        "changing system appearance plays the same reveal when motion is allowed");
                    CompleteReveal(main);
                    Check(!HasOverlay(main)&&store.Load().ThemeMode=="system","system reveal clears without changing the saved preference");
                }
                if(SystemParameters.ClientAreaAnimation)
                {
                    bool initial=ThemeManager.IsDark;
                    ThemeManager.SetSystemTheme(!initial);Pump(240);
                    var before=All<FrameworkElement>(main).Single(e=>e.Name=="LiquidThemeOverlay");
                    ThemeManager.SetSystemTheme(initial);
                    Check(All<FrameworkElement>(main).Contains(before),"another system appearance change reverses the active mask");
                    CompleteReveal(main);
                    Check(ThemeManager.IsDark==initial&&store.Load().ThemeMode=="system","automatic reversal preserves Follow System");
                }
                Click(titleLight);CompleteReveal(main);
                Check(store.Load().ThemeMode=="light"&&picker.Value=="light","the sun leaves follow-system mode directly");
                Click(titleDark);
                main.Width-=8;main.UpdateLayout();Pump();
                Check(HasOverlay(main)==SystemParameters.ClientAreaAnimation,"resizing keeps the theme animation active");
                CompleteReveal(main);
                Check(store.Load().ThemeMode=="dark","resizing leaves the requested theme committed at completion");
                motion.SelectedValue=ThemeTransitionMotion.Off;
                Check(store.Load().ThemeTransitionEffect=="off","changing the motion dropdown immediately persists Off");
                Click(lightChoice);Pump();
                Click(titleDark);
                Check(!HasOverlay(main)&&store.Load().ThemeMode=="dark"&&store.Load().ThemeTransitionEffect=="off",
                    "Off takes effect immediately and survives reload");
                Click(titleSystem);
                ThemeManager.SetSystemTheme(!ThemeManager.IsDark);
                Check(!HasOverlay(main)&&store.Load().ThemeMode=="system","Off also suppresses automatic system animation");
                foreach(string effect in ThemeTransitionMotion.AnimatedEffects)
                {
                    motion.SelectedValue=effect;
                    pet.SetThemeMode("light");Pump();
                    Click(titleDark);
                    Check(HasOverlay(main)==SystemParameters.ClientAreaAnimation,effect+" uses the native reveal when motion is allowed");
                    CompleteReveal(main);
                    Check(!HasOverlay(main)&&store.Load().ThemeTransitionEffect==effect&&store.Load().ThemeMode=="dark",
                        effect+" selection and completed palette survive reload ("+store.Load().ThemeMode+", "+store.Load().ThemeTransitionEffect+")");
                    Click(titleLight);Pump(320);
                    var originalOverlay=All<FrameworkElement>(main).SingleOrDefault(e=>e.Name=="LiquidThemeOverlay");
                    Click(titleDark);
                    if(SystemParameters.ClientAreaAnimation)
                        Check(originalOverlay!=null&&All<FrameworkElement>(main).Contains(originalOverlay),
                            effect+" keeps the rendered mask when reversing");
                    CompleteReveal(main);
                    Check(store.Load().ThemeMode=="dark",effect+" saves the last requested theme");
                }
                motion.SelectedValue=ThemeTransitionMotion.RandomSelection;
                pet.SetThemeMode("light");
                Click(titleDark);
                Check(HasOverlay(main)==SystemParameters.ClientAreaAnimation,"Random selects a reveal for an actual palette change");
                CompleteReveal(main);
                Check(store.Load().ThemeMode=="dark"&&store.Load().ThemeTransitionEffect==ThemeTransitionMotion.RandomSelection,
                    "Random remains selected after an animated switch");
                var settingsPage=All<SettingsPage>(main).Single();settingsPage.SelectNavigation(4);Pump();var about=All<AboutPanel>(main).Single();
                var updateCard=All<Border>(about).Single(b=>b.Background is LinearGradientBrush&&All<Button>(b).Any(c=>c.Name=="CheckUpdates"));
                Check(Contrast(Painted(updateCard.Background),Painted(ThemeManager.Surface))>=1.18,"dark about update card visibly separates from its page");
                Check(Contrast(Painted(updateCard.BorderBrush),Painted(updateCard.Background))>=1.5,"dark about update card keeps a visible perimeter");Capture(main,"about-dark.png");
                ThemeManager.Apply("light");Pump();Capture(main,"about-light.png");ThemeManager.Apply("dark");Pump();Check(Contrast(Painted(updateCard.Background),Painted(ThemeManager.Surface))>=1.18,"existing about card restores dark hierarchy after a light-dark switch");settingsPage.SelectNavigation(2);Pump();var work=All<WorkSettingsPanel>(main).Single();var projectCard=All<Border>(work).First(b=>b.BorderThickness.Left==1&&All<TextBox>(b).Any(t=>t.Name=="AgentWorkspace"));Check(Contrast(Painted(projectCard.Background),Painted(ThemeManager.Surface))>=1.18,"actual work settings groups visibly separate from the dark page");Capture(main,"work-settings-dark.png");settingsPage.SelectNavigation(3);Pump();
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
