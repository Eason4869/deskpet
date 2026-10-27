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
        public bool HasUnsavedChanges{get{return draft!=null&&!busy&&(name.Text!=draft.Name||appId.Text!=draft.AppId||secret.Password!=draft.Secret||String.Join("\n",draft.Users)!=users.Text||String.Join("\n",draft.Chats)!=groups.Text||ImChannelProfile.Kinds[Math.Max(0,platform.SelectedIndex)]!=draft.Kind);}}
        public ImChannelSettingsPanel(PetWindow owner)
        {
            pet=owner;manager=pet.ImChannels;Margin=new Thickness(24,22,24,20);
            Children.Add(LocalizedUi.Text(()=>ImText.Get("im.title"),22,Ui.PageInk));Children.Add(LocalizedUi.Text(()=>ImText.Get("im.subtitle"),12,Ui.Muted));
            profiles=new ComboBox{Name="ImChannelList",MinHeight=36,Margin=new Thickness(0,18,0,8)};Children.Add(profiles);
            var add=LocalizedUi.Button(()=>ImText.Get("im.add"));add.Name="ImChannelAdd";add.Click+=delegate{if(busy)return;if(HasUnsavedChanges){notice.Text=ImText.Get("im.unsaved");return;}New();};Children.Add(add);
            platform=new ComboBox{Name="ImPlatform",ItemsSource=ImChannelProfile.Kinds,SelectedIndex=0,MinHeight=36};LocalizedUi.Options(platform,()=>ImChannelProfile.Kinds.Select(kind=>ImText.Get("im.platform."+kind)));Label("im.platform",platform);
            name=Field("ImChannelName");Label("im.name",name);appId=Field("ImAppId");Label("im.appId",appId);
            secret=new PasswordBox{Name="ImSecret",MinHeight=36,Padding=new Thickness(8),MaxLength=8192};Label("im.secret",secret);
            companion=LocalizedUi.Text(()=>ImText.Get("im.binding"),12,Ui.Muted);Children.Add(companion);
            users=Field("ImAllowedUsers");users.AcceptsReturn=true;users.MinHeight=66;Label("im.users",users);
            groups=Field("ImAllowedGroups");groups.AcceptsReturn=true;groups.MinHeight=54;Label("im.groups",groups);Children.Add(LocalizedUi.Text(()=>ImText.Get("im.allowHint"),12,Ui.Muted));
            qr=new Image{Name="ImLoginQr",Width=240,Height=240,HorizontalAlignment=HorizontalAlignment.Left,Visibility=Visibility.Collapsed,Margin=new Thickness(0,12,0,8)};Children.Add(qr);
            actions=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,16,0,12)};Children.Add(actions);
            ButtonAction("im.save","ImSave",async delegate{await Save();});
            ButtonAction("im.connect","ImConnect",async delegate{var value=Read();value.Enabled=false;await manager.SaveAsync(value);await manager.StartAsync(value.Id);Reload(value.Id);});
            ButtonAction("im.pause","ImPause",async delegate{if(draft!=null)await manager.PauseAsync(draft.Id);Reload(draft.Id);});
            ButtonAction("im.delete","ImDelete",async delegate{if(draft!=null)await manager.DeleteAsync(draft.Id);Reload(null);});
            var sign=LocalizedUi.Button(()=>ImText.Get("im.login"));sign.Name="ImWeixinLogin";sign.Click+=async delegate{await Run(async delegate{
                if((string)platform.SelectedItem!="weixin")throw new InvalidOperationException("im.selectWeixin");var snapshot=Read();var attempt=new CancellationTokenSource();login=attempt;
                try{using(var adapter=new WeixinAdapter(snapshot)){
                    snapshot=await adapter.LoginAsync(value=>ShowLoginQr(value,attempt),attempt.Token);
                    attempt.Token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();await manager.SaveAsync(snapshot,attempt.Token);Reload(snapshot.Id);
                }}finally{if(login==attempt)login=null;attempt.Dispose();qr.Source=null;qr.Visibility=Visibility.Collapsed;}
            });};Children.Add(sign);var cancel=LocalizedUi.Button(()=>ImText.Get("im.cancelLogin"));cancel.Click+=delegate{if(login!=null)login.Cancel();};Children.Add(cancel);
            var website=LocalizedUi.Button(()=>ImText.Get("im.openPlatform"));website.Name="ImOpenPlatform";website.Click+=delegate{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ImChannelProfile.PlatformSite((string)platform.SelectedItem)){UseShellExecute=true});}catch{notice.Text=ImText.Get("im.failed");}};Children.Add(website);
            state=Ui.Text("",12,Ui.Muted);state.Name="ImChannelStatus";Children.Add(state);notice=Ui.Text("",12,Ui.Muted);notice.Name="ImChannelNotice";Children.Add(notice);
            Children.Add(LocalizedUi.Text(()=>ImText.Get("im.localOnly"),12,Ui.Muted));
            profiles.SelectionChanged+=delegate{if(loading||busy||profiles.SelectedItem==null)return;if(HasUnsavedChanges){loading=true;profiles.SelectedItem=((System.Collections.Generic.IEnumerable<ImChannelProfile>)profiles.ItemsSource).FirstOrDefault(p=>p.Id==draft.Id);loading=false;notice.Text=ImText.Get("im.unsaved");return;}Load((ImChannelProfile)profiles.SelectedItem);};
            platform.SelectionChanged+=delegate{if(draft!=null&&(string)platform.SelectedItem!=draft.Kind){appId.Text="";secret.Password="";}};manager.Changed+=Changed;Reload(null);
        }
        void ShowLoginQr(string value,CancellationTokenSource attempt)
        {
            byte[] bytes=ImLoginQr.Png(value);Dispatcher.Invoke(new Action(()=>{if(disposed||attempt.IsCancellationRequested)return;using(var stream=new MemoryStream(bytes)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();qr.Source=bitmap;qr.Visibility=Visibility.Visible;}}));
        }
        TextBox Field(string id){var field=Ui.Field();field.Name=id;field.MaxLength=16000;return field;}
        void Label(string key,UIElement field){LocalizedUi.Label(this,()=>ImText.Get(key),field);}
        void ButtonAction(string key,string id,Func<Task> action){var button=LocalizedUi.Button(()=>ImText.Get(key));button.Name=id;button.Click+=async delegate{await Run(action);};actions.Children.Add(button);}
        async Task Run(Func<Task> action){if(busy||disposed)return;busy=true;actions.IsEnabled=false;profiles.IsEnabled=false;platform.IsEnabled=false;name.IsEnabled=appId.IsEnabled=secret.IsEnabled=users.IsEnabled=groups.IsEnabled=false;try{await action();notice.Text="";}catch(Exception error){if(!disposed)notice.Text=ImText.Get(error is InvalidOperationException&&error.Message.StartsWith("im.",StringComparison.Ordinal)?error.Message:error is OperationCanceledException?"im.paused":"im.failed");}finally{busy=false;if(!disposed){actions.IsEnabled=true;profiles.IsEnabled=true;platform.IsEnabled=true;name.IsEnabled=appId.IsEnabled=secret.IsEnabled=users.IsEnabled=groups.IsEnabled=true;Refresh();}}}
        ImChannelProfile Read(){var copy=draft.Copy();copy.Kind=(string)platform.SelectedItem;if(copy.Kind!=draft.Kind)copy.ApiBase="";copy.Name=name.Text.Trim();copy.AppId=appId.Text.Trim();copy.Secret=secret.Password;copy.Users=Lines(users.Text);copy.Chats=Lines(groups.Text);copy.Validate();return copy;}
        static System.Collections.Generic.List<string> Lines(string text){return text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList();}
        async Task Save(){var value=Read();await manager.SaveAsync(value);Reload(value.Id);}
        void New(){var value=new ImChannelProfile{PetId=pet.Skin.Id,PetName=pet.Skin.Name,Persona=pet.Settings.SystemPrompt,Name=pet.Skin.Name};Load(value);}
        void Load(ImChannelProfile profile){draft=profile.Copy();platform.SelectedItem=draft.Kind;name.Text=draft.Name;appId.Text=draft.AppId;secret.Password=draft.Secret;users.Text=String.Join("\n",draft.Users);groups.Text=String.Join("\n",draft.Chats);LocalizedUi.Set(companion,"Text",()=>ImText.Get("im.binding")+" "+draft.PetName);Refresh();}
        void Reload(string id){loading=true;var list=manager.Profiles;profiles.ItemsSource=list;LocalizedUi.Options(profiles,()=>list.Select(p=>p.ToString()));profiles.SelectedItem=list.FirstOrDefault(p=>p.Id==id);loading=false;var value=manager.Profiles.FirstOrDefault(p=>p.Id==id);if(value!=null)Load(value);else New();}
        void Changed(){if(!disposed&&Interlocked.Exchange(ref refreshQueued,1)==0)Dispatcher.BeginInvoke(new Action(()=>{Interlocked.Exchange(ref refreshQueued,0);if(!disposed)Refresh();}));}
        void Refresh(){if(draft==null)return;LocalizedUi.Set(state,"Text",()=>ImText.Get(manager.State(draft.Id))+"\n"+ImText.Get("im.lastSender")+" "+manager.LastSender(draft.Id));if(manager.Warning.Length>0)notice.Text=ImText.Get(manager.Warning);}
        public void Dispose(){disposed=true;manager.Changed-=Changed;if(login!=null)login.Cancel();qr.Source=null;}
    }
}
