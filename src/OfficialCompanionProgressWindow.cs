using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed class OfficialCompanionProgressWindow:Window
    {
        readonly CancellationTokenSource cancel=new CancellationTokenSource();
        readonly TextBlock message,roles;
        readonly ProgressBar progress;
        readonly Button stop,retry;
        readonly Dictionary<string,string> status=new Dictionary<string,string>();
        bool finished,closed;readonly bool passive;
        internal bool IsPassive {get{return passive;}}
        internal CancellationToken Token {get{return cancel.Token;}}
        internal OfficialCompanionProgressWindow(Window owner,bool all,bool passive=false)
        {
            this.passive=passive;
            Ui.Window(this,all?"全量下载／更新":"下载角色包",430,280);Name="OfficialCompanionProgress";
            if(owner!=null&&owner.IsLoaded){Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}ShowInTaskbar=false;ResizeMode=ResizeMode.NoResize;
            var panel=new StackPanel{Margin=new Thickness(22)};
            message=Ui.Text("正在检查适用的角色包…",13);message.Name="OfficialDownloadStatus";panel.Children.Add(message);
            progress=new ProgressBar{Name="OfficialDownloadProgress",Minimum=0,Maximum=100,IsIndeterminate=true,Height=8,Margin=new Thickness(0,14,0,12)};panel.Children.Add(progress);
            roles=Ui.Text("",11,Ui.Muted);roles.TextWrapping=TextWrapping.Wrap;panel.Children.Add(roles);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,16,0,0)};
            stop=Ui.Button("取消");stop.Name="CancelOfficialDownload";stop.Click+=delegate{if(finished)Close();else{cancel.Cancel();stop.IsEnabled=false;message.Text="正在取消，已安装的角色包会保留。";}};actions.Children.Add(stop);
            retry=Ui.Button("重试");retry.Name="RetryOfficialDownload";retry.Visibility=Visibility.Collapsed;actions.Children.Add(retry);panel.Children.Add(actions);Content=panel;
            if(passive){Name="OfficialCompanionDownloadBubble";Height=140;MinHeight=0;ShowActivated=false;Focusable=false;progress.Visibility=roles.Visibility=actions.Visibility=Visibility.Collapsed;message.Text="正在更新角色包，请稍后...";}
            Closed+=delegate{closed=true;if(!finished&&!passive)cancel.Cancel();};
        }
        internal void Report(OfficialCompanionProgress value)
        {
            if(passive)return;
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>Report(value)));return;}if(closed||finished)return;
            string stage=value.Stage=="metadata"?"检查版本":value.Stage=="download"?"下载":value.Stage=="validate"?"检查素材":value.Stage=="install"?"安装":"准备";
            var profile=OfficialCompanionProfiles.Find(value.CharacterId);string name=profile==null?"官方伙伴":profile.Name;
            message.Text=name+" · "+stage;
            if(value.TotalBytes>0)message.Text+=" · "+(value.CompletedBytes/1048576d).ToString("0.0")+" / "+(value.TotalBytes/1048576d).ToString("0.0")+" MB";
            if(!String.IsNullOrEmpty(value.Route))message.Text+=" · "+(value.Route.IndexOf("镜像",StringComparison.Ordinal)>=0?"镜像加速":value.Route.IndexOf("代理",StringComparison.Ordinal)>=0?"网络代理":"GitHub 直连");
            progress.IsIndeterminate=value.TotalBytes<=0;progress.Value=value.Percent;
            if(profile!=null)status[profile.Id]=name+"："+stage;
            roles.Text=String.Join("　",OfficialCompanionProfiles.All.Where(p=>status.ContainsKey(p.Id)).Select(p=>status[p.Id]));
        }
        internal void Complete(){if(closed)return;finished=true;progress.IsIndeterminate=false;progress.Value=100;Close();}
        internal void Failure(string text,Action again)
        {
            if(closed)return;finished=true;progress.IsIndeterminate=false;message.Text=text;stop.Content="关闭";stop.IsEnabled=true;
            if(again!=null){retry.Visibility=Visibility.Visible;retry.Click+=delegate{Close();again();};}
        }
    }
}
