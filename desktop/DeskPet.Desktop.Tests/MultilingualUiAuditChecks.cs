using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Desktop;

internal static class MultilingualUiAuditChecks
{
    // Call once per process, before another fixture constructs localized static UI types.
    internal static int Run(string directory,string code,string? captureRoot=null)
    {
        if(!UiLanguages.Choices.Any(c=>c.Code==code))throw new ArgumentException("Unsupported audit locale",nameof(code));
        string output=Path.GetFullPath(captureRoot??Path.Combine("work","language-ui","full","avalonia",code));Directory.CreateDirectory(output);
        int checks=0,captures=0;var manifest=new List<string>{"capture\tpixels\tvisible_text_records"};
        void Check(bool value,string reason){if(!value)throw new Exception("Multilingual Avalonia audit: "+reason);checks++;}
        void Until(Func<bool> ready){var timer=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(timer.Elapsed.TotalSeconds>10)throw new TimeoutException("Audit UI did not settle");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}
        void Settle(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
        string Plain(string? text)=>(text??"").Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");
        IEnumerable<Control> Controls(Control root)=>new[]{root}.Concat(root.GetVisualDescendants().OfType<Control>());
        bool VisibleIn(Control control,Control root,out Rect bounds)
        {
            bounds=default;if(!control.IsEffectivelyVisible||control.Bounds.Width<=0||control.Bounds.Height<=0)return false;
            var point=control.TranslatePoint(default,root);if(point==null)return false;
            bounds=new Rect(point.Value,control.Bounds.Size);if(!bounds.Intersects(new Rect(root.Bounds.Size)))return false;
            foreach(var parent in control.GetVisualAncestors().TakeWhile(parent=>!ReferenceEquals(parent,root)).OfType<Control>())
            {
                if(!parent.ClipToBounds)continue;var origin=parent.TranslatePoint(default,root);
                if(origin!=null&&!new Rect(origin.Value,parent.Bounds.Size).Intersects(bounds))return false;
            }
            return true;
        }
        void Capture(TopLevel root,string name)
        {
            Settle();Check(root.IsVisible&&root.Bounds.Width>40&&root.Bounds.Height>30,"visible arranged capture "+name);
            Check(root.Bounds.Width<=4096&&root.Bounds.Height<=4096,"bounded capture "+name);
            // Headless rendering captures this top-level control, never the system desktop.
            using(var frame=root.CaptureRenderedFrame())
            {
                Check(frame!=null&&frame.PixelSize.Width>40&&frame.PixelSize.Height>30,"rendered pixels "+name);
                frame!.Save(Path.Combine(output,name+".png"),new PngBitmapEncoderOptions());
            }
            var lines=new List<string>{"type\tname\tbounds\thasHan\ttext"};
            if(root is Window window)lines.Add("WindowTitle\t\t\t"+Regex.IsMatch(window.Title??"",@"[\u3400-\u9fff]")+"\t"+Plain(window.Title));
            foreach(var control in Controls(root))
            {
                if(!VisibleIn(control,root,out var bounds))continue;
                string? text=control switch { TextBlock block=>block.Text,ContentControl content when content.Content is string value=>value,HeaderedItemsControl header when header.Header is string value=>value,_=>null };
                if(!string.IsNullOrWhiteSpace(text))
                {
                    Check(!Regex.IsMatch(text.Trim(),@"^\[[a-zA-Z][a-zA-Z0-9]*\.[a-zA-Z0-9_.]+\]$"),"no missing resource marker "+name+" "+text);
                    lines.Add(control.GetType().Name+"\t"+control.Name+"\t"+bounds.ToString()+"\t"+Regex.IsMatch(text,@"[\u3400-\u9fff]")+"\t"+Plain(text));
                }
                if(control is TextBox input&&!string.IsNullOrWhiteSpace(input.PlaceholderText))lines.Add("PlaceholderText\t"+control.Name+"\t\t\t"+Plain(input.PlaceholderText));
                var tip=ToolTip.GetTip(control);if(tip is string tipText)lines.Add("ToolTip\t"+control.Name+"\t\t\t"+Plain(tipText));
                string? accessible=Avalonia.Automation.AutomationProperties.GetName(control);if(!string.IsNullOrWhiteSpace(accessible))lines.Add("AutomationName\t"+control.Name+"\t\t\t"+Plain(accessible));
            }
            Check(lines.Count>1,"visible text exists "+name);File.WriteAllLines(Path.Combine(output,name+".txt"),lines,Encoding.UTF8);
            manifest.Add(name+"\t"+root.Bounds.Width.ToString(CultureInfo.InvariantCulture)+"x"+root.Bounds.Height.ToString(CultureInfo.InvariantCulture)+"\t"+(lines.Count-1));captures++;
        }
        void ScrollCaptures(Window host,string name)
        {
            Settle();var scrolls=Controls(host).OfType<ScrollViewer>().Where(v=>v.IsEffectivelyVisible).ToArray();
            foreach(var scroll in scrolls)scroll.Offset=new Vector(scroll.Offset.X,0);Settle();Capture(host,name+"-top");
            foreach(var scroll in scrolls)scroll.Offset=new Vector(scroll.Offset.X,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height));Settle();Capture(host,name+"-bottom");
        }
        void Inside(Control root,string name)
        {
            var control=Controls(root).Single(c=>c.Name==name);
            Check(VisibleIn(control,root,out var bounds)&&bounds.Left>=-1&&bounds.Top>=-1&&bounds.Right<=root.Bounds.Width+1&&bounds.Bottom<=root.Bounds.Height+1,"control within capture "+name);
        }
        void Show(Window window,string name){window.Show();try{Settle();ScrollCaptures(window,name);}finally{window.Close();Settle();}}
        string fixture=Path.Combine(directory,"full-audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        var runtime=new TestRuntime(fixture);runtime.InitializeProtection();
        var settingsStore=new SettingsStore(Path.Combine(fixture,"settings.json"));
        var config=new PetSettings{UiLanguage=code,AutoUpdate=false,GreetingEnabled=false,AiGreetingEnabled=false,ReadReplies=false,OnboardingState="completed",Wander=false,FollowCursor=false,ShowPredictedMessages=false,AgentWorkspace=Path.Combine(fixture,"workspace")};
        Directory.CreateDirectory(config.AgentWorkspace);foreach(string key in config.Hotkeys.Keys.ToArray())config.Hotkeys[key]="";settingsStore.Save(config);
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),runtime);
        Check(TextResources.CurrentLanguage==code,"saved startup locale");
        var chat=new ChatWindow(owner,false);chat.Show();
        try
        {
            foreach(string mode in new[]{"light","dark"})
            {
                owner.Session.Clear();
                DesktopTheme.Apply(mode);chat.Height=780;
                foreach(int width in new[]{1000,430}){chat.Width=width;Settle();Inside(chat,"ShellAvatar");Inside(chat,"ChatDictation");Capture(chat,"chat-"+mode+"-"+width);}
                var avatar=chat.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="ShellAvatar");avatar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Settle();var menu=avatar.ContextMenu!;
                var language=menu.ItemsSource!.OfType<MenuItem>().Single(m=>m.Name=="ProfileLanguage");
                var menuRoot=TopLevel.GetTopLevel(language);Check(menuRoot!=null,"profile popup rendered");Capture(menuRoot!,"avatar-"+mode);
                language.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Until(()=>chat.OwnedWindows.OfType<LanguagePickerWindow>().Any(window=>window.IsVisible));
                var picker=chat.OwnedWindows.OfType<LanguagePickerWindow>().Single(window=>window.IsVisible);var choices=picker.GetLogicalDescendants().OfType<ListBox>().Single();
                Check(choices.ItemsSource!.Cast<ListBoxItem>().Select(item=>item.Tag as string).SequenceEqual(UiLanguages.Choices.Select(choice=>choice.Code))&&((ListBoxItem)choices.SelectedItem!).Tag as string==code,"system and supported native language options retain selected locale");
                ScrollCaptures(picker,"language-dialog-"+mode);picker.Close();menu.Close();
                var settings=new SettingsWindow(owner);settings.Show();
                try
                {
                    foreach(int width in new[]{1000,570})
                    {
                        settings.Width=width;settings.Height=760;
                        foreach(string section in new[]{"partner","behavior","text","image","asr","speech","live","work","voice","storage"})
                        {
                            settings.SelectSection(section);Settle();Check(settings.SelectedSection==section,"selected section "+section);
                            ScrollCaptures(settings,"settings-"+section+"-"+mode+"-"+width);
                        }
                    }
                }
                finally{settings.Close();Settle();}
                chat.Width=1000;chat.ShowImages();Settle();
                foreach(int width in new[]{1000,430})
                {
                    chat.Width=width;
                    for(int section=0;section<3;section++){chat.Images.SelectedIndex=section;ScrollCaptures(chat,"images-"+section+"-"+mode+"-"+width);}
                }
                chat.NavigateHistory(-1);chat.Width=1000;owner.Session.AddLiveTurn(owner.Skin.Id,"UI fixture question","UI fixture reply");Settle();Capture(chat,"chat-history-"+mode);
                Show(new ChatWindow(owner,true),"quick-chat-"+mode);
                Show(new AboutWindow(owner),"about-"+mode);
                Show(new LiveWindow(owner),"live-idle-"+mode);
                // Saved step selection changes only guide text; OpenStep/Start/test buttons are never invoked.
                for(int step=0;step<17;step++)
                {
                    owner.Settings.OnboardingState="deferred";owner.Settings.OnboardingStep=step;
                    Show(new GuideWindow(owner),"guide-"+step.ToString("D2",CultureInfo.InvariantCulture)+"-"+mode);
                }
                owner.Settings.OnboardingState="completed";
            }
            Check(runtime.RecorderCreations==0,"no audio recorder created");Check(!owner.Live.IsActive&&!owner.Session.IsBusy,"no live or chat API operation started");
            File.WriteAllLines(Path.Combine(output,"manifest.tsv"),manifest,Encoding.UTF8);
            File.WriteAllLines(Path.Combine(output,"exclusions.txt"),new[]{
                "Control-only headless top-level rendering. One startup locale per process; no screen/desktop capture.",
                "No microphone, playback, network, downloads or destructive actions are activated by this fixture.",
                "Captured settings: all ten SelectSection destinations in light/dark at normal/minimum width, scroll top and bottom; all 17 guide text steps.",
                "Excluded: native tray/file/folder/OS permission dialogs, WebView/browser, update/download/install/uninstall, API and recording/live-connected states.",
                "Image pages cover empty generation/gallery/task states; populated images, reference editor, queue/error states and image dialogs require separate injected fixtures.",
                "Platform-specific project/usage/plugin/approval surfaces not exposed by this Avalonia shell are covered separately by WPF where applicable.",
                "hasHan is a review hint only: Han text can be valid Japanese, native language names, brands or fixture/user content. Human visual/text review is required for translation quality and truncation."
            },Encoding.UTF8);
            Console.WriteLine("Multilingual Avalonia audit: "+code+", "+captures+" captures; "+output);return checks;
        }
        finally
        {
            foreach(var window in new[]{chat}.Concat(owner.Pet.OwnedWindows).ToArray())window.Close();owner.Pet.Close();owner.Dispose();Until(()=>owner.ImageServicesShutdown.IsCompleted);owner.ImageServicesShutdown.GetAwaiter().GetResult();
        }
    }
}
