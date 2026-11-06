using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DeskPet.Desktop;
using Dafeiyu;

internal static class SecretFieldChecks
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
        var input=new TextBox{Name="FixtureKey",Text="dummy-key-中文"};var field=new SecretField(input);
        var parent=new StackPanel();parent.Children.Add(field);var window=new Window{Width=320,Height=120,Content=parent};window.Show();Dispatcher.UIThread.RunJobs();
        try{
            var eye=(Button)input.InnerRightContent!;int changes=0;input.TextChanged+=(_,_)=>changes++;
            Check(input.PasswordChar=='●'&&!input.RevealPassword,"native key starts masked");
            eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Check(input.RevealPassword&&input.Text=="dummy-key-中文"&&changes==0,"native reveal preserves the key without a draft change");
            input.Focus();input.Text="edited-dummy";Dispatcher.UIThread.RunJobs();
            Check(input.RevealPassword&&input.Text=="edited-dummy","native visible edits stay visible");
            eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!input.RevealPassword&&input.Text=="edited-dummy","native hide preserves edited key");
            eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));parent.IsVisible=false;Dispatcher.UIThread.RunJobs();
            Check(!input.RevealPassword,"native parent collapse masks the key");
            parent.IsVisible=true;eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.Content=null;Dispatcher.UIThread.RunJobs();
            Check(!input.RevealPassword,"native navigation masks the key");
            window.Content=parent;Dispatcher.UIThread.RunJobs();
            Check(!input.RevealPassword,"native reused page stays masked");
            Check(Avalonia.Automation.AutomationProperties.GetName(eye)==TextResources.Get("secretField.show"),"native eye has a localized accessible name");
            DesktopTheme.Apply("dark");var glyph=(Avalonia.Controls.Shapes.Path)((Viewbox)eye.Content!).Child!;
            Check(((Avalonia.Media.ISolidColorBrush)glyph.Stroke!).Color==((Avalonia.Media.ISolidColorBrush)SettingsVisual.Muted).Color,"native eye follows the dark theme ink");DesktopTheme.Apply("light");
            parent.Children.Remove(field);input.Text=new string('x',512);window.Content=SettingsVisual.ActionRow("",()=>"API Key",()=>"",field);window.Width=700;Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            Check(field.Width==280&&field.Bounds.Width<=280,"native secret action row retains the standard field width");
            window.Width=300;Dispatcher.UIThread.RunJobs();window.UpdateLayout();eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            Check(field.Bounds.Width<=300&&input.Bounds.Width<=300,"native long revealed key stays inside narrow settings");
        }finally{window.Close();}
        return count;
    }
}
