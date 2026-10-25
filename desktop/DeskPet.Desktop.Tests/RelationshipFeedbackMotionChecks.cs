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
        int checks=0;
        void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        void Pause(int milliseconds){var watch=System.Diagnostics.Stopwatch.StartNew();var frame=new DispatcherFrame();using(var timer=new System.Threading.Timer(_=>Dispatcher.UIThread.Post(()=>{if(watch.ElapsedMilliseconds>=milliseconds)frame.Continue=false;}),null,0,10))Dispatcher.UIThread.PushFrame(frame);}
        void Until(Func<bool> condition,int milliseconds,string description){var watch=System.Diagnostics.Stopwatch.StartNew();while(!condition()){if(watch.ElapsedMilliseconds>=milliseconds)throw new TimeoutException(description);Pause(10);}}
        RelationshipFeedbackNotice Gain(string id,int points=1){var notice=new RelationshipFeedbackNotice{CompanionId=id,AwardedPoints=points,Milestones=new List<RelationshipMilestone>()};notice.EventIds.Add(Guid.NewGuid().ToString("N"));return notice;}
        static Border Card(RelationshipFeedbackWindow window){var outer=(Border)window.Content!;return outer.Child is Border inner?inner:outer;}
        static double Offset(Border card)=>((TranslateTransform)card.RenderTransform!).Y;
        void Inside(RelationshipFeedbackWindow window,Border card,string description){var top=card.TranslatePoint(new Point(0,0),window)!.Value;var bottom=card.TranslatePoint(new Point(0,card.Bounds.Height),window)!.Value;Check(top.Y>=-.5&&bottom.Y<=window.Bounds.Height+.5,description+": top="+top.Y+" bottom="+bottom.Y+" window="+window.Bounds.Height);}

        Directory.CreateDirectory(directory);SecretProtection.Configure(new TestProtector());
        new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=TextResources.CurrentLanguage,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,OnboardingState="completed",Wander=false,FollowCursor=false,HideOnFullscreen=false});
        var lifetime=new ClassicDesktopStyleApplicationLifetime();using var owner=new DesktopController(lifetime,new FixtureRuntime(directory));owner.Pet.Show();
        try{
            var ordinary=new RelationshipFeedbackWindow(owner,Gain(owner.RelationshipCompanionId));ordinary.Show();
            var card=Card(ordinary);Check(card.RenderTransform is TranslateTransform&&card.Opacity<=.15&&Offset(card)>=8,"ordinary gain begins below its final position and transparent");
            Inside(ordinary,card,"ordinary gain card fits the transparent window during entry");
            Until(()=>card.Opacity>.05,500,"ordinary entrance begins");Check(ordinary.IsVisible&&card.Opacity<.9&&Offset(card)>1&&Offset(card)<12,"ordinary gain visibly rises and fades in between its endpoints: opacity="+card.Opacity+" y="+Offset(card));
            Until(()=>card.Opacity>=.95,500,"ordinary entrance completes");Check(ordinary.IsVisible&&Math.Abs(Offset(card))<1,"ordinary gain rises and fades in within 200ms");
            Pause(750);Check(ordinary.IsVisible&&card.Opacity>=.95&&Math.Abs(Offset(card))<1,"ordinary gain holds fully visible for about one second");
            Pause(310);Check(ordinary.IsVisible&&card.Opacity<.9&&Offset(card)<-1,"ordinary gain fades upward during its 250ms exit");Inside(ordinary,card,"ordinary gain card fits the transparent window during exit");
            Pause(260);Check(!ordinary.IsVisible,"ordinary gain closes after its short exit");

            var merged=new RelationshipFeedbackWindow(owner,Gain(owner.RelationshipCompanionId));merged.Show();var mergedCard=Card(merged);
            Until(()=>mergedCard.Opacity>=.95,500,"merge fixture enters");Until(()=>mergedCard.Opacity<.9,1800,"merge fixture begins exit");Check(merged.IsVisible&&Offset(mergedCard)<0,"late merge fixture reaches the upward exit");
            double beforeOpacity=mergedCard.Opacity,beforeOffset=Offset(mergedCard);merged.Merge(Gain(owner.RelationshipCompanionId));
            Check(Math.Abs(mergedCard.Opacity-beforeOpacity)<.05&&Math.Abs(Offset(mergedCard)-beforeOffset)<.5,"late merge extends notice without replaying its entrance or jumping");
            Pause(180);Check(merged.IsVisible&&mergedCard.Opacity>=.95&&Math.Abs(Offset(mergedCard))<1,"late merge settles smoothly back to readable position");
            Pause(650);Check(merged.IsVisible&&mergedCard.Opacity>=.95,"merged points retain a new readable hold");merged.Close();

            var interaction=new RelationshipFeedbackWindow(owner,Gain(owner.RelationshipCompanionId));interaction.Show();
            Pause(300);var interactionCard=Card(interaction);double interactionOpacity=interactionCard.Opacity,interactionOffset=Offset(interactionCard);interaction.SetInteraction("A saved interaction response",Guid.NewGuid().ToString("N"));
            Check(Math.Abs(interactionCard.Opacity-interactionOpacity)<.05&&Math.Abs(Offset(interactionCard)-interactionOffset)<.5,"interaction extends the notice without replaying its entrance");
            Pause(6600);Check(interaction.IsVisible&&Card(interaction).Opacity>=.95,"interaction response remains readable for seven seconds after insertion");
            Pause(800);Check(!interaction.IsVisible,"interaction response exits after its seven-second hold");

            var milestoneNotice=Gain(owner.RelationshipCompanionId);milestoneNotice.Milestones.Add(new RelationshipMilestone{StageId="familiar",EarnedLocalDate="2026-10-25"});
            var milestone=new RelationshipFeedbackWindow(owner,milestoneNotice);milestone.Show();
            Pause(6800);Check(milestone.IsVisible&&Card(milestone).Opacity>=.95,"milestone remains readable for seven seconds");
            Pause(750);Check(!milestone.IsVisible,"milestone exits after its seven-second hold");
            Check(!ordinary.ShowActivated&&!ordinary.ShowInTaskbar,"animated notice remains passive");
            return checks;
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
