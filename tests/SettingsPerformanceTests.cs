using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    // Offline fixture: its own settings, no credentials, hotkeys or auto-update.
    // --measure records a baseline without requiring the optimized behavior.
    public static class SettingsPerformanceTests
    {
        static int checks;
        static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
        static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
        static void Render(Window window)
        {
            window.UpdateLayout();var frame=new DispatcherFrame();bool rendered=false;
            EventHandler tick=delegate{if(window.IsVisible&&window.ActualWidth>0){rendered=true;frame.Continue=false;}};
            var timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};timeout.Tick+=delegate{frame.Continue=false;};
            CompositionTarget.Rendering+=tick;timeout.Start();
            try{Dispatcher.PushFrame(frame);}finally{timeout.Stop();CompositionTarget.Rendering-=tick;}
            if(!rendered)throw new TimeoutException("Settings render callback did not arrive.");
        }
        static void Summary(string name,List<double> values)
        {
            var ordered=values.OrderBy(x=>x).ToArray();Console.WriteLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "METRIC {0} n={1} median_ms={2:F3} p95_ms={3:F3} max_ms={4:F3}",name,ordered.Length,ordered[ordered.Length/2],ordered[(int)Math.Ceiling(ordered.Length*.95)-1],ordered[ordered.Length-1]));
        }
        [STAThread] public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;SettingsPage page=null;Window host=null;
            string root=Path.GetFullPath(Path.Combine("work","performance","fixture-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            try
            {
                TextResources.ConfigureStartupLanguage("zh_CN");
                var cfg=new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,SaveChatHistory=false,OnboardingState="completed"};
                foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                if(!args.Contains("--measure"))cfg.Text=new ConnectionSettings{BaseUrl="https://example.com/v1",Model="fixture-model"};
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);
                if(args.Contains("--profile-ai"))
                {
                    var watch=Stopwatch.StartNew();var editor=new ConnectionSettingsPanel(pet);Console.WriteLine("PROFILE ai_panel_constructor_ms="+watch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                    foreach(string field in new[]{"text","recognition","synthesis","live","images"})Console.WriteLine("PROFILE initialized_"+field+"="+(Field(editor,field)!=null));
                    editor.DisposeImageGeneration();return 0;
                }
                if(args.Contains("--measure"))
                {
                    var constructors=new List<double>();var renders=new List<double>();var switches=new List<double>();
                    for(int i=0;i<10;i++)
                    {
                        var watch=Stopwatch.StartNew();page=new SettingsPage(pet);constructors.Add(watch.Elapsed.TotalMilliseconds);
                        host=new Window{Content=page,Width=1030,Height=760};host.Show();Render(host);renders.Add(watch.Elapsed.TotalMilliseconds);
                        if(i==9)for(int j=0;j<30;j++){watch.Restart();page.SelectNavigation(j%2==0?1:0);Render(host);switches.Add(watch.Elapsed.TotalMilliseconds);}
                        host.Close();host=null;page.Dispose();page=null;Pump();
                    }
                    Summary("settings_constructor",constructors);Summary("settings_first_render_callback",renders);Summary("settings_companion_ai_switch_including_first",switches);
                    var process=Process.GetCurrentProcess();Console.WriteLine("METRIC process_private_bytes="+process.PrivateMemorySize64+" working_set_bytes="+process.WorkingSet64);
                    Console.WriteLine("Measurement: same process, 10 fresh settings instances, no forced GC; render callback is a layout/render proxy, not OS presentation or input latency. Memory is an end-of-run sample, not retained heap or total resource savings.");
                    return 0;
                }
                var removal=new TextProviderEditor(cfg);
                typeof(TextProviderEditor).GetMethod("RemoveModel",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(removal,null);
                Check(Field(removal,"modelForm")==null&&removal.ReadProviders().Single().Models.Count==0,"remove model works before model dialog is ever opened");
                page=new SettingsPage(pet);host=new Window{Content=page,Width=1030,Height=760};host.Show();Render(host);
                foreach(string field in new[]{"connections","aiConfiguration","work","quiet","about","imChannels"})Check(Field(page,field)==null,"unvisited settings do not construct "+field);
                Check(!page.HasUnsavedEdits&&!page.HasUnsavedWorkEdits,"dirty queries do not instantiate settings pages");
                page.RefreshTextProviders();page.RefreshState();page.CanChangeWorkSettingsProject(null);
                foreach(string field in new[]{"connections","work","quiet","about","imChannels"})Check(Field(page,field)==null,"background refresh does not construct "+field);
                page.SelectAiSection(0);Render(host);var ai=Field(page,"connections");
                Check(Field(Field(ai,"text"),"modelForm")==null,"unopened model dialog allocates no form controls");
                foreach(string field in new[]{"recognition","synthesis","live","images"})Check(Field(ai,field)==null,"text AI route does not create "+field);
                cfg.Transcription.Model="external-asr-change";
                Check(((ConnectionSettingsPanel)ai).Save()&&cfg.Transcription.Model=="external-asr-change","saving visited AI editor preserves unopened service settings");
                foreach(string field in new[]{"recognition","synthesis","live","images"})Check(Field(ai,field)==null,"AI save does not create "+field);
                var provider=(TextBox)page.FindControl("TextProviderName");provider.Text="local unsaved provider";
                page.SelectNavigation(0);page.RefreshTextProviders();page.SelectAiSection(0);Render(host);
                Check(ReferenceEquals(ai,Field(page,"connections"))&&provider.Text=="local unsaved provider","AI page is reused and provider draft survives refresh");
                var aiHost=(ContentControl)Field(ai,"page");var textScroll=(ScrollViewer)aiHost.Content;
                page.SelectAiSection(3);Render(host);page.SelectAiSection(0);Render(host);
                Check(ReferenceEquals(textScroll,aiHost.Content),"AI tab navigation reuses its scroll container");
                Check(Field(ai,"recognition")==null&&Field(ai,"synthesis")==null&&Field(ai,"images")==null,"direct Live route leaves other editors unopened");
                page.SelectAiSection(1);Render(host);var asrDrafts=(System.Collections.Generic.List<TextProviderSettings>)Field(Field(ai,"recognition"),"drafts");
                var invalidProvider=new TextProviderSettings{Name="invalid hidden service",Connection=new ConnectionSettings{BaseUrl="file:///not-an-api",Model="fixture"}};invalidProvider.Models.Add("fixture");asrDrafts.Add(invalidProvider);
                string savedName=cfg.TextProviders[0].Name;
                Check(!((ConnectionSettingsPanel)ai).Save()&&cfg.TextProviders[0].Name==savedName,"invalid inactive provider cannot partially apply text drafts");asrDrafts.Remove(invalidProvider);
                page.SelectWorkSettings();Render(host);var work=Field(page,"work");var goal=(TextBox)page.FindControl("AgentGoal");string previousGoal=goal.Text;
                page.SelectNavigation(0);cfg.AgentGoal="externally updated goal";page.RefreshState();
                Check(goal.Text==previousGoal,"hidden work controls are not refreshed by unrelated state updates");
                page.SelectWorkSettings();Render(host);Check(goal.Text==cfg.AgentGoal,"activating work catches up with committed settings");goal.Text="local unsaved work";
                page.SelectNavigation(0);page.RefreshState();page.SelectWorkSettings();Render(host);
                Check(ReferenceEquals(work,Field(page,"work"))&&goal.Text=="local unsaved work","work page is reused and unsaved edits survive activation");
                page.SelectGeneralSection(2);Render(host);Check(page.FindControl("ClearAllHistory")!=null,"direct general deep link materializes its page");
                page.SelectNavigation(5);Render(host);var channels=Field(page,"imChannels");var channelName=(TextBox)page.FindControl("ImChannelName");channelName.Text="local unsaved channel";
                page.SelectNavigation(0);page.SelectNavigation(5);Render(host);
                Check(ReferenceEquals(channels,Field(page,"imChannels"))&&channelName.Text=="local unsaved channel","channel draft and controls survive navigation");
                page.SelectNavigation(4);Render(host);Check(Field(page,"about")!=null,"about page materializes on demand");
                page.Dispose();page.Dispose();Check(true,"disposing all visited pages is idempotent");
                using(var direct=new SettingsPage(pet)){direct.SelectAiSection(4);var editor=Field(direct,"connections");Check(Field(editor,"images")!=null&&Field(editor,"text")==null,"direct image route does not construct text editor first");}
                using(var direct=new SettingsPage(pet)){direct.SelectAiSection(3);var editor=Field(direct,"connections");Check(Field(editor,"live")!=null&&Field(editor,"text")==null&&Field(editor,"synthesis")==null,"direct Live route only constructs Live editor");}
                Console.WriteLine("Settings performance lifecycle: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL "+error);return 1;}
            finally{if(host!=null)host.Close();if(page!=null)page.Dispose();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
