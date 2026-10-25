using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class LiveLanguageUiChecks
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var runtime=new TestRuntime(directory);runtime.InitializeProtection();
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage="zh_CN",AutoUpdate=false,OnboardingState="completed",GreetingEnabled=false,ReadReplies=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),runtime);
        var chat=new ChatWindow(owner,false);chat.Show();var settings=new SettingsWindow(owner);settings.Show();settings.SelectSection("text");Dispatcher.UIThread.RunJobs();
        var field=settings.GetLogicalDescendants().OfType<TextBox>().First();field.Text="unsaved 草稿";field.SelectionStart=2;field.SelectionEnd=5;
        int checks=0;void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
        var content=settings.Content;var session=owner.Session;
        var maintenanceHost=new Window{Width=650,Height=700};var maintenance=new DataManagementView(owner,maintenanceHost,()=>{});maintenanceHost.Content=maintenance;maintenanceHost.Show();Dispatcher.UIThread.RunJobs();
        var clear=maintenance.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="ClearAllHistory");var clearCaption=clear.Content;
        var styled=LocalizedUi.Button(()=>TextResources.Get("common.save"),()=>{});SettingsVisual.StyleButton(styled);var styledCaption=styled.Content;maintenance.Children.Add(styled);
        var petMenu=(ContextMenu)owner.Pet.GetType().GetMethod("BuildMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(owner.Pet,null)!;
        foreach(var choice in UiLanguages.Choices.Where(c=>c.Code!="system"))
        {
            owner.SetUiLanguage(choice.Code);Dispatcher.UIThread.RunJobs();
            Check(TextResources.CurrentLanguage==choice.Code,"effective language "+choice.Code);
            Check(ReferenceEquals(content,settings.Content)&&ReferenceEquals(session,owner.Session),"window and service identity "+choice.Code);
            Check(field.Text=="unsaved 草稿"&&field.SelectionStart==2&&field.SelectionEnd==5,"unsaved field and selection "+choice.Code);
            Check(settings.Title==TextResources.Get("settingsWindow.settingsWindow.title"),"open settings title "+choice.Code);
            Check(settings.GetLogicalDescendants().OfType<ListBox>().Single(x=>x.Name=="SettingsNavigation").GetLogicalDescendants().OfType<TextBlock>().Any(x=>x.Text==TextResources.Get("settingsWindow.settingsWindow.text")),"navigation updates "+choice.Code);
            Check(ReferenceEquals(clearCaption,clear.Content)&&((TextBlock)clear.Content!).Text==TextResources.Get("dataManagement.clearHistory")&&maintenance.GetLogicalDescendants().OfType<TextBlock>().Any(label=>label.Text==TextResources.Get("dataManagement.retained")),"retained maintenance captions and styles update "+choice.Code);
            Check(ReferenceEquals(styledCaption,styled.Content)&&((TextBlock)styled.Content!).Text==TextResources.Get("common.save"),"styled caption transfers its binding "+choice.Code);
            Check((string)((MenuItem)petMenu.ItemsSource!.Cast<object>().First()).Header! ==TextResources.Get("petWindow.buildMenu.text"),"existing pet menu caption updates "+choice.Code);
        }
        owner.SetUiLanguage("en_US");settings.Hide();owner.SetUiLanguage("ja_JP");settings.Show();Dispatcher.UIThread.RunJobs();
        var dialog=new LanguagePickerWindow(owner.Settings.UiLanguage,owner.SetUiLanguage);dialog.Show();Dispatcher.UIThread.RunJobs();owner.SetUiLanguage("en_US");Dispatcher.UIThread.RunJobs();
        Check(dialog.Title==TextResources.Get("menus.language"),"existing language dialog title updates");
        Check(!ApplicationRestart.Requested&&String.IsNullOrEmpty(owner.Session.LastNotice),"no language restart request or notice");
        dialog.Close();maintenanceHost.Close();settings.Close();chat.Close();owner.Pet.Close();owner.Dispose();
        var watch=System.Diagnostics.Stopwatch.StartNew();while(!owner.ImageServicesShutdown.IsCompleted){if(watch.ElapsedMilliseconds>10000)throw new TimeoutException("Language test cleanup");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}owner.ImageServicesShutdown.GetAwaiter().GetResult();return checks;
    }
}
