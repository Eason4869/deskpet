using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class LiveLanguageUiTests
    {
        static int checks;
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
        [STAThread] public static int Main()
        {
            try{
                var app=new Application();TextResources.ConfigureStartupLanguage("zh_CN");
                var text=new TextBlock();LocalizedUi.Bind(text,TextBlock.TextProperty,"menus.language");
                var draft=new TextBox{Text="unsaved 草稿",SelectionStart=3,SelectionLength=2};var panel=new StackPanel();panel.Children.Add(text);panel.Children.Add(draft);
                var window=new Window{Content=panel,Width=400,Height=240};window.Show();Pump();
                foreach(string code in UiLanguages.Choices.Where(choice=>choice.Code!="system").Select(choice=>choice.Code)){var before=window.Content;TextResources.ApplyLanguage(TextResources.PrepareLanguage(code));Pump();Check(text.Text==TextResources.Get("menus.language")&&ReferenceEquals(before,window.Content),"original controls update "+code);Check(draft.Text=="unsaved 草稿"&&draft.SelectionStart==3&&draft.SelectionLength==2,"draft and selection survive "+code);}
                window.Hide();TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));window.Show();Pump();Check(text.Text=="Language","hidden control uses active language");
                var dialog=new LanguagePickerWindow("en_US",delegate{});dialog.Show();Pump();TextResources.ApplyLanguage(TextResources.PrepareLanguage("ja_JP"));Pump();Check(dialog.Title==TextResources.Get("menus.language"),"real language dialog title hot updates");
                dialog.Close();window.Close();TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));Console.WriteLine("LiveLanguageUiTests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    }
}
