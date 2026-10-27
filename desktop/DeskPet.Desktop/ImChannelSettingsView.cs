using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Dafeiyu;
using Dafeiyu.ImChannels;

namespace DeskPet.Desktop;

internal sealed class ImChannelSettingsView : StackPanel, IDisposable
{
    readonly DesktopController owner;readonly ImChannelManager manager;
    readonly ComboBox profiles=new(){Name="ImChannelList",MinHeight=36},platform=new(){Name="ImPlatform",ItemsSource=ImChannelProfile.Kinds,SelectedIndex=0,MinHeight=36};
    readonly TextBox name=new(){Name="ImChannelName"},appId=new(){Name="ImAppId"},secret=new(){Name="ImSecret",PasswordChar='●',MaxLength=8192},users=new(){Name="ImAllowedUsers",AcceptsReturn=true,MinHeight=66},groups=new(){Name="ImAllowedGroups",AcceptsReturn=true,MinHeight=54};
    readonly TextBlock status=new(){Name="ImChannelStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap},notice=new(){Name="ImChannelNotice",TextWrapping=Avalonia.Media.TextWrapping.Wrap},companion=new();
    readonly WrapPanel actions=new(){Orientation=Orientation.Horizontal};readonly Image qr=new(){Name="ImLoginQr",Width=240,Height=240,IsVisible=false,HorizontalAlignment=HorizontalAlignment.Left};
    ImChannelProfile draft=null!;bool loading,busy,disposed;int refreshQueued;CancellationTokenSource? login;
    public bool HasUnsavedChanges=>draft!=null&&!busy&&(name.Text!=draft.Name||appId.Text!=draft.AppId||secret.Text!=draft.Secret||String.Join("\n",draft.Users)!=(users.Text??"")||String.Join("\n",draft.Chats)!=(groups.Text??"")||(string?)platform.SelectedItem!=draft.Kind);
    internal ImChannelSettingsView(DesktopController controller)
    {
        owner=controller;manager=owner.ImChannels;Spacing=10;
        Children.Add(SettingsVisual.Text(()=>TextResources.Get("im.title"),22));Children.Add(SettingsVisual.Text(()=>TextResources.Get("im.subtitle"),12));Children.Add(profiles);
        var add=LocalizedUi.Button(()=>TextResources.Get("im.add"),()=>{if(busy)return;if(HasUnsavedChanges){notice.Text=TextResources.Get("im.unsaved");return;}New();});add.Name="ImChannelAdd";Children.Add(add);
        Label("im.platform",platform);Label("im.name",name);Label("im.appId",appId);Label("im.secret",secret);Children.Add(companion);Label("im.users",users);Label("im.groups",groups);Children.Add(SettingsVisual.Text(()=>TextResources.Get("im.allowHint"),12));Children.Add(qr);Children.Add(actions);
        Action("im.save","ImSave",Save);Action("im.connect","ImConnect",async()=>{var value=Read();value.Enabled=false;await manager.SaveAsync(value);await manager.StartAsync(value.Id);Reload(value.Id);});Action("im.pause","ImPause",async()=>{await manager.PauseAsync(draft.Id);Reload(draft.Id);});Action("im.delete","ImDelete",async()=>{await manager.DeleteAsync(draft.Id);Reload(null);});
        var sign=LocalizedUi.Button(()=>TextResources.Get("im.login"),()=>{_ = Run(async()=>{
            if((string?)platform.SelectedItem!="weixin")throw new InvalidOperationException("im.selectWeixin");var snapshot=Read();login=new CancellationTokenSource();
            try{using var adapter=new WeixinAdapter(snapshot);snapshot=await adapter.LoginAsync(value=>Dispatcher.UIThread.Post(()=>{if(!disposed&&login!=null&&!login.IsCancellationRequested){ClearQr();using var stream=new MemoryStream(ImLoginQr.Png(value));qr.Source=new Avalonia.Media.Imaging.Bitmap(stream);qr.IsVisible=true;}}),login.Token);
                if(disposed)return;await manager.SaveAsync(snapshot);Reload(snapshot.Id);
            }finally{login.Dispose();login=null;ClearQr();}
        });});sign.Name="ImWeixinLogin";Children.Add(sign);
        Children.Add(LocalizedUi.Button(()=>TextResources.Get("im.cancelLogin"),()=>login?.Cancel()));Children.Add(status);Children.Add(notice);Children.Add(SettingsVisual.Text(()=>TextResources.Get("im.localOnly"),12));
        profiles.SelectionChanged+=(_,_)=>{if(loading||busy||profiles.SelectedItem is not ImChannelProfile p)return;if(HasUnsavedChanges){loading=true;profiles.SelectedItem=(profiles.ItemsSource as IEnumerable<ImChannelProfile>)?.FirstOrDefault(x=>x.Id==draft.Id);loading=false;notice.Text=TextResources.Get("im.unsaved");return;}Load(p);};platform.SelectionChanged+=(_,_)=>{if(draft!=null&&(string?)platform.SelectedItem!=draft.Kind){appId.Text="";secret.Text="";}};manager.Changed+=Changed;Reload(null);
    }
    void Label(string key,Control control){Children.Add(SettingsVisual.Text(()=>TextResources.Get(key),12));control.MinHeight=Math.Max(control.MinHeight,36);Children.Add(control);}
    void Action(string key,string id,Func<Task> action){var button=LocalizedUi.Button(()=>TextResources.Get(key),()=>{_ = Run(action);});button.Name=id;actions.Children.Add(button);}
    async Task Run(Func<Task> action){if(busy||disposed)return;busy=true;SetEnabled(false);try{await action();notice.Text="";}catch(Exception error){if(!disposed)notice.Text=TextResources.Get(error is InvalidOperationException&&error.Message.StartsWith("im.",StringComparison.Ordinal)?error.Message:error is OperationCanceledException?"im.paused":"im.failed");}finally{busy=false;if(!disposed){SetEnabled(true);Refresh();}}}
    void SetEnabled(bool enabled){actions.IsEnabled=profiles.IsEnabled=platform.IsEnabled=enabled;foreach(var field in new[]{name,appId,secret,users,groups})field.IsEnabled=enabled;}
    ImChannelProfile Read(){var copy=draft.Copy();copy.Kind=(string)platform.SelectedItem!;if(copy.Kind!=draft.Kind)copy.ApiBase="";copy.Name=(name.Text??"").Trim();copy.AppId=(appId.Text??"").Trim();copy.Secret=secret.Text??"";copy.Users=Lines(users.Text);copy.Chats=Lines(groups.Text);copy.Validate();return copy;}
    static List<string> Lines(string? text)=>(text??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList();
    async Task Save(){var value=Read();await manager.SaveAsync(value);Reload(value.Id);}
    void New()=>Load(new ImChannelProfile{PetId=owner.Skin.Id,PetName=owner.Skin.Name,Persona=owner.Settings.SystemPrompt,Name=owner.Skin.Name});
    void Load(ImChannelProfile value){draft=value.Copy();platform.SelectedItem=draft.Kind;name.Text=draft.Name;appId.Text=draft.AppId;secret.Text=draft.Secret;users.Text=String.Join("\n",draft.Users);groups.Text=String.Join("\n",draft.Chats);LocalizedUi.Set(companion,"Text",()=>TextResources.Get("im.binding")+" "+draft.PetName);Refresh();}
    void Reload(string? id){loading=true;var list=manager.Profiles;profiles.ItemsSource=list;profiles.SelectedItem=list.FirstOrDefault(x=>x.Id==id);loading=false;if(profiles.SelectedItem is ImChannelProfile p)Load(p);else New();}
    void Changed(){if(!disposed&&Interlocked.Exchange(ref refreshQueued,1)==0)Dispatcher.UIThread.Post(()=>{Interlocked.Exchange(ref refreshQueued,0);if(!disposed)Refresh();});}
    void Refresh(){if(draft==null)return;LocalizedUi.Set(status,"Text",()=>TextResources.Get(manager.State(draft.Id))+"\n"+TextResources.Get("im.lastSender")+" "+manager.LastSender(draft.Id));if(manager.Warning.Length>0)notice.Text=TextResources.Get(manager.Warning);}
    void ClearQr(){(qr.Source as IDisposable)?.Dispose();qr.Source=null;qr.IsVisible=false;}
    public void Dispose(){disposed=true;manager.Changed-=Changed;login?.Cancel();ClearQr();}
}
