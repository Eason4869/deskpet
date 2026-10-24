using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    public sealed class DataActionConfirmationWindow:Window
    {
        readonly Button confirm,cancel;readonly TextBlock status;readonly Func<Task<string>> execute,flush;bool busy,started;
        internal Task PendingOperation {get;private set;}
        public DataActionConfirmationWindow(string title,string scope,Func<Task<string>> action,Func<Task<string>> flushPending=null)
        {
            execute=action;flush=flushPending;PendingOperation=Task.FromResult(0);Name="DataActionConfirmation";Title=title;Width=520;Height=440;MinWidth=380;MinHeight=340;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
            Background=ThemeManager.Surface;Foreground=Ui.Ink;FontFamily=new FontFamily("Global User Interface");
            var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var body=new StackPanel();body.Children.Add(Ui.Text(title,20));var text=Ui.Text(scope,13);text.Name="DataActionScope";text.Margin=new Thickness(0,16,0,16);body.Children.Add(text);status=Ui.Text("",13);status.Name="DataActionStatus";body.Children.Add(status);grid.Children.Add(Ui.Scroll(body));
            var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};cancel=Ui.Button(TextResources.Get("common.cancel"));cancel.Name="CancelDataAction";cancel.Click+=delegate{Close();};
            confirm=Ui.Button(title,true);confirm.Name="ConfirmDataAction";confirm.Click+=delegate{PendingOperation=ExecuteAsync();};actions.Children.Add(cancel);actions.Children.Add(confirm);Grid.SetRow(actions,1);grid.Children.Add(actions);Content=grid;
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;if(!busy)Close();}};Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy)e.Cancel=true;};
        }
        async Task ExecuteAsync()
        {
            if(started)return;started=busy=true;confirm.IsEnabled=cancel.IsEnabled=false;status.Text=TextResources.Get("dataManagement.processing");
            try{status.Text=await execute();}
            catch(InvalidOperationException){status.Text=TextResources.Get("dataMaintenance.busy");}
            catch(Exception){status.Text=TextResources.Get("dataManagement.failed");}
            {if(flush!=null){string warning=await flush();if(!String.IsNullOrEmpty(warning))status.Text+="\n"+warning;}busy=false;cancel.IsEnabled=true;cancel.Content=TextResources.Get("dataManagement.close");}
        }
    }
}
