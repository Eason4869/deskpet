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
                var header=new SettingsPageHeader(()=>TextResources.Get("menus.language"),()=>TextResources.Get("common.cancel"),()=>new[]{TextResources.Get("common.save"),TextResources.Get("common.cancel")},new[]{"LiveFirstTab","LiveSecondTab"});header.Select(1);int selections=0;header.SelectionChanged+=index=>selections++;panel.Children.Add(header);var iconButton=ShellIcons.Button("expand-tabs",()=>TextResources.Get("menus.language"),null);var originalIcon=iconButton.Content;panel.Children.Add(iconButton);
                var window=new Window{Content=panel,Width=400,Height=240};window.Show();Pump();
                var optional=ImageGenerationSettings.DefaultChoices(new[]{"1024x1024","high"});panel.Children.Add(optional);Pump();int optionalChanges=0;optional.SelectionChanged+=delegate{optionalChanges++;};
                var rules=(StackPanel)HomeVisual.Rules();panel.Children.Add(rules);var originalRules=rules;
                var styled=LocalizedUi.Button(()=>TextResources.Get("common.save"));DataManagementVisual.Button(styled);var originalStyledCaption=styled.Content;panel.Children.Add(styled);
                foreach(string code in UiLanguages.Choices.Where(choice=>choice.Code!="system").Select(choice=>choice.Code)){var before=window.Content;TextResources.ApplyLanguage(TextResources.PrepareLanguage(code));Pump();Check(text.Text==TextResources.Get("menus.language")&&ReferenceEquals(before,window.Content),"original controls update "+code);Check(draft.Text=="unsaved 草稿"&&draft.SelectionStart==3&&draft.SelectionLength==2,"draft and selection survive "+code);Check(header.Title.Text==TextResources.Get("menus.language")&&((TextBlock)header.Tabs[0].Content).Text==TextResources.Get("common.save")&&header.SelectedIndex==1&&selections==0,"settings header and tabs update without navigation "+code);Check(ReferenceEquals(originalIcon,iconButton.Content)&&(string)iconButton.ToolTip==TextResources.Get("menus.language"),"icon retained and tooltip updated "+code);}
                window.Hide();TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));window.Show();Pump();Check(text.Text=="Language","hidden control uses active language");
                Check(ReferenceEquals(styled.Content,originalStyledCaption)&&((TextBlock)styled.Content).Text==TextResources.Get("common.save"),"styled maintenance button retains its caption binding and original child");
                var previews=new PreviewWorkspace(delegate{},delegate{});var previewBody=new TextBlock{Text="persistent content"};previews.AddPage(()=>TextResources.Get("menus.language"),"file",previewBody);panel.Children.Add(previews);TextResources.ApplyLanguage(TextResources.PrepareLanguage("ja_JP"));Pump();Check(previews.Selected==previewBody&&Walk(previews).OfType<TextBlock>().Any(label=>label.Text==TextResources.Get("menus.language")),"canonical preview tab title refreshes without replacing its selected body");TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();string literalTitle=TextResources.Get("previewWorkspace.conversationTitle.text");previews.AttachConversation(new TextBlock(),null);previews.UpdateConversationTitle(literalTitle);TextResources.ApplyLanguage(TextResources.PrepareLanguage("ja_JP"));Pump();Check(Walk(previews).OfType<TextBlock>().Single(label=>label.Name=="ConversationPreviewTitle").Text==literalTitle,"literal user title equal to an old default caption is never translated");TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();previews.Dispose();panel.Children.Remove(previews);
                Check(optional.SelectedIndex==0&&ImageGenerationSettings.Optional(optional)==""&&optional.Text==TextResources.Get("imageGenerationSettings.defaultChoices.text")&&optionalChanges==0,"translated editable default keeps identity and request parameter without selection events");
                optional.Text="custom unsaved dimension";TextResources.ApplyLanguage(TextResources.PrepareLanguage("ja_JP"));Pump();Check(optional.Text=="custom unsaved dimension"&&ImageGenerationSettings.Optional(optional)=="custom unsaved dimension","editable custom parameter remains an exact draft");optional.SelectedIndex=0;TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();Check(optional.SelectedIndex==0&&ImageGenerationSettings.Optional(optional)=="","reselecting default keeps sentinel after another language switch");
                Check(ReferenceEquals(originalRules,rules)&&rules.Children.OfType<Grid>().Any(row=>row.Children.OfType<TextBlock>().Any(label=>label.Text==TextResources.Get("relationship.home.ruleVisit"))),"relationship rule helper refreshes existing label");
                var dialog=new LanguagePickerWindow("en_US",delegate{});dialog.Show();Pump();TextResources.ApplyLanguage(TextResources.PrepareLanguage("ja_JP"));Pump();Check(dialog.Title==TextResources.Get("menus.language"),"real language dialog title hot updates");
                dialog.Close();window.Close();TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));Console.WriteLine("LiveLanguageUiTests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var nested in Walk(child))yield return nested;}
    }
}
