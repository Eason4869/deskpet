using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Dafeiyu.ImChannels;

namespace Dafeiyu
{
    public sealed class ImChannelSettingsPanel:StackPanel,IDisposable
    {
        readonly PetWindow pet;readonly ImChannelManager manager;readonly ComboBox profiles,platform;
        readonly TextBox name,appId,users,groups;readonly PasswordBox secret;readonly TextBlock state,notice,companion;readonly Image qr;
        readonly WrapPanel actions;ImChannelProfile draft;bool loading,busy,disposed;int refreshQueued;CancellationTokenSource login;
        readonly StackPanel overview=new StackPanel{Name="ImChannelOverview"},details=new StackPanel{Name="ImChannelDetails",Visibility=Visibility.Collapsed};
        readonly StackPanel platformCards=new StackPanel();readonly Button scan;readonly FrameworkElement scanActions;
        readonly ContentControl detailLogo=new ContentControl{Margin=new Thickness(0,0,12,0)};readonly TextBlock detailTitle;
        readonly StackPanel manualFields=new StackPanel();readonly RadioButton scanMode,manualMode;Window connectionDialog;
        readonly Grid profilePicker;
        internal Func<ImChannelProfile,Action<string>,CancellationToken,Task<ImChannelProfile>> RegistrationOverride;
        public bool HasUnsavedChanges{get{return draft!=null&&!busy&&(name.Text!=draft.Name||appId.Text!=draft.AppId||secret.Password!=draft.Secret||String.Join("\n",draft.Users)!=users.Text||String.Join("\n",draft.Chats)!=groups.Text||ImChannelProfile.Kinds[Math.Max(0,platform.SelectedIndex)]!=draft.Kind);}}
        public ImChannelSettingsPanel(PetWindow owner)
        {
            pet=owner;manager=pet.ImChannels;Margin=new Thickness(24,22,24,20);
            Children.Add(overview);Children.Add(details);
            overview.Children.Add(Ui.PageHeading(ImText.Get("im.title")));LocalizedUi.Set((TextBlock)overview.Children[0],"Text",()=>ImText.Get("im.title"));
            var intro=LocalizedUi.Text(()=>ImText.Get("im.subtitle"),12,Ui.Muted);intro.Margin=new Thickness(0,8,0,24);overview.Children.Add(intro);overview.Children.Add(platformCards);
            var back=LocalizedUi.Button(()=>ImText.Get("common.cancel"));back.Name="ImChannelsBack";back.Visibility=Visibility.Collapsed;back.Click+=delegate{if(connectionDialog!=null)connectionDialog.Close();};details.Children.Add(back);
            var identity=new StackPanel{Orientation=Orientation.Horizontal};identity.Children.Add(detailLogo);detailTitle=Ui.PageHeading("");identity.Children.Add(detailTitle);details.Children.Add(identity);details.Children.Add(LocalizedUi.Text(()=>ImText.Get("im.subtitle"),12,Ui.Muted));
            var profileRow=new Grid{Margin=new Thickness(0,18,0,8)};profilePicker=profileRow;profileRow.ColumnDefinitions.Add(new ColumnDefinition());profileRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});details.Children.Add(profileRow);
            profiles=new ComboBox{Name="ImChannelList",MinHeight=36};profileRow.Children.Add(profiles);
            var add=LocalizedUi.Button(()=>ImText.Get("im.add"));add.Name="ImChannelAdd";add.Margin=new Thickness(10,0,0,0);add.Click+=delegate{if(busy)return;if(HasUnsavedChanges){notice.Text=ImText.Get("im.unsaved");return;}New();};Grid.SetColumn(add,1);profileRow.Children.Add(add);
            platform=new ComboBox{Name="ImPlatform",ItemsSource=ImChannelProfile.Kinds,SelectedIndex=0,MinHeight=36};LocalizedUi.Options(platform,()=>ImChannelProfile.Kinds.Select(kind=>ImText.Get("im.platform."+kind)));
            name=Field("ImChannelName");appId=Field("ImAppId");
            secret=new PasswordBox{Name="ImSecret",MinHeight=36,Padding=new Thickness(8),MaxLength=8192,Background=ThemeManager.FieldSurface,BorderBrush=Ui.Line};
            platform.Visibility=Visibility.Collapsed;details.Children.Add(platform);LocalizedUi.Label(details,()=>ImText.Get("im.name"),name);
            AddFieldPair("im.appId",appId,"im.secret",secret);
            companion=LocalizedUi.Text(()=>ImText.Get("im.binding"),12,Ui.Muted);details.Children.Add(companion);
            users=Field("ImAllowedUsers");users.AcceptsReturn=true;users.MinHeight=66;
            groups=Field("ImAllowedGroups");groups.AcceptsReturn=true;groups.MinHeight=66;var access=new StackPanel{Margin=new Thickness(0,12,0,0)};LocalizedUi.Label(access,()=>ImText.Get("im.users"),users);LocalizedUi.Label(access,()=>ImText.Get("im.groups"),groups);access.Children.Add(LocalizedUi.Text(()=>ImText.Get("im.allowHint"),12,Ui.Muted));var accessOptions=new Expander{Name="ImAccessOptions",Content=access,IsExpanded=false,Margin=new Thickness(0,16,0,0)};LocalizedUi.Set(accessOptions,"Header",()=>TextResources.Get("im.accessOptions"));details.Children.Add(accessOptions);
            qr=new Image{Name="ImLoginQr",Width=240,Height=240,HorizontalAlignment=HorizontalAlignment.Left,Visibility=Visibility.Collapsed,Margin=new Thickness(0,12,0,8)};details.Children.Add(qr);
            actions=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,16,0,12)};details.Children.Add(actions);
            ButtonAction("im.save","ImSave",async delegate{await Save();});
            ButtonAction("im.connect","ImConnect",async delegate{var value=Read();value.Enabled=false;await manager.SaveAsync(value);await manager.StartAsync(value.Id);Reload(value.Id);});
            ButtonAction("im.pause","ImPause",async delegate{if(draft!=null)await manager.PauseAsync(draft.Id);Reload(draft.Id);});
            ButtonAction("im.delete","ImDelete",async delegate{if(draft!=null)await manager.DeleteAsync(draft.Id);Reload(null);});
            var sign=LocalizedUi.Button(()=>TextResources.Get("im.scanConnect"),true);scan=sign;sign.Name="ImChannelScan";sign.Click+=async delegate{await Run(ScanAsync);};var loginActions=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};scanActions=loginActions;details.Children.Add(loginActions);sign.Margin=new Thickness(0,0,8,8);loginActions.Children.Add(sign);var cancel=LocalizedUi.Button(()=>ImText.Get("im.cancelLogin"));cancel.Margin=new Thickness(0,0,8,8);cancel.Click+=delegate{if(login!=null)login.Cancel();};loginActions.Children.Add(cancel);
            var website=LocalizedUi.Button(()=>ImText.Get("im.openPlatform"));website.Name="ImOpenPlatform";website.Margin=new Thickness(0,0,8,8);website.Click+=delegate{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ImChannelProfile.PlatformSite((string)platform.SelectedItem)){UseShellExecute=true});}catch{notice.Text=ImText.Get("im.failed");}};loginActions.Children.Add(website);
            state=Ui.Text("",12,Ui.Muted);state.Name="ImChannelStatus";details.Children.Add(state);notice=Ui.Text("",12,Ui.Muted);notice.Name="ImChannelNotice";details.Children.Add(notice);
            details.Children.Add(LocalizedUi.Text(()=>ImText.Get("im.localOnly"),12,Ui.Muted));
            profiles.SelectionChanged+=delegate{if(loading||busy||profiles.SelectedItem==null)return;if(HasUnsavedChanges){loading=true;profiles.SelectedItem=((System.Collections.Generic.IEnumerable<ImChannelProfile>)profiles.ItemsSource).FirstOrDefault(p=>p.Id==draft.Id);loading=false;notice.Text=ImText.Get("im.unsaved");return;}Load((ImChannelProfile)profiles.SelectedItem);};
            details.Children.Remove(qr);details.Children.Remove(loginActions);var scanSection=new StackPanel{Margin=new Thickness(0,18,0,8)};scanSection.Children.Add(LocalizedUi.Text(()=>TextResources.Get("im.scanHelp"),12,Ui.Muted));loginActions.Margin=new Thickness(0,12,0,0);scanSection.Children.Add(loginActions);scanSection.Children.Add(qr);scanActions=scanSection;details.Children.Insert(3,scanSection);
            var inputs=details.Children.Cast<UIElement>().SkipWhile(e=>e!=profileRow).TakeWhile(e=>e!=state).ToArray();foreach(var input in inputs){details.Children.Remove(input);manualFields.Children.Add(input);}details.Children.Insert(4,manualFields);
            var modes=new WrapPanel{Margin=new Thickness(0,20,0,0)};scanMode=new RadioButton{Name="ImScanMode",GroupName="ImConnectionMethod",IsChecked=true,Margin=new Thickness(0,0,24,0)};manualMode=new RadioButton{Name="ImManualMode",GroupName="ImConnectionMethod"};LocalizedUi.Set(scanMode,"Content",()=>TextResources.Get("im.scanConnect"));LocalizedUi.Set(manualMode,"Content",()=>TextResources.Get("im.manualConnect"));modes.Children.Add(scanMode);modes.Children.Add(manualMode);details.Children.Insert(3,modes);scanMode.Checked+=delegate{SetConnectionMode();};manualMode.Checked+=delegate{SetConnectionMode();};SetConnectionMode();
            platform.SelectionChanged+=delegate{if(draft!=null&&(string)platform.SelectedItem!=draft.Kind){appId.Text="";secret.Password="";}RefreshScan();};manager.Changed+=Changed;Reload(null);RenderOverview();
        }
        void ShowLoginQr(string value,CancellationTokenSource attempt)
        {
            byte[] bytes=ImLoginQr.Png(value);Dispatcher.Invoke(new Action(()=>{if(disposed||attempt.IsCancellationRequested)return;using(var stream=new MemoryStream(bytes)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();qr.Source=bitmap;qr.Visibility=Visibility.Visible;}}));
        }
        void RenderOverview()
        {
            platformCards.Children.Clear();foreach(string kind in ImChannelProfile.Kinds){string current=kind;var list=manager.Profiles.Where(p=>p.Kind==current).ToArray();bool connected=list.Any(p=>manager.State(p.Id)=="im.connected");
                var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(54)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                row.Children.Add(new Border{Width=40,Height=40,CornerRadius=new CornerRadius(12),Background=Ui.Brush("#F8F9FB"),BorderBrush=Ui.Brush("#E3E7EC"),BorderThickness=new Thickness(1),Child=ImChannelLogos.Image(current,24),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left});
                var body=new StackPanel();var title=new WrapPanel();title.Children.Add(LocalizedUi.InlineText(()=>ImText.Get("im.platform."+current),15,Ui.PageInk));var stateLabel=LocalizedUi.InlineText(()=>list.Length==0?TextResources.Get("im.notConfigured"):ImText.Get(connected?"im.connected":manager.State(list[0].Id)),11,connected?Ui.Brush("#20834C"):Ui.Muted);stateLabel.Margin=new Thickness(12,2,0,0);title.Children.Add(stateLabel);body.Children.Add(title);var description=LocalizedUi.Text(()=>TextResources.Format("im.cardDescription",ImText.Get("im.platform."+current)),12,Ui.Muted);description.Margin=new Thickness(0,7,0,0);body.Children.Add(description);if(list.Length>0){var saved=Ui.InlineText(String.Join(" · ",list.Select(p=>p.Name)),11,Ui.Muted);saved.Margin=new Thickness(0,5,0,0);body.Children.Add(saved);}Grid.SetColumn(body,1);row.Children.Add(body);var arrow=Ui.InlineText("›",24,Ui.Muted);arrow.Margin=new Thickness(16,0,0,0);Grid.SetColumn(arrow,2);row.Children.Add(arrow);
                var button=Ui.Button("");button.Name="ImPlatformCard_"+current;button.Template=ChannelCardTemplate();button.Content=row;button.Background=ThemeManager.Surface;button.BorderBrush=Ui.Brush("#E3E7EC");button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(18,17,18,17);button.Margin=new Thickness(0,0,0,12);button.Click+=delegate{OpenPlatform(current);};platformCards.Children.Add(button);
            }
        }
        static ControlTemplate cardTemplate;
        static ControlTemplate ChannelCardTemplate()
        {
            return cardTemplate??(cardTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='card' CornerRadius='12' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='card' Property='Background' Value='#F3F5F8'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='card' Property='BorderBrush' Value='#456DE6'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='card' Property='Background' Value='#E8EBF0'/></Trigger></ControlTemplate.Triggers></ControlTemplate>"));
        }
        void OpenPlatform(string kind)
        {
            if(connectionDialog!=null){connectionDialog.Activate();return;}
            if(busy)return;if(HasUnsavedChanges&&draft.Kind!=kind){kind=draft.Kind;notice.Text=ImText.Get("im.unsaved");}
            if(!HasUnsavedChanges){var selected=manager.Profiles.FirstOrDefault(p=>p.Kind==kind);if(selected==null)selected=new ImChannelProfile{Kind=kind,PetId=pet.Skin.Id,PetName=pet.Skin.Name,Persona=pet.Settings.SystemPrompt,Name=pet.Skin.Name};Load(selected);loading=true;profiles.SelectedItem=manager.Profiles.FirstOrDefault(p=>p.Id==selected.Id);loading=false;}
            details.Visibility=Visibility.Visible;RefreshScan();var owner=Window.GetWindow(this);Children.Remove(details);details.Margin=new Thickness(24);var dialog=new Window{Name="ImConnectionDialog"};Ui.Window(dialog,ImText.Get("im.platform."+kind),680,680);dialog.MinWidth=500;dialog.MinHeight=460;dialog.ResizeMode=ResizeMode.CanMinimize;dialog.Owner=owner;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;dialog.Content=Ui.Scroll(details);connectionDialog=dialog;scanMode.IsChecked=true;SetConnectionMode();dialog.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(disposed)return;if(busy&&login==null){e.Cancel=true;return;}if(login!=null)login.Cancel();};
            dialog.Closed+=delegate{var host=dialog.Content as ScrollViewer;if(host!=null)host.Content=null;dialog.Content=null;connectionDialog=null;details.Margin=new Thickness(0);details.Visibility=Visibility.Collapsed;Children.Add(details);RenderOverview();};dialog.Loaded+=async delegate{if(!disposed&&scanMode.IsChecked==true)await Run(ScanAsync);};dialog.Show();
        }
        void SetConnectionMode(){if(scanActions==null||scanMode==null)return;bool scanning=scanMode.IsChecked==true;if(!scanning&&login!=null)login.Cancel();scanActions.Visibility=scanning?Visibility.Visible:Visibility.Collapsed;manualFields.Visibility=scanning?Visibility.Collapsed:Visibility.Visible;}
        void RefreshScan(){SetConnectionMode();if(platform.SelectedItem!=null){string kind=(string)platform.SelectedItem;detailLogo.Content=ImChannelLogos.Image(kind,30);LocalizedUi.Set(detailTitle,"Text",()=>ImText.Get("im.platform."+kind));}}
        async Task ScanAsync()
        {
            var snapshot=Read();snapshot.Enabled=false;var attempt=new CancellationTokenSource();login=attempt;state.Visibility=Visibility.Collapsed;notice.Text=ImText.Get("im.connecting");
            try{Action<string> show=value=>ShowLoginQr(value,attempt);if(RegistrationOverride!=null)snapshot=await RegistrationOverride(snapshot,show,attempt.Token);else switch(snapshot.Kind){
                case "weixin":using(var adapter=new WeixinAdapter(snapshot))snapshot=await adapter.LoginAsync(show,attempt.Token);break;
                case "feishu":using(var registration=new FeishuRegistration(snapshot))snapshot=await registration.LoginAsync(show,attempt.Token);break;
                case "qq":using(var registration=new QqRegistration(snapshot))snapshot=await registration.LoginAsync(show,attempt.Token);break;
                case "dingtalk":using(var registration=new DingTalkRegistration(snapshot))snapshot=await registration.LoginAsync(show,attempt.Token);break;
                case "wecom":using(var registration=new WecomRegistration(snapshot))snapshot=await registration.LoginAsync(show,attempt.Token);break;
                default:throw new InvalidOperationException("im.platformFailed");
            }attempt.Token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();await manager.SaveAsync(snapshot,attempt.Token);attempt.Token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();await manager.StartAsync(snapshot.Id,attempt.Token);Reload(snapshot.Id);manualMode.IsChecked=true;}
            finally{if(login==attempt)login=null;attempt.Dispose();qr.Source=null;qr.Visibility=Visibility.Collapsed;state.Visibility=Visibility.Visible;}
        }
        TextBox Field(string id){var field=Ui.Field();field.Name=id;field.MaxLength=16000;return field;}
        void AddFieldPair(string firstKey,UIElement first,string secondKey,UIElement second)
        {
            var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var left=new StackPanel();var right=new StackPanel();LocalizedUi.Label(left,()=>ImText.Get(firstKey),first);LocalizedUi.Label(right,()=>ImText.Get(secondKey),second);grid.Children.Add(left);grid.Children.Add(right);details.Children.Add(grid);
            Action layout=delegate{bool narrow=grid.ActualWidth<640;grid.ColumnDefinitions[1].Width=new GridLength(narrow?0:16);grid.ColumnDefinitions[2].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star);Grid.SetColumn(right,narrow?0:2);Grid.SetRow(right,narrow?1:0);};grid.SizeChanged+=delegate{layout();};grid.Loaded+=delegate{layout();};
        }
        void ButtonAction(string key,string id,Func<Task> action){var button=LocalizedUi.Button(()=>ImText.Get(key));button.Name=id;button.Margin=new Thickness(0,0,8,8);button.Click+=async delegate{await Run(action);};actions.Children.Add(button);}
        async Task Run(Func<Task> action){if(busy||disposed)return;busy=true;scan.IsEnabled=false;actions.IsEnabled=false;profiles.IsEnabled=false;platform.IsEnabled=false;name.IsEnabled=appId.IsEnabled=secret.IsEnabled=users.IsEnabled=groups.IsEnabled=false;try{await action();notice.Text="";}catch(Exception error){if(!disposed)notice.Text=error is OperationCanceledException&&manualMode.IsChecked==true?"":ImText.Get(error is InvalidOperationException&&error.Message.StartsWith("im.",StringComparison.Ordinal)?error.Message:error is OperationCanceledException?"im.paused":"im.failed");}finally{busy=false;if(!disposed){scan.IsEnabled=true;actions.IsEnabled=true;profiles.IsEnabled=true;platform.IsEnabled=true;name.IsEnabled=appId.IsEnabled=secret.IsEnabled=users.IsEnabled=groups.IsEnabled=true;Refresh();}}}
        ImChannelProfile Read(){var copy=draft.Copy();copy.Kind=(string)platform.SelectedItem;if(copy.Kind!=draft.Kind)copy.ApiBase="";copy.Name=name.Text.Trim();copy.AppId=appId.Text.Trim();copy.Secret=secret.Password;copy.Users=Lines(users.Text);copy.Chats=Lines(groups.Text);copy.Validate();return copy;}
        static System.Collections.Generic.List<string> Lines(string text){return text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList();}
        async Task Save(){var value=Read();await manager.SaveAsync(value);Reload(value.Id);}
        void New(){var value=new ImChannelProfile{Kind=(string)platform.SelectedItem??"qq",PetId=pet.Skin.Id,PetName=pet.Skin.Name,Persona=pet.Settings.SystemPrompt,Name=pet.Skin.Name};Load(value);}
        void Load(ImChannelProfile profile){draft=profile.Copy();platform.SelectedItem=draft.Kind;name.Text=draft.Name;appId.Text=draft.AppId;secret.Password=draft.Secret;users.Text=String.Join("\n",draft.Users);groups.Text=String.Join("\n",draft.Chats);LocalizedUi.Set(companion,"Text",()=>ImText.Get("im.binding")+" "+draft.PetName);Refresh();}
        void Reload(string id){loading=true;var list=manager.Profiles;profiles.ItemsSource=list;LocalizedUi.Options(profiles,()=>list.Select(p=>p.ToString()));profiles.SelectedItem=list.FirstOrDefault(p=>p.Id==id);loading=false;var value=manager.Profiles.FirstOrDefault(p=>p.Id==id);if(value!=null)Load(value);else New();}
        void Changed(){if(!disposed&&Interlocked.Exchange(ref refreshQueued,1)==0)Dispatcher.BeginInvoke(new Action(()=>{Interlocked.Exchange(ref refreshQueued,0);if(!disposed)Refresh();}));}
        void Refresh(){if(draft==null)return;bool saved=manager.Profiles.Any(p=>p.Id==draft.Id);profilePicker.Visibility=manager.Profiles.Count>0?Visibility.Visible:Visibility.Collapsed;foreach(Button action in actions.Children)if(action.Name=="ImPause"||action.Name=="ImDelete")action.Visibility=saved?Visibility.Visible:Visibility.Collapsed;state.Visibility=saved?Visibility.Visible:Visibility.Collapsed;LocalizedUi.Set(state,"Text",()=>ImText.Get(manager.State(draft.Id))+"\n"+ImText.Get("im.lastSender")+" "+manager.LastSender(draft.Id));if(manager.Warning.Length>0)notice.Text=ImText.Get(manager.Warning);RenderOverview();}
        public void Dispose(){disposed=true;manager.Changed-=Changed;if(login!=null)login.Cancel();if(connectionDialog!=null)connectionDialog.Close();qr.Source=null;}
    }
}
