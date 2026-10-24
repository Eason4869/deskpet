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
            var brand=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=Ui.Text(TextResources.Get("app.displayName"),27);title.FontWeight=FontWeights.SemiBold;brand.Children.Add(title);
            brand.Children.Add(Ui.Text(TextResources.Format("aboutPanel.aboutPanel.template",ReleaseInfo.DisplayVersion,ReleaseInfo.Author),12,Ui.Muted));hero.Children.Add(brand);panel.Children.Add(hero);
            var introduction=Ui.Text(TextResources.Get("aboutPanel.aboutPanel.text"),13,Ui.Muted);introduction.Margin=new Thickness(0,0,0,20);panel.Children.Add(introduction);
            var update=new StackPanel();update.Children.Add(Heading(TextResources.Get("aboutPanel.aboutPanel.heading"),17));
            channel=new ComboBox{Name="UpdateChannel",ItemsSource=new[]{TextResources.Get("aboutPanel.aboutPanel.itemsSource"),TextResources.Get("aboutPanel.aboutPanel.itemsSource.alternativeA")},SelectedIndex=pet.Settings.UpdateChannel==UpdateChannel.Preview?1:0,Margin=new Thickness(0,8,0,4)};update.Children.Add(channel);
            channelNotice=Ui.Text("",11,Ui.Muted);update.Children.Add(channelNotice);
            channel.SelectionChanged+=delegate{if(!initializing){updates.ChangeChannel(channel.SelectedIndex==1?UpdateChannel.Preview:UpdateChannel.Stable);Refresh();}};
            status=Ui.Text("",12);status.Name="UpdateStatus";status.Margin=new Thickness(0,6,0,8);update.Children.Add(status);
            var actions=new WrapPanel();check=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeA"),true);check.Name="CheckUpdates";check.Click+=async delegate{if(updates.IsReadyToInstall)await updates.InstallPendingNowAsync();else if(SaveNetwork())await updates.CheckAndUpdateAsync(true);};actions.Children.Add(check);
            cancel=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeB"));cancel.Name="CancelUpdate";cancel.Click+=delegate{updates.Cancel();};actions.Children.Add(cancel);update.Children.Add(actions);
            progress=new ProgressBar{Name="UpdateProgress",Minimum=0,Maximum=100,Height=5,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};update.Children.Add(progress);
            progressLabel=Ui.Text("",11,Ui.Blue);update.Children.Add(progressLabel);routeLabel=Ui.Text("",11,Ui.Muted);routeLabel.Name="UpdateRoute";routeLabel.Margin=new Thickness(0,6,0,0);update.Children.Add(routeLabel);
            var automatic=Ui.Toggle(TextResources.Get("aboutPanel.aboutPanel.toggle"),pet.Settings.AutoUpdate,v=>{pet.Settings.AutoUpdate=v;updates.AutomaticChanged();});automatic.Name="AutoUpdate";automatic.Margin=new Thickness(0,16,0,5);update.Children.Add(automatic);
            panel.Children.Add(GlassSurface.Card(update));
            var network=new Expander{Name="UpdateNetwork",Header=TextResources.Get("aboutPanel.aboutPanel.header"),IsExpanded=false,Margin=new Thickness(0,0,0,18),FontSize=14,Foreground=Ui.Ink};
            var networkBody=new StackPanel{Margin=new Thickness(2,10,0,0)};
            direct=Choice(TextResources.Get("aboutPanel.aboutPanel.choice"),"UpdateDirect","UpdateNetworkMode",!pet.Settings.UpdateUseProxy && !pet.Settings.UpdateUseMirror);networkBody.Children.Add(direct);
            mirror=Choice(TextResources.Get("aboutPanel.aboutPanel.choice.alternativeA"),"UpdateMirror","UpdateNetworkMode",pet.Settings.UpdateUseMirror);var mirrorHeading=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};mirrorHeading.Children.Add(mirror);networkBody.Children.Add(mirrorHeading);
            mirrorOptions=new StackPanel{Margin=new Thickness(24,0,0,10)};
            bool custom=!mirrorPresets.Contains(pet.Settings.UpdateMirrorAddress);
            builtinMirrors=new RadioButton[mirrorPresets.Length];
            foreach(int i in Enumerable.Range(0,mirrorPresets.Length).OrderBy(index=>mirrorPresets[index].Length)){var option=Choice(mirrorPresets[i],i==0?"DefaultUpdateMirror":"BuiltinUpdateMirror"+i,"UpdateMirrorChoice",savedMirror==mirrorPresets[i]);option.Tag=mirrorPresets[i];builtinMirrors[i]=option;AddMirrorRow(option,mirrorPresets[i],i.ToString());}
            customMirror=Choice(TextResources.Get("aboutPanel.aboutPanel.choice.alternativeB"),"CustomUpdateMirror","UpdateMirrorChoice",custom && savedMirror!=null);AddMirrorRow(customMirror,TextResources.Get("aboutPanel.aboutPanel.addMirrorRow"),"Custom");
            mirrorAddress=Ui.Field(custom?pet.Settings.UpdateMirrorAddress:"");mirrorAddress.Name="UpdateMirrorAddress";mirrorAddress.Margin=new Thickness(22,0,0,7);mirrorAddress.LostFocus+=delegate{if(customMirror.IsChecked==true)SaveNetwork();};mirrorOptions.Children.Add(mirrorAddress);mirrorAddress.TextChanged+=delegate{if(!initializing){SetMirrorResult(customMirror,TextResources.Get("aboutPanel.aboutPanel.setMirrorResult"),null);Refresh();}};
            testMirror=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeC"));testMirror.Name="TestUpdateMirror";testMirror.HorizontalAlignment=HorizontalAlignment.Left;testMirror.FontSize=12;testMirror.Margin=new Thickness(10,0,0,0);testMirror.Click+=async delegate{await TestMirrorsAsync();};mirrorHeading.Children.Add(testMirror);
            mirrorOptions.Children.Add(Ui.Text(TextResources.Get("aboutPanel.aboutPanel.text.alternativeD"),11,Ui.Muted));networkBody.Children.Add(mirrorOptions);
            proxy=Choice(TextResources.Get("aboutPanel.aboutPanel.choice.alternativeC"),"UpdateProxy","UpdateNetworkMode",pet.Settings.UpdateUseProxy);networkBody.Children.Add(proxy);
            proxyOptions=new StackPanel{Margin=new Thickness(24,0,0,9)};proxyAddress=Ui.Field(ProxyConfiguration.DisplayAddress(pet.Settings.UpdateProxyAddress));proxyAddress.Name="UpdateProxyAddress";proxyAddress.LostFocus+=delegate{SaveNetwork();};proxyOptions.Children.Add(proxyAddress);proxyOptions.Children.Add(Ui.Text(TextResources.Get("aboutPanel.aboutPanel.text.alternativeE"),11,Ui.Muted));networkBody.Children.Add(proxyOptions);
            networkNotice=Ui.Text("",11,Ui.Muted);networkNotice.Name="UpdateNetworkNotice";networkBody.Children.Add(networkNotice);network.Content=networkBody;panel.Children.Add(network);
            foreach(var option in new[]{direct,mirror,proxy,customMirror}.Concat(builtinMirrors))option.Checked+=delegate{if(!initializing){RefreshNetwork();SaveNetwork();Refresh();}};
            panel.Children.Add(new Border{Height=1,Background=Ui.Line,Margin=new Thickness(0,0,0,15)});panel.Children.Add(Heading(TextResources.Get("aboutPanel.aboutPanel.heading.alternativeA"),14));
            var repository=Ui.Text("",12,Ui.Blue);repository.Name="RepositoryUrl";repository.Margin=new Thickness(0,5,0,10);
            var link=new Hyperlink(new Run(ReleaseInfo.RepositoryUrl)){NavigateUri=new Uri(ReleaseInfo.RepositoryUrl)};link.RequestNavigate+=delegate(object sender,System.Windows.Navigation.RequestNavigateEventArgs args){args.Handled=true;Open(ReleaseInfo.RepositoryUrl);};repository.Inlines.Add(link);panel.Children.Add(repository);
            var links=new WrapPanel();var releases=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeF"));releases.Click+=delegate{Open(ReleaseInfo.RepositoryUrl+"/releases");};links.Children.Add(releases);var feedback=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeG"));feedback.Click+=delegate{Open(ReleaseInfo.RepositoryUrl+"/issues");};links.Children.Add(feedback);
            openFolder=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeH"));openFolder.Name="OpenDownloads";openFolder.Click+=delegate{Open(Path.Combine(pet.Store.DirectoryPath,"updates"));};links.Children.Add(openFolder);panel.Children.Add(links);
            var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};uninstall=Ui.Button(TextResources.Get("aboutPanel.aboutPanel.text.alternativeI"));uninstall.Name="UninstallApplication";uninstall.Foreground=Ui.Brush("#AC4A57");uninstall.Margin=new Thickness(12,0,0,0);uninstall.Click+=delegate{Uninstall();};DockPanel.SetDock(uninstall,Dock.Right);footer.Children.Add(uninstall);
            var cache=Ui.Button(TextResources.Get("cache.title"));cache.Name="ClearCache";cache.Foreground=Ui.Muted;cache.Margin=new Thickness(12,0,0,0);cache.Click+=delegate{var dialog=new CacheMaintenanceWindow(pet.Store.DirectoryPath){Owner=Window.GetWindow(this)};dialog.ShowDialog();};DockPanel.SetDock(cache,Dock.Right);footer.Children.Add(cache);
            footer.Children.Add(Ui.Text(TextResources.Get("aboutPanel.aboutPanel.text.alternativeJ"),11,Ui.Muted));var footerBorder=new Border{Child=footer,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0)};Grid.SetRow(footerBorder,1);Children.Add(footerBorder);
            initializing=false;RefreshNetwork();updates.Changed+=Refresh;Refresh();
        }
        static TextBlock Heading(string text,double size){var label=Ui.Text(text,size);label.FontWeight=FontWeights.SemiBold;return label;}
        static RadioButton Choice(string text,string name,string group,bool selected){return new RadioButton{Name=name,Content=text,GroupName=group,IsChecked=selected,Margin=new Thickness(0,7,0,7),VerticalContentAlignment=VerticalAlignment.Center,Foreground=Ui.Ink,FontSize=12};}
        string SelectedMirror {get{var selected=builtinMirrors.FirstOrDefault(b=>b.IsChecked==true);return selected==null?(customMirror.IsChecked==true?mirrorAddress.Text:""):(string)selected.Tag;}}
        void RefreshNetwork(){mirrorOptions.Visibility=mirror.IsChecked==true?Visibility.Visible:Visibility.Collapsed;proxyOptions.Visibility=proxy.IsChecked==true?Visibility.Visible:Visibility.Collapsed;testMirror.Visibility=mirror.IsChecked==true?Visibility.Visible:Visibility.Collapsed;}
        internal bool ValidateNetwork(out string error)
        {
            try{if(proxy.IsChecked==true)ProxyConfiguration.Endpoint(ProxyConfiguration.Normalize(proxyAddress.Text));if(mirror.IsChecked==true){string address=GitHubUpdates.MirrorPrefix(SelectedMirror);if(!testedMirrors.Contains(address) && address!=savedMirror)throw new InvalidOperationException(TextResources.Get("aboutPanel.validateNetwork.text"));}error="";return true;}
            catch(InvalidOperationException e){error=e.Message;return false;}
        }
        internal bool SaveNetwork()
        {
            if(initializing || disposed)return false;string error;if(!ValidateNetwork(out error)){networkNotice.Text=mirror.IsChecked==true && String.IsNullOrEmpty(SelectedMirror)?TextResources.Get("aboutPanel.saveNetwork.trueStringIsNullOrEmpty"):error;return false;}
            try{string proxyValue=pet.Settings.UpdateProxyAddress,mirrorValue=pet.Settings.UpdateMirrorAddress;if(proxy.IsChecked==true){proxyValue=ProxyConfiguration.DisplayAddress(proxyAddress.Text);ProxyConfiguration.Endpoint(proxyValue);}if(mirror.IsChecked==true)mirrorValue=GitHubUpdates.MirrorPrefix(SelectedMirror);pet.Settings.UpdateUseProxy=proxy.IsChecked==true;pet.Settings.UpdateUseMirror=mirror.IsChecked==true;pet.Settings.UpdateProxyAddress=proxyValue;if(proxy.IsChecked==true)proxyAddress.Text=proxyValue;pet.Settings.UpdateMirrorAddress=mirrorValue;if(mirror.IsChecked==true)savedMirror=mirrorValue;pet.SaveSettings();networkNotice.Text=TextResources.Get("aboutPanel.saveNetwork.networkNotice");return true;}
            catch(InvalidOperationException e){networkNotice.Text=e.Message;updates.ReportValidation(e.Message);return false;}
        }
        void AddMirrorRow(RadioButton option,string address,string suffix)
        {
            var content=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var label=Ui.Text(address,12);label.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(label);
            var state=Ui.Text(option.IsChecked==true?TextResources.Get("aboutPanel.addMirrorRow.text"):TextResources.Get("aboutPanel.addMirrorRow.text.alternativeA"),10,Ui.Muted);state.Name="MirrorStatus"+suffix;
            var badge=new Border{Child=state,CornerRadius=new CornerRadius(9),Background=Ui.Pale,Padding=new Thickness(6,2,6,2),Margin=new Thickness(8,0,0,0)};content.Children.Add(badge);
            var latency=Ui.Text("",10,Ui.Brush("#159EB3"));latency.Name="MirrorLatency"+suffix;
            content.Children.Add(new Border{Child=latency,CornerRadius=new CornerRadius(9),Background=Ui.Brush("#E2F8FB"),Padding=new Thickness(5,2,5,2),Margin=new Thickness(5,0,0,0),Visibility=Visibility.Collapsed});
            option.Content=content;option.Margin=new Thickness(0,9,0,9);mirrorLabels.Add(option,Tuple.Create(state,latency));mirrorOptions.Children.Add(option);
        }
        void SetMirrorResult(RadioButton row,string text,long? milliseconds,bool? available=null)
        {
            var labels=mirrorLabels[row];labels.Item1.Text=text;labels.Item1.Foreground=available==true?Ui.Brush("#00A86B"):available==false?Ui.Brush("#AC4A57"):Ui.Muted;
            ((Border)labels.Item1.Parent).Background=available==true?Ui.Brush("#E0F8EC"):Ui.Pale;
            labels.Item2.Text=milliseconds.HasValue?milliseconds.Value+"ms":"";((Border)labels.Item2.Parent).Visibility=milliseconds.HasValue?Visibility.Visible:Visibility.Collapsed;
        }
        string AddressOf(RadioButton row){return row==customMirror?mirrorAddress.Text.Trim().TrimEnd('/'):(string)row.Tag;}
        internal async Task TestMirrorsAsync()
        {
            if(probe!=null || updates.IsBusy || disposed)return;
            var cancellation=new CancellationTokenSource();probe=cancellation;Refresh();networkNotice.Text=TextResources.Get("aboutPanel.testMirrorsAsync.networkNotice");
            var rows=builtinMirrors.Concat(String.IsNullOrWhiteSpace(mirrorAddress.Text)?new RadioButton[0]:new[]{customMirror}).ToArray();
            foreach(var row in rows){testedMirrors.Remove(AddressOf(row));SetMirrorResult(row,TextResources.Get("aboutPanel.testMirrorsAsync.setMirrorResult"),null);}
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
                            testedMirrors.Add(address);row.ToolTip=null;SetMirrorResult(row,TextResources.Get("aboutPanel.testMirrorsAsync.setMirrorResult.alternativeA"),Math.Max(1,timer.ElapsedMilliseconds),true);
                        }
                        catch(OperationCanceledException){if(!disposed)SetMirrorResult(row,TextResources.Get("aboutPanel.testMirrorsAsync.setMirrorResult.alternativeB"),null);}
                        catch(Exception error){if(!disposed){SetMirrorResult(row,TextResources.Get("aboutPanel.testMirrorsAsync.setMirrorResult.alternativeC"),null,false);row.ToolTip=error is ApiException || error is InvalidOperationException?error.Message:TextResources.Get("aboutPanel.testMirrorsAsync.invalidOperationExceptionErrorMessage");}}
                    }));
                }
                if(!disposed)networkNotice.Text=TextResources.Get("aboutPanel.testMirrorsAsync.networkNotice.alternativeA");
            }
            catch(OperationCanceledException){if(!disposed){foreach(var row in rows)SetMirrorResult(row,TextResources.Get("aboutPanel.testMirrorsAsync.setMirrorResult.alternativeB"),null);networkNotice.Text=TextResources.Get("aboutPanel.testMirrorsAsync.networkNotice.alternativeB");}}
            catch(Exception error){if(!disposed){foreach(var row in rows)SetMirrorResult(row,TextResources.Get("aboutPanel.testMirrorsAsync.setMirrorResult.alternativeD"),null);networkNotice.Text=error is ApiException?error.Message:TextResources.Get("aboutPanel.testMirrorsAsync.apiExceptionErrorMessage");}}
            finally{probe=null;cancellation.Dispose();if(!disposed)Refresh();}
        }
        void Refresh()
        {
            if(disposed)return;channel.IsEnabled=!updates.IsBusy&&probe==null;channelNotice.Text=pet.Settings.UpdateChannel==UpdateChannel.Preview?TextResources.Get("aboutPanel.refresh.updateChannelUpdateChannelPreview"):TextResources.Get("aboutPanel.refresh.updateChannelUpdateChannelPreview.alternativeA");bool networkBusy=updates.IsBusy || probe!=null;check.Content=updates.IsReadyToInstall?TextResources.Get("aboutPanel.refresh.contentUpdatesIsReadyToInstall"):TextResources.Get("aboutPanel.refresh.contentUpdatesIsReadyToInstall.alternativeA");check.IsEnabled=probe==null && (!updates.IsBusy || updates.IsReadyToInstall);cancel.IsEnabled=updates.CanCancel;
            foreach(var option in new[]{direct,mirror,proxy})option.IsEnabled=!networkBusy;foreach(var option in builtinMirrors.Concat(new[]{customMirror})){string address=AddressOf(option);option.IsEnabled=!networkBusy && (testedMirrors.Contains(address) || (option.IsChecked==true && address==savedMirror));}testMirror.Content=probe==null?TextResources.Get("aboutPanel.refresh.contentProbeNull"):TextResources.Get("aboutPanel.refresh.contentProbeNull.alternativeA");proxyAddress.IsEnabled=mirrorAddress.IsEnabled=testMirror.IsEnabled=!networkBusy;
            progress.Visibility=updates.IsBusy?Visibility.Visible:Visibility.Collapsed;progress.IsIndeterminate=updates.IsBusy && !updates.IsDownloading && updates.Progress<100;progress.Value=updates.Progress;
            progressLabel.Text=updates.IsDownloading?TextResources.Format("aboutPanel.refresh.isDownloadingMathRound",Math.Round(updates.Progress)):"";routeLabel.Text=updates.NetworkStatus??TextResources.Get("aboutPanel.refresh.routeLabelUpdatesNetworkStatus");status.Text=updates.Status;openFolder.IsEnabled=Directory.Exists(Path.Combine(pet.Store.DirectoryPath,"updates"));uninstall.IsEnabled=!networkBusy;
        }
        void Uninstall()
        {
            var dialog=new UninstallConfirmationWindow(pet){Owner=Window.GetWindow(this)};dialog.ShowDialog();
        }
        void Open(string address){try{Process.Start(new ProcessStartInfo(address){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){updates.ReportValidation(TextResources.Get("aboutPanel.open.updatesReportValidation"));}}
        public void Dispose(){if(disposed)return;disposed=true;if(probe!=null)probe.Cancel();updates.Changed-=Refresh;}
    }
}
