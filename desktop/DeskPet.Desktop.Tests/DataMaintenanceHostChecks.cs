using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class DataMaintenanceHostChecks
{
    internal static int Run(string directory,string language="en_US")
    {
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{AutoUpdate=false,OnboardingState="completed",UiLanguage=language});
        var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new Runtime(directory));
        void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(20);while(!task.IsCompleted){if(DateTime.UtcNow>deadline)throw new TimeoutException("Admission UI fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();}
        try
        {
            Wait(owner.WorkBindings.Jobs.InitializeAsync());
            var gate=owner.DataMaintenance;
            if(!ReferenceEquals(gate,owner.Session.Maintenance)||!ReferenceEquals(gate,owner.Dictation.Maintenance)||!ReferenceEquals(gate,owner.Live.Maintenance)||!ReferenceEquals(gate,owner.WorkBindings.Manager.Maintenance)||!ReferenceEquals(gate,owner.WorkBindings.Jobs.Maintenance))throw new Exception("Native producer gates differ");
            var acquired=gate.BeginAsync();Wait(acquired);using(acquired.Result)
            {
                var previous=owner.Skin;string id=owner.Settings.SkinId,key=owner.Settings.SelectedCompanionKey,persona=owner.Session.PersonaId;
                var target=new PetSkin{Id="maintenance-fixture",PickerKey="maintenance-fixture",Name="Fixture",Prompt="Fixture persona"};
                try{Wait(owner.SelectSkinAsync(target));}catch(InvalidOperationException){}
                if(!ReferenceEquals(owner.Skin,previous)||owner.Settings.SkinId!=id||owner.Settings.SelectedCompanionKey!=key||owner.Session.PersonaId!=persona)throw new Exception("Maintenance rejection published a mixed companion identity");
                var exitField=typeof(DesktopController).GetField("applicationExitDrain",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;var previousDrain=exitField.GetValue(owner);
                bool refused=false;try{Wait(owner.ExitAsync());}catch(InvalidOperationException){refused=true;}
                if(!refused||!ReferenceEquals(previousDrain,exitField.GetValue(owner)))throw new Exception("Native exit began draining during maintenance");
            }
            Console.WriteLine("PASS native host shared admission, atomic companion refusal and exit exclusion");return 3;
        }
        finally{owner.Pet.Close();owner.Dispose();Wait(owner.ImageServicesShutdown);}
    }
    private sealed class Runtime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("No device in fixture");
        public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("No device in fixture");
    }
}
