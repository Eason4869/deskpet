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

internal static class LanguageUiChecks
{
    internal static int RunSystemStartup(string directory,string culture)
    {
        string expected=culture switch {"zh-TW"=>"zh_TW","en-GB" or "ar-SA"=>"en_US",_=>throw new ArgumentException("Unknown system-language UI fixture")};
        int checks=0;void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"AutoUpdate\":false,\"GreetingEnabled\":false,\"ReadReplies\":false,\"OnboardingState\":\"completed\"}");
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory));
        Check(owner.Settings.UiLanguage=="system"&&TextResources.CurrentLanguage==expected,"legacy settings follow system UI culture before controller UI creation");
        var chat=new ChatWindow(owner,false);chat.Show();Dispatcher.UIThread.RunJobs();
        var avatar=chat.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="ShellAvatar");avatar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu=avatar.ContextMenu!;Check(menu.ItemsSource!.OfType<MenuItem>().Single(item=>item.Name=="ProfileLanguage").Header as string==(expected=="zh_TW"?"語言":"Language"),"actual avatar menu uses the resolved system language");menu.Close();
        owner.SetUiLanguage("ja_JP");Check(owner.Settings.UiLanguage=="ja_JP"&&TextResources.CurrentLanguage==expected&&owner.Session.LastNotice==TextResources.Get("languages.restartRequired"),"manual preference persists while the process language stays frozen");
        owner.SetUiLanguage("system");Check(owner.Settings.UiLanguage=="system"&&new SettingsStore(Path.Combine(directory,"settings.json")).Load().UiLanguage=="system"&&String.IsNullOrEmpty(owner.Session.LastNotice),"returning to the active system language clears an unnecessary restart notice");
        chat.Close();owner.Pet.Close();owner.Dispose();var watch=System.Diagnostics.Stopwatch.StartNew();while(!owner.ImageServicesShutdown.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("System language fixture cleanup timed out");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}owner.ImageServicesShutdown.GetAwaiter().GetResult();return checks;
    }
    internal static int Run(string directory,string code)
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("Language UI: "+label);checks++;}
        void Until(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Language UI did not arrange");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        string artifacts=Path.GetFullPath(Path.Combine("work","language-ui","avalonia",code));Directory.CreateDirectory(artifacts);
        Directory.CreateDirectory(directory);var runtime=new TestRuntime(directory);runtime.InitializeProtection();
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=code,AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,ReadReplies=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),runtime);
        Check(TextResources.CurrentLanguage==code,"startup uses selected catalog "+code);
        var names=new[]{"简体中文","繁體中文","English","日本語","한국어"};
        foreach(string mode in new[]{"light","dark"})
        {
            DesktopTheme.Apply(mode);var chat=new ChatWindow(owner,false);chat.Show();chat.Width=390;Dispatcher.UIThread.RunJobs();
            var avatar=chat.GetLogicalDescendants().OfType<Button>().Single(item=>item.Name=="ShellAvatar");
            avatar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var menu=avatar.ContextMenu!;
            var items=menu.ItemsSource!.Cast<MenuItem>().ToArray();var language=items.Single(item=>item.Name=="ProfileLanguage");
            Check(Array.IndexOf(items,language)+1==Array.IndexOf(items,items.Single(item=>item.Name=="ProfileSettings")),mode+" language is immediately above settings");
            Check(language.ItemsSource==null,mode+" language entry opens a separate scrolling selector");
            void Select(string previous,string selected)
            {
                language.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Until(()=>chat.OwnedWindows.OfType<LanguagePickerWindow>().Any(window=>window.IsVisible));
                var dialog=chat.OwnedWindows.OfType<LanguagePickerWindow>().Single(window=>window.IsVisible);var list=dialog.GetLogicalDescendants().OfType<ListBox>().Single();var choices=list.ItemsSource!.Cast<ListBoxItem>().ToArray();
                Check(choices.Select(item=>item.Tag as string).SequenceEqual(UiLanguages.Choices.Select(choice=>choice.Code))&&((ListBoxItem)list.SelectedItem!).Tag as string==previous,mode+" system and native language choices preserve saved selection");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var image=dialog.CaptureRenderedFrame()){Check(image!=null,mode+" language dialog rendered");image!.Save(Path.Combine(artifacts,"dialog-"+mode+".png"),new PngBitmapEncoderOptions());}
                list.SelectedItem=choices.Single(item=>item.Tag as string==selected);dialog.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="SaveLanguageSelection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            }
            string pending=code=="en_US"?"ja_JP":"en_US";Select(code,pending);
            Check(owner.Settings.UiLanguage==pending&&new SettingsStore(Path.Combine(directory,"settings.json")).Load().UiLanguage==pending&&TextResources.CurrentLanguage==code,mode+" selection persists without changing the running window language");
            Check(owner.Session.LastNotice==TextResources.Get("languages.restartRequired"),mode+" selection explains the restart requirement");Select(pending,code);menu.Close();chat.Close();
            var settings=new SettingsWindow(owner);settings.Show();Dispatcher.UIThread.RunJobs();
            Check(settings.GetLogicalDescendants().OfType<TabItem>().Any(item=>item.Header as string==TextResources.Get("settingsWindow.settingsWindow.text.alternativeE")),mode+" settings use the selected language");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var image=settings.CaptureRenderedFrame()){Check(image!=null,mode+" settings rendered");image!.Save(Path.Combine(artifacts,"settings-"+mode+".png"),new PngBitmapEncoderOptions());}settings.Close();
        }
        owner.Pet.Close();owner.Dispose();Until(()=>owner.ImageServicesShutdown.IsCompleted);owner.ImageServicesShutdown.GetAwaiter().GetResult();return checks;
    }
}
