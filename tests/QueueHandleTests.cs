using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class QueueHandleTests
    {
        static int passed;
        static IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void Inspect(Window host,int count)
        {
            host.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));
            var rows=All<Grid>(host).Where(r=>r.Name=="QueuedMessageRow").ToArray();Check(rows.Length==count,"renders "+count+" pending rows");
            foreach(var row in rows)
            {
                var grip=All<Button>(row).Single(b=>b.Name=="ReorderQueuedMessage");
                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});
                Check(count>1?grip.IsVisible&&grip.IsEnabled&&grip.Opacity==1:!grip.IsVisible&&!grip.IsEnabled&&!grip.Focus(),"hover and keyboard handle availability matches queue count");
                Check(All<Button>(row).Count(b=>b.IsVisible&&(b.Name=="GuideQueuedMessage"||b.Name=="RemoveQueuedMessage"||b.Name=="EditQueuedMessage"))==3,"guidance, remove and edit remain available");
            }
        }
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window host=null;
            try
            {
                var cfg=new PetSettings();cfg.ReadReplies=false;cfg.AutoUpdate=false;cfg.ShowPredictedMessages=false;cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"};
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"queue-handle-"+Guid.NewGuid().ToString("N"),"settings.json")),null);
                var session=new ConversationSession(new Api(),new Speech(),()=>null);session.SelectPersona(pet.Skin.Id);
                typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);
                host=new Window{Width=850,Height=340,ShowInTaskbar=false};var composer=new ConversationComposer(pet,host,new MarkdownEditor(),new Button(),new TextBlock(),false);host.Content=composer;host.Show();
                var queue=pet.InputQueue;queue.Enqueue("running");queue.Enqueue("first pending");Inspect(host,1);
                queue.Enqueue("second pending");Inspect(host,2);queue.Remove(queue.Items.Last().Id);Inspect(host,1);
                composer.Detach();Console.WriteLine("Queue handle tests: "+passed+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(host!=null)host.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
        sealed class Api:IConversationApi
        {public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken t){var pending=new TaskCompletionSource<string>();t.Register(()=>pending.TrySetCanceled());return pending.Task;}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken t){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string x,CancellationToken t){throw new NotSupportedException();}public void Dispose(){}}
        sealed class Speech:IReplySpeech
        {public Task SpeakAsync(string s,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int v){}public void Dispose(){}}
    }
}
