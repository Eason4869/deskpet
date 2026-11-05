using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class NotificationIntegrationUiChecks
{
    internal static int Run(string directory)
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        void Complete(Task task){var end=DateTime.UtcNow.AddSeconds(15);while(!task.IsCompleted){if(DateTime.UtcNow>end)throw new TimeoutException("Notification integration shutdown");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();}
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{AutoUpdate=false,GreetingEnabled=false,OnboardingState="completed",NotificationSoundsEnabled=false});
        var runtime=new TestRuntime(directory);var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),runtime);owner.Pet.Show();
        var stage=typeof(ConversationSession).GetProperty("Stage")!;ProactiveConversationBinding? binding=null;
        try{
            Check(!owner.NotificationAudioBusy,"idle application leaves notification audio available");
            var item=new ProactiveEvent(Guid.NewGuid().ToString("N"),owner.Skin.Id,"A shown proactive greeting",null,DateTime.UtcNow,null,null);owner.RegisterShownProactive(item,owner.Settings);
            binding=new ProactiveConversationBinding(owner,runtime,item,new ImageDescriptionOverride{Mode="disabled"});
            var bindings=(Dictionary<string,ProactiveConversationBinding>)typeof(DesktopController).GetField("proactiveBindings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;bindings.Add(binding.Id,binding);
            stage.SetValue(binding.Session,ConversationStage.Speaking);
            Check(owner.Session.Stage==ConversationStage.Idle&&owner.WorkBindings.Manager.OpenWorkspaces.All(w=>w.Session.Stage==ConversationStage.Idle),"proactive reply owns an independent speech session");
            Check(owner.NotificationAudioBusy,"notification sounds yield to an independent proactive conversation reply");
            stage.SetValue(binding.Session,ConversationStage.Idle);Check(!owner.NotificationAudioBusy,"finishing the proactive reply releases audio without replaying a notification");
            return checks;
        }finally{if(binding!=null)stage.SetValue(binding.Session,ConversationStage.Idle);owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
    }
}
