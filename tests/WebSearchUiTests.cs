using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu.Tests
{
    public static class WebSearchUiTests
    {
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;
            try{
                SecretFieldChecks.Run();
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,Text=new ConnectionSettings{BaseUrl="https://old-provider.invalid/v1",Model="old-model",ApiKey="dummy-model-key"},WebSearch=new WebSearchSettings{Provider="native",Connection=new ConnectionSettings{BaseUrl="https://legacy-search.example",Model="search",ApiKey="fixture-legacy-tavily-key"}}};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                var legacy=cfg.WebSearch.Copy();
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"search-ui-"+Guid.NewGuid().ToString("N"),"settings.json")),null);
                var panel=new WebSearchSettingsPanel(pet);host=new Window{Content=panel,Width=600,Height=700};host.Show();host.UpdateLayout();
                var enabled=All<CheckBox>(panel).Single(c=>c.Name=="WebSearchEnabled");var provider=All<ComboBox>(panel).Single(c=>c.Name=="WebSearchProvider");var keyInput=All<PasswordBox>(panel).Single();
                Check(!enabled.IsChecked.Value&&!panel.HasUnsavedChanges,"native search starts clean with legacy Tavily credentials retained separately");
                enabled.IsChecked=true;provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"tavily");keyInput.Password="fixture-tavily-key";var draft=panel.Read();
                var secret=(SecretField)VisualTreeHelper.GetParent(keyInput);
                var visibleKey=secret.Children.OfType<TextBox>().Single();
                var eye=secret.Children.OfType<Button>().Single();
                var originalLanguage=TextResources.CurrentLanguage;
                try{
                    foreach(var language in new[]{"en_US","zh_CN"}){
                        TextResources.ApplyLanguage(TextResources.PrepareLanguage(language));
                        host.UpdateLayout();
                        var maskedPeer=new System.Windows.Automation.Peers.PasswordBoxAutomationPeer(keyInput);
                        Check(maskedPeer.GetName()==TextResources.Get("webSearch.key"),"masked search key has a localized accessible name "+language);
                        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var visiblePeer=new System.Windows.Automation.Peers.TextBoxAutomationPeer(visibleKey);
                        Check(visibleKey.IsVisible&&visiblePeer.GetName()==TextResources.Get("webSearch.key"),"revealed search key has a localized accessible name "+language);
                        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                }finally{TextResources.ApplyLanguage(TextResources.PrepareLanguage(originalLanguage));}
                Check(draft.Enabled&&draft.Provider=="tavily"&&draft.Connection.ApiKey=="fixture-tavily-key"&&panel.HasUnsavedChanges,"Tavily selection and masked key produce an isolated draft");
                Check(provider.Items.Count==WebSearchProviders.Ids.Length,"every supported provider is selectable");
                provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"brave");Check(keyInput.Password==""&&All<TextBox>(panel).Single(t=>t.Name=="WebSearchAddress").Text==WebSearchProviders.DefaultBaseUrl("brave"),"switching providers uses a separate key and official endpoint");keyInput.Password="fixture-brave-key";
                provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"tavily");Check(keyInput.Password=="fixture-tavily-key","switching providers preserves each unsaved connection");
                provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"custom");var customFormat=All<ComboBox>(panel).Single(c=>c.Name=="WebSearchCustomProtocol");var address=All<TextBox>(panel).Single(t=>t.Name=="WebSearchAddress");Check(address.Text==""&&keyInput.Password==""&&customFormat.IsVisible,"custom API exposes its format with separate empty credentials");address.Text="http://127.0.0.1:9999/full/search";customFormat.SelectedIndex=Array.IndexOf(WebSearchProviders.SchemaIds,"deepseek");Check(panel.Read().CustomProtocol=="deepseek"&&panel.Read().Connection.BaseUrl==address.Text,"custom draft keeps a full endpoint and compatible schema");
                provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,"tavily");
                Check(!cfg.WebSearch.Enabled&&cfg.WebSearch.Connection.ApiKey=="fixture-legacy-tavily-key","editing search settings does not mutate live configuration");
                var sources=All<CheckBox>(panel).Single(c=>c.Name=="WebSearchSources");sources.IsChecked=false;Check(!panel.Read().ShowSources,"source citation toggle is saved in the draft");
                Check(All<Button>(panel).Any(b=>b.Name=="SaveWebSearch"),"search settings have an explicit save action");
                All<Button>(panel).Single(b=>b.Name=="SaveWebSearch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var timer=System.Diagnostics.Stopwatch.StartNew();while(panel.HasUnsavedChanges&&timer.ElapsedMilliseconds<10000){System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background,new Action(delegate{}));System.Threading.Thread.Sleep(10);}
                Check(pet.Settings.WebSearch.Enabled&&pet.Settings.WebSearch.Provider=="tavily"&&!panel.HasUnsavedChanges,"explicit save publishes the search draft and acknowledges it");
                pet.Settings.WebSearch=legacy.Copy();var restoredPanel=new WebSearchSettingsPanel(pet);var restoredSurface=new DockPanel();var restoreFooter=restoredPanel.DetachSaveFooter();DockPanel.SetDock(restoreFooter,Dock.Bottom);restoredSurface.Children.Add(restoreFooter);restoredSurface.Children.Add(Ui.Scroll(restoredPanel));host.Content=restoredSurface;host.UpdateLayout();Check(!restoredPanel.HasUnsavedChanges,"legacy native configuration opens without a pending draft");All<Button>(restoredSurface).Single(b=>b.Name=="RestoreWebSearch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!restoredPanel.HasUnsavedChanges,"restore stays clean with a retained legacy Tavily custom address and key");
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web-search-settings.png")))png.Save(file);
                return 0;
            }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
