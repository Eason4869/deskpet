using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Dafeiyu
{
    public sealed class AboutPanel : Grid,IDisposable
    {
        readonly PetWindow pet;readonly UpdateCoordinator updates;
        readonly RadioButton direct,mirror,proxy,customMirror;
        readonly RadioButton[] builtinMirrors;
        static readonly string[] mirrorPresets={PetSettings.DefaultUpdateMirror,"https://gh-proxy.com","https://edgeone.gh-proxy.com","https://hk.gh-proxy.com"};
        readonly TextBox proxyAddress,mirrorAddress;readonly StackPanel mirrorOptions,proxyOptions;
        readonly TextBlock status,progressLabel,routeLabel,networkNotice;
        readonly ComboBox channel;readonly TextBlock channelNotice;
        readonly Button check,cancel,openFolder,testMirror,uninstall;readonly ProgressBar progress;
        CancellationTokenSource probe;bool disposed,initializing=true;
        readonly Dictionary<RadioButton,Tuple<TextBlock,TextBlock>> mirrorLabels=new Dictionary<RadioButton,Tuple<TextBlock,TextBlock>>();
        readonly HashSet<string> testedMirrors=new HashSet<string>(StringComparer.Ordinal);
        internal Func<string,CancellationToken,Task> MirrorProbe {get;set;}
        string savedMirror;

        public bool HasUnsavedChanges {get{return (proxy.IsChecked==true)!=pet.Settings.UpdateUseProxy || (mirror.IsChecked==true)!=pet.Settings.UpdateUseMirror || (proxy.IsChecked==true && ProxyConfiguration.DisplayAddress(proxyAddress.Text)!=ProxyConfiguration.DisplayAddress(pet.Settings.UpdateProxyAddress)) || (mirror.IsChecked==true && SelectedMirror.Trim().TrimEnd('/')!=pet.Settings.UpdateMirrorAddress);}}
        public AboutPanel(PetWindow companion)
        {
            pet=companion;updates=pet.Updates;savedMirror=pet.Settings.UpdateUseMirror?pet.Settings.UpdateMirrorAddress:null;Margin=new Thickness(24,20,24,20);
            RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var panel=new StackPanel();Children.Add(Ui.Scroll(panel));
            var hero=new DockPanel{Margin=new Thickness(0,0,0,18)};
            var logo=new Image{Source=Ui.ApplicationIcon(),Width=62,Height=62,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,16,0)};RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality);DockPanel.SetDock(logo,Dock.Left);hero.Children.Add(logo);
            var brand=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=Ui.Text("桌面伙伴",27);title.FontWeight=FontWeights.SemiBold;brand.Children.Add(title);
            brand.Children.Add(Ui.Text("DeskPet  "+ReleaseInfo.DisplayVersion+"  ·  "+ReleaseInfo.Author,12,Ui.Muted));hero.Children.Add(brand);panel.Children.Add(hero);
            var introduction=Ui.Text("陪你写代码、做设计，也聊聊今天的小想法。",13,Ui.Muted);introduction.Margin=new Thickness(0,0,0,20);panel.Children.Add(introduction);
            var update=new StackPanel();update.Children.Add(Heading("保持最新",17));
            channel=new ComboBox{Name="UpdateChannel",ItemsSource=new[]{"正式版（main）","预览版（dev）"},SelectedIndex=pet.Settings.UpdateChannel==UpdateChannel.Preview?1:0,Margin=new Thickness(0,8,0,4)};update.Children.Add(channel);
            channelNotice=Ui.Text("",11,Ui.Muted);update.Children.Add(channelNotice);
            channel.SelectionChanged+=delegate{if(!initializing){updates.ChangeChannel(channel.SelectedIndex==1?UpdateChannel.Preview:UpdateChannel.Stable);Refresh();}};
            status=Ui.Text("",12);status.Name="UpdateStatus";status.Margin=new Thickness(0,6,0,8);update.Children.Add(status);
            var actions=new WrapPanel();check=Ui.Button("检查并更新",true);check.Name="CheckUpdates";check.Click+=async delegate{if(updates.IsReadyToInstall)await updates.InstallPendingNowAsync();else if(SaveNetwork())await updates.CheckAndUpdateAsync(true);};actions.Children.Add(check);
            cancel=Ui.Button("取消更新");cancel.Name="CancelUpdate";cancel.Click+=delegate{updates.Cancel();};actions.Children.Add(cancel);update.Children.Add(actions);
            progress=new ProgressBar{Name="UpdateProgress",Minimum=0,Maximum=100,Height=5,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};update.Children.Add(progress);
            progressLabel=Ui.Text("",11,Ui.Blue);update.Children.Add(progressLabel);routeLabel=Ui.Text("",11,Ui.Muted);routeLabel.Name="UpdateRoute";routeLabel.Margin=new Thickness(0,6,0,0);update.Children.Add(routeLabel);
            var automatic=Ui.Toggle("自动更新",pet.Settings.AutoUpdate,v=>{pet.Settings.AutoUpdate=v;updates.AutomaticChanged();});automatic.Name="AutoUpdate";automatic.Margin=new Thickness(0,16,0,5);update.Children.Add(automatic);
            panel.Children.Add(GlassSurface.Card(update));
            var network=new Expander{Name="UpdateNetwork",Header="网络加速",IsExpanded=false,Margin=new Thickness(0,0,0,18),FontSize=14,Foreground=Ui.Ink};
            var networkBody=new StackPanel{Margin=new Thickness(2,10,0,0)};
            direct=Choice("不使用 GitHub 加速","UpdateDirect","UpdateNetworkMode",!pet.Settings.UpdateUseProxy && !pet.Settings.UpdateUseMirror);networkBody.Children.Add(direct);
            mirror=Choice("使用 GitHub 加速","UpdateMirror","UpdateNetworkMode",pet.Settings.UpdateUseMirror);var mirrorHeading=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};mirrorHeading.Children.Add(mirror);networkBody.Children.Add(mirrorHeading);
            mirrorOptions=new StackPanel{Margin=new Thickness(24,0,0,10)};
            bool custom=!mirrorPresets.Contains(pet.Settings.UpdateMirrorAddress);
            builtinMirrors=new RadioButton[mirrorPresets.Length];
            foreach(int i in Enumerable.Range(0,mirrorPresets.Length).OrderBy(index=>mirrorPresets[index].Length)){var option=Choice(mirrorPresets[i],i==0?"DefaultUpdateMirror":"BuiltinUpdateMirror"+i,"UpdateMirrorChoice",savedMirror==mirrorPresets[i]);option.Tag=mirrorPresets[i];builtinMirrors[i]=option;AddMirrorRow(option,mirrorPresets[i],i.ToString());}
            customMirror=Choice("自定义镜像地址","CustomUpdateMirror","UpdateMirrorChoice",custom && savedMirror!=null);AddMirrorRow(customMirror,"自定义","Custom");
            mirrorAddress=Ui.Field(custom?pet.Settings.UpdateMirrorAddress:"");mirrorAddress.Name="UpdateMirrorAddress";mirrorAddress.Margin=new Thickness(22,0,0,7);mirrorAddress.LostFocus+=delegate{if(customMirror.IsChecked==true)SaveNetwork();};mirrorOptions.Children.Add(mirrorAddress);mirrorAddress.TextChanged+=delegate{if(!initializing){SetMirrorResult(customMirror,"待测试",null);Refresh();}};
            testMirror=Ui.Button("测试连通性");testMirror.Name="TestUpdateMirror";testMirror.HorizontalAlignment=HorizontalAlignment.Left;testMirror.FontSize=12;testMirror.Margin=new Thickness(10,0,0,0);testMirror.Click+=async delegate{await TestMirrorsAsync();};mirrorHeading.Children.Add(testMirror);
            mirrorOptions.Children.Add(Ui.Text("先测试，再选择可用地址。仅加速更新包下载；失败后自动回退 GitHub 直连。",11,Ui.Muted));networkBody.Children.Add(mirrorOptions);
            proxy=Choice("使用网络代理","UpdateProxy","UpdateNetworkMode",pet.Settings.UpdateUseProxy);networkBody.Children.Add(proxy);
            proxyOptions=new StackPanel{Margin=new Thickness(24,0,0,9)};proxyAddress=Ui.Field(ProxyConfiguration.DisplayAddress(pet.Settings.UpdateProxyAddress));proxyAddress.Name="UpdateProxyAddress";proxyAddress.LostFocus+=delegate{SaveNetwork();};proxyOptions.Children.Add(proxyAddress);proxyOptions.Children.Add(Ui.Text("默认 http://127.0.0.1:7890，用于检查版本与下载更新。",11,Ui.Muted));networkBody.Children.Add(proxyOptions);
            networkNotice=Ui.Text("",11,Ui.Muted);networkNotice.Name="UpdateNetworkNotice";networkBody.Children.Add(networkNotice);network.Content=networkBody;panel.Children.Add(network);
            foreach(var option in new[]{direct,mirror,proxy,customMirror}.Concat(builtinMirrors))option.Checked+=delegate{if(!initializing){RefreshNetwork();SaveNetwork();Refresh();}};
            panel.Children.Add(new Border{Height=1,Background=Ui.Line,Margin=new Thickness(0,0,0,15)});panel.Children.Add(Heading("项目与帮助",14));
            var repository=Ui.Text("",12,Ui.Blue);repository.Name="RepositoryUrl";repository.Margin=new Thickness(0,5,0,10);
            var link=new Hyperlink(new Run(ReleaseInfo.RepositoryUrl)){NavigateUri=new Uri(ReleaseInfo.RepositoryUrl)};link.RequestNavigate+=delegate(object sender,System.Windows.Navigation.RequestNavigateEventArgs args){args.Handled=true;Open(ReleaseInfo.RepositoryUrl);};repository.Inlines.Add(link);panel.Children.Add(repository);
            var links=new WrapPanel();var releases=Ui.Button("版本记录");releases.Click+=delegate{Open(ReleaseInfo.RepositoryUrl+"/releases");};links.Children.Add(releases);var feedback=Ui.Button("反馈问题");feedback.Click+=delegate{Open(ReleaseInfo.RepositoryUrl+"/issues");};links.Children.Add(feedback);
            openFolder=Ui.Button("更新文件夹");openFolder.Name="OpenDownloads";openFolder.Click+=delegate{Open(Path.Combine(pet.Store.DirectoryPath,"updates"));};links.Children.Add(openFolder);panel.Children.Add(links);
            var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};uninstall=Ui.Button("卸载");uninstall.Name="UninstallApplication";uninstall.Foreground=Ui.Brush("#AC4A57");uninstall.Margin=new Thickness(12,0,0,0);uninstall.Click+=delegate{Uninstall();};DockPanel.SetDock(uninstall,Dock.Right);footer.Children.Add(uninstall);footer.Children.Add(Ui.Text("卸载会退出桌宠并打开卸载向导。\n本机设置、聊天记录和外观数据会保留。",11,Ui.Muted));var footerBorder=new Border{Child=footer,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0)};Grid.SetRow(footerBorder,1);Children.Add(footerBorder);
            initializing=false;RefreshNetwork();updates.Changed+=Refresh;Refresh();
        }
        static TextBlock Heading(string text,double size){var label=Ui.Text(text,size);label.FontWeight=FontWeights.SemiBold;return label;}
        static RadioButton Choice(string text,string name,string group,bool selected){return new RadioButton{Name=name,Content=text,GroupName=group,IsChecked=selected,Margin=new Thickness(0,7,0,7),VerticalContentAlignment=VerticalAlignment.Center,Foreground=Ui.Ink,FontSize=12};}
        string SelectedMirror {get{var selected=builtinMirrors.FirstOrDefault(b=>b.IsChecked==true);return selected==null?(customMirror.IsChecked==true?mirrorAddress.Text:""):(string)selected.Tag;}}
        void RefreshNetwork(){mirrorOptions.Visibility=mirror.IsChecked==true?Visibility.Visible:Visibility.Collapsed;proxyOptions.Visibility=proxy.IsChecked==true?Visibility.Visible:Visibility.Collapsed;testMirror.Visibility=mirror.IsChecked==true?Visibility.Visible:Visibility.Collapsed;}
        internal bool ValidateNetwork(out string error)
        {
            try{if(proxy.IsChecked==true)ProxyConfiguration.Endpoint(ProxyConfiguration.Normalize(proxyAddress.Text));if(mirror.IsChecked==true){string address=GitHubUpdates.MirrorPrefix(SelectedMirror);if(!testedMirrors.Contains(address) && address!=savedMirror)throw new InvalidOperationException("请先测试连通性，再选择可用的镜像地址。");}error="";return true;}
            catch(InvalidOperationException e){error=e.Message;return false;}
        }
        internal bool SaveNetwork()
        {
            if(initializing || disposed)return false;string error;if(!ValidateNetwork(out error)){networkNotice.Text=mirror.IsChecked==true && String.IsNullOrEmpty(SelectedMirror)?"请先测试连通性，再选择可用地址。":error;return false;}
            try{string proxyValue=pet.Settings.UpdateProxyAddress,mirrorValue=pet.Settings.UpdateMirrorAddress;if(proxy.IsChecked==true){proxyValue=ProxyConfiguration.DisplayAddress(proxyAddress.Text);ProxyConfiguration.Endpoint(proxyValue);}if(mirror.IsChecked==true)mirrorValue=GitHubUpdates.MirrorPrefix(SelectedMirror);pet.Settings.UpdateUseProxy=proxy.IsChecked==true;pet.Settings.UpdateUseMirror=mirror.IsChecked==true;pet.Settings.UpdateProxyAddress=proxyValue;if(proxy.IsChecked==true)proxyAddress.Text=proxyValue;pet.Settings.UpdateMirrorAddress=mirrorValue;if(mirror.IsChecked==true)savedMirror=mirrorValue;pet.SaveSettings();networkNotice.Text="已保存。";return true;}
            catch(InvalidOperationException e){networkNotice.Text=e.Message;updates.ReportValidation(e.Message);return false;}
        }
        void AddMirrorRow(RadioButton option,string address,string suffix)
        {
            var content=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var label=Ui.Text(address,12);label.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(label);
            var state=Ui.Text(option.IsChecked==true?"已保存":"待测试",10,Ui.Muted);state.Name="MirrorStatus"+suffix;
            var badge=new Border{Child=state,CornerRadius=new CornerRadius(9),Background=Ui.Pale,Padding=new Thickness(6,2,6,2),Margin=new Thickness(8,0,0,0)};content.Children.Add(badge);
            var latency=Ui.Text("",10,Ui.Brush("#159EB3"));latency.Name="MirrorLatency"+suffix;
            content.Children.Add(new Border{Child=latency,CornerRadius=new CornerRadius(9),Background=Ui.Brush("#E2F8FB"),Padding=new Thickness(5,2,5,2),Margin=new Thickness(5,0,0,0),Visibility=Visibility.Collapsed});
            option.Content=content;option.Margin=new Thickness(0,9,0,9);mirrorLabels.Add(option,Tuple.Create(state,latency));mirrorOptions.Children.Add(option);
        }
        void SetMirrorResult(RadioButton row,string text,long? milliseconds)
        {
            var labels=mirrorLabels[row];labels.Item1.Text=text;labels.Item1.Foreground=text=="可用"?Ui.Brush("#00A86B"):text=="不可用"?Ui.Brush("#AC4A57"):Ui.Muted;
            ((Border)labels.Item1.Parent).Background=text=="可用"?Ui.Brush("#E0F8EC"):Ui.Pale;
            labels.Item2.Text=milliseconds.HasValue?milliseconds.Value+"ms":"";((Border)labels.Item2.Parent).Visibility=milliseconds.HasValue?Visibility.Visible:Visibility.Collapsed;
        }
        string AddressOf(RadioButton row){return row==customMirror?mirrorAddress.Text.Trim().TrimEnd('/'):(string)row.Tag;}
        internal async Task TestMirrorsAsync()
        {
            if(probe!=null || updates.IsBusy || disposed)return;
            var cancellation=new CancellationTokenSource();probe=cancellation;Refresh();networkNotice.Text="正在获取官方更新信息并测试镜像…";
            var rows=builtinMirrors.Concat(String.IsNullOrWhiteSpace(mirrorAddress.Text)?new RadioButton[0]:new[]{customMirror}).ToArray();
            foreach(var row in rows){testedMirrors.Remove(AddressOf(row));SetMirrorResult(row,"测试中",null);}
            try
            {
                using(var client=new GitHubUpdates())
                {
                    var release=MirrorProbe==null?await client.CheckAsync(ReleaseInfo.RepositoryUrl,false,ProxyConfiguration.DefaultAddress,false,cancellation.Token):null;
                    await Task.WhenAll(rows.Select(async row=>
                    {
                        string address=AddressOf(row);var timer=Stopwatch.StartNew();
                        try
                        {
                            GitHubUpdates.MirrorPrefix(address);
                            if(MirrorProbe!=null)await MirrorProbe(address,cancellation.Token);else await client.TestMirrorAsync(address,release,cancellation.Token);
                            cancellation.Token.ThrowIfCancellationRequested();if(disposed || address!=AddressOf(row))return;
                            testedMirrors.Add(address);row.ToolTip=null;SetMirrorResult(row,"可用",Math.Max(1,timer.ElapsedMilliseconds));
                        }
                        catch(OperationCanceledException){if(!disposed)SetMirrorResult(row,"已取消",null);}
                        catch(Exception error){if(!disposed){SetMirrorResult(row,"不可用",null);row.ToolTip=error is ApiException || error is InvalidOperationException?error.Message:"测试失败，请稍后重试。";}}
                    }));
                }
                if(!disposed)networkNotice.Text="测试完成，请选择可用地址；耗时为读取更新包文件头的时间。";
            }
            catch(OperationCanceledException){if(!disposed){foreach(var row in rows)SetMirrorResult(row,"已取消",null);networkNotice.Text="测试已取消。";}}
            catch(Exception error){if(!disposed){foreach(var row in rows)SetMirrorResult(row,"未测试",null);networkNotice.Text=error is ApiException?error.Message:"无法获取官方更新信息，尚未测试镜像。";}}
            finally{probe=null;cancellation.Dispose();if(!disposed)Refresh();}
        }
        void Refresh()
        {
            if(disposed)return;channel.IsEnabled=!updates.IsBusy&&probe==null;channelNotice.Text=pet.Settings.UpdateChannel==UpdateChannel.Preview?"预览通道可能包含未稳定功能，也接收更高正式版。":"仅接收正式版，切换通道不会降级。";bool networkBusy=updates.IsBusy || probe!=null;check.Content=updates.IsReadyToInstall?"立即更新并重启":"检查并更新";check.IsEnabled=probe==null && (!updates.IsBusy || updates.IsReadyToInstall);cancel.IsEnabled=updates.CanCancel;
            foreach(var option in new[]{direct,mirror,proxy})option.IsEnabled=!networkBusy;foreach(var option in builtinMirrors.Concat(new[]{customMirror})){string address=AddressOf(option);option.IsEnabled=!networkBusy && (testedMirrors.Contains(address) || (option.IsChecked==true && address==savedMirror));}testMirror.Content=probe==null?"测试连通性":"正在测试…";proxyAddress.IsEnabled=mirrorAddress.IsEnabled=testMirror.IsEnabled=!networkBusy;
            progress.Visibility=updates.IsBusy?Visibility.Visible:Visibility.Collapsed;progress.IsIndeterminate=updates.IsBusy && !updates.IsDownloading && updates.Progress<100;progress.Value=updates.Progress;
            progressLabel.Text=updates.IsDownloading?"下载进度 "+Math.Round(updates.Progress)+"%":"";routeLabel.Text=updates.NetworkStatus??"从所选 GitHub 发行通道更新，设置与聊天数据会保留。";status.Text=updates.Status;openFolder.IsEnabled=Directory.Exists(Path.Combine(pet.Store.DirectoryPath,"updates"));uninstall.IsEnabled=!networkBusy;
        }
        void Uninstall()
        {
            if(!pet.CanApplyUpdate(true)){updates.ReportValidation("请先保存设置和输入草稿，结束对话、听写或引导，再打开卸载向导。");return;}
            string path;try{path=UpdateInstall.GetUninstallerPath(AppDomain.CurrentDomain.BaseDirectory);}catch(System.Security.SecurityException){updates.ReportValidation("无法读取安装记录，可从 Windows“已安装的应用”中卸载桌面伙伴。");return;}
            if(path==null){updates.ReportValidation("当前为便携运行目录。退出后可移除程序文件；需保留资料时请保留 data 文件夹。");return;}
            try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)});pet.Close();}catch(System.ComponentModel.Win32Exception){updates.ReportValidation("卸载向导未能启动，可从 Windows“已安装的应用”中卸载桌面伙伴。");}
        }
        void Open(string address){try{Process.Start(new ProcessStartInfo(address){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){updates.ReportValidation("Windows 暂时无法打开此地址或文件夹。");}}
        public void Dispose(){if(disposed)return;disposed=true;if(probe!=null)probe.Cancel();updates.Changed-=Refresh;}
    }
}
