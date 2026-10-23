using System;
using System.IO;
using System.Linq;
using System.Windows;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        UsageStore usageStats;
        internal UsageStore UsageStats {get{return usageStats??(usageStats=new UsageStore(Path.Combine(Store.DirectoryPath,"usage")));}}
        void InitializeUsageTracking(){try{UsageTracking.Initialize(UsageStats);}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException))throw;Notice=TextResources.Get("petAppShell.initializeUsageTracking.notice");}}
        internal ChatWindow EnsureAppWindow()
        {
            if(chat==null){chat=(Application.Current==null?null:Application.Current.Windows.OfType<ChatWindow>().FirstOrDefault(w=>w.OwnsPet(this)))??new ChatWindow(this,CreateDraftConversation(null,Settings.AgentEnabled));var owned=chat;chat.Closed+=delegate{if(ReferenceEquals(chat,owned)){chat=null;if(preferences!=null){preferences.Dispose();preferences=null;}var ignored=CloseConversationWorkspacesAsync();}};}return chat;
        }
        internal void ShowAppWindow(ChatWindow window){if(!window.IsVisible)window.Show();if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Activate();}
        internal void ShowMcpPage(bool edit=false){var window=EnsureAppWindow();window.ShowMcpPage(edit);ShowAppWindow(window);}
        internal void NewShellConversation(){NewActiveConversation();}
        internal void ReleaseSettingsPage(SettingsPage page){if(page!=null&&ReferenceEquals(preferences,page)){preferences=null;CancelPendingGreeting();}}
    }
    public sealed partial class ChatWindow
    {
        internal bool OwnsPet(PetWindow value){return ReferenceEquals(pet,value);}
    }
}
