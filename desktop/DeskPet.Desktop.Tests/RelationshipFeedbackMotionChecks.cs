using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using DeskPet.Desktop;
using Dafeiyu;

internal static class RelationshipFeedbackMotionChecks
{
    internal static int Run(string directory)
    {
        int checks=0;void Check(bool yes,string text){if(!yes)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Pause(int ms){var watch=System.Diagnostics.Stopwatch.StartNew();var frame=new DispatcherFrame();using(var timer=new System.Threading.Timer(_=>Dispatcher.UIThread.Post(()=>{if(watch.ElapsedMilliseconds>=ms)frame.Continue=false;}),null,0,10))Dispatcher.UIThread.PushFrame(frame);}
        void Until(Func<bool> yes,int ms){var watch=System.Diagnostics.Stopwatch.StartNew();while(!yes()){if(watch.ElapsedMilliseconds>=ms)throw new TimeoutException("brief feedback visibility");Pause(10);}}
        RelationshipFeedbackNotice Gain(string id){return new RelationshipFeedbackNotice{CompanionId=id,AwardedPoints=1,Milestones=new List<RelationshipMilestone>()};}
        static Border Card(RelationshipFeedbackWindow w)=>((Border)w.Content!).Child is Border c?c:(Border)w.Content!;
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=TextResources.CurrentLanguage,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",Wander=false,FollowCursor=false,HideOnFullscreen=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));owner.Pet.Show();
        try{
            var startup=owner.Relationships.FlushAsync();Until(()=>startup.IsCompleted,5000);startup.GetAwaiter().GetResult();Pause(600);
            ((RelationshipFeedbackWindow?)typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner))?.Close();
            var ordinary=new RelationshipFeedbackWindow(owner,Gain(owner.RelationshipCompanionId));ordinary.Show();var card=Card(ordinary);
            Check(card.Opacity<=.15&&((TranslateTransform)card.RenderTransform!).Y>=8,"gain begins transparent below its resting position");
            Until(()=>card.Opacity>=.95,500);Check(ordinary.IsVisible&&Math.Abs(((TranslateTransform)card.RenderTransform!).Y)<1,"brief gain becomes clearly visible");
            Pause(1000);Check(!ordinary.IsVisible,"ordinary gain closes within one second including animation");
            var interaction=new RelationshipFeedbackWindow(owner,Gain(owner.RelationshipCompanionId));interaction.Show();interaction.SetInteraction("A brief greeting",Guid.NewGuid().ToString("N"));Until(()=>Card(interaction).Opacity>=.95,500);Pause(1000);Check(!interaction.IsVisible,"greeting has the same one-second lifetime");
            var stageNotice=Gain(owner.RelationshipCompanionId);stageNotice.Milestones.Add(new RelationshipMilestone{StageId="familiar",EarnedLocalDate="2026-10-27"});var stage=new RelationshipFeedbackWindow(owner,stageNotice);stage.Show();Until(()=>Card(stage).Opacity>=.95,500);Pause(1000);Check(!stage.IsVisible,"milestone content does not extend the notice");
            var merged=new RelationshipFeedbackWindow(owner,Gain(owner.RelationshipCompanionId));merged.Show();Until(()=>Card(merged).Opacity>=.95,500);Pause(400);double before=Card(merged).Opacity;merged.Merge(Gain(owner.RelationshipCompanionId));merged.SetInteraction("Late greeting",Guid.NewGuid().ToString("N"));Check(Math.Abs(Card(merged).Opacity-before)<.05,"merge updates content without restarting entrance");Pause(650);Check(!merged.IsVisible,"late gain and greeting do not restart the one-second lifetime");
            Check(!ordinary.ShowActivated&&!ordinary.ShowInTaskbar,"brief notice remains passive");
            typeof(PetWindow).GetField("random",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(owner.Pet,new MissGreetingRandom());
            RelationshipFeedbackWindow? Notice()=>(RelationshipFeedbackWindow?)typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner);
            int scoreBefore=owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).State.Score;owner.Pet.UserInteract("petting");var flush=owner.Relationships.FlushAsync();Until(()=>flush.IsCompleted,3000);flush.GetAwaiter().GetResult();
            Until(()=>Notice()?.IsVisible==true&&Notice()!.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==TextResources.Format("relationship.feedback.gain",1)),3000);
            // Collapsed ScrollViewer content can remain logically realized; inspect its containing area.
            Check(owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).State.Score==scoreBefore+1&&!Notice()!.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Name=="RelationshipInteraction"&&t.Parent is ScrollViewer area&&area.IsVisible),"greeting probability never suppresses or changes a real gain");Notice()!.Close();
            owner.Pet.UserInteract("petting");flush=owner.Relationships.FlushAsync();Until(()=>flush.IsCompleted,3000);flush.GetAwaiter().GetResult();Pause(500);Check(Notice()?.IsVisible!=true,"probability miss with no gain creates no popup");return checks;
        }finally{owner.Pet.Close();owner.Dispose();owner.ImageServicesShutdown.GetAwaiter().GetResult();}
    }
    internal static int RunPlacement(string directory)
    {
        int checks=0;void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        static PixelRect Pixels(Window window)=>new(window.Position.X,window.Position.Y,(int)Math.Ceiling(window.Bounds.Width*window.RenderScaling),(int)Math.Ceiling(window.Bounds.Height*window.RenderScaling));
        static bool Overlap(PixelRect a,PixelRect b)=>a.X<b.Right&&a.Right>b.X&&a.Y<b.Bottom&&a.Bottom>b.Y;
        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=TextResources.CurrentLanguage,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",Wander=false,FollowCursor=false,HideOnFullscreen=false});
        var lifetime=new ClassicDesktopStyleApplicationLifetime();using var owner=new DesktopController(lifetime,new FixtureRuntime(directory));owner.Pet.Show();
        RelationshipFeedbackWindow? notice=null;
        try{
            owner.Pet.Position=new PixelPoint(125,10);Dispatcher.UIThread.RunJobs();
            var gain=new RelationshipFeedbackNotice{CompanionId=owner.RelationshipCompanionId,AwardedPoints=2,Milestones=new List<RelationshipMilestone>()};notice=new RelationshipFeedbackWindow(owner,gain);notice.Show();Dispatcher.UIThread.RunJobs();
            var pet=Pixels(owner.Pet);var bubble=Pixels(notice);Console.WriteLine("FEEDBACK_PLACEMENT pet="+pet+" bubble="+bubble);
            Check(pet.Width>0&&pet.Height>0&&bubble.Width>0&&bubble.Height>0,"native placement fixture has real laid-out window bounds");
            Check(owner.Pet.Position.X==125&&owner.Pet.Position.Y==10,"feedback placement leaves the pet position unchanged");
            Check(!Overlap(pet,bubble),"top-edge gain bubble leaves the pet uncovered");
            var original=bubble;owner.Pet.Position=new PixelPoint(310,240);Dispatcher.UIThread.RunJobs();bubble=Pixels(notice);
            Check(bubble.X!=original.X||bubble.Y!=original.Y,"gain bubble follows a relocated pet");
            Check(!Overlap(Pixels(owner.Pet),bubble),"relocated gain bubble remains outside the pet bounds");
            notice.Close();notice=null;owner.Pet.Position=new PixelPoint(125,10);Dispatcher.UIThread.RunJobs();
            string reply="placement response";string eventId=Guid.NewGuid().ToString("N");owner.ShowRelationshipInteraction(owner.RelationshipCompanionId,reply,eventId);Dispatcher.UIThread.RunJobs();
            var routed=(RelationshipFeedbackWindow?)typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner);
            Check(routed?.IsVisible==true&&routed.GetLogicalDescendants().OfType<TextBlock>().Any(text=>text.Text==reply),"controller interaction route retains the opened reply notice");
            Check(!Overlap(Pixels(owner.Pet),Pixels(routed!)),"controller interaction route also leaves the top-edge pet uncovered");
            routed!.Close();
            var area=owner.Pet.Screens.ScreenFromWindow(owner.Pet)?.WorkingArea??new PixelRect(0,0,1920,1080);
            owner.Pet.Width=(area.Width-4)/owner.Pet.RenderScaling;owner.Pet.Height=(area.Height-4)/owner.Pet.RenderScaling;owner.Pet.Position=new PixelPoint(area.X+2,area.Y+2);Dispatcher.UIThread.RunJobs();
            var fullPet=Pixels(owner.Pet);Check(fullPet.Width>=area.Width-8&&fullPet.Height>=area.Height-8,"no-space fixture fills the working area");
            owner.ShowRelationshipInteraction(owner.RelationshipCompanionId,"No space response",Guid.NewGuid().ToString("N"));Dispatcher.UIThread.RunJobs();
            var suppressed=(RelationshipFeedbackWindow?)typeof(DesktopController).GetField("relationshipNotice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner);
            Check(suppressed?.IsVisible!=true,"controller suppresses a no-space notice without covering pet input");
            return checks;
        }finally{notice?.Close();owner.Pet.Close();owner.Dispose();owner.ImageServicesShutdown.GetAwaiter().GetResult();}
    }
    sealed class MissGreetingRandom:Random {public override double NextDouble(){return .9;}}
    sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("No fixture audio");
        public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("No fixture audio");
    }
}
