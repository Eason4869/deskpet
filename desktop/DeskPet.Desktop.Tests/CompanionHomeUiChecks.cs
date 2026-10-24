using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Automation;
using Avalonia.LogicalTree;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Dafeiyu;
using DeskPet.Desktop;

internal static class CompanionHomeUiChecks
{
    internal static int Run(string directory,string language="en_US")
    {
        int checks=0;
        void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        void Complete(Task task,int timeout=15000){var timer=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(timer.ElapsedMilliseconds>timeout)throw new TimeoutException("Companion fixture");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
        T Named<T>(Control root,string name) where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(x=>x.Name==name);
        void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Directory.CreateDirectory(directory);
        SecretProtection.Configure(new TestProtector());new SettingsStore(Path.Combine(directory,"settings.json")).Save(new PetSettings{UiLanguage=language,OnboardingState="completed",AutoUpdate=false});
        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new FixtureRuntime(directory));
        Complete(Task.Run(()=>RelationshipHomeFixtures.Seed156(owner.RelationshipService,owner.RelationshipCompanionId)));
        var chat=new ChatWindow(owner,false);chat.Show();
        try{
            var draft=Named<TextBox>(chat,"ChatDraft");draft.Text="preserved companion draft";var session=chat.BoundSession;
            var avatar=Named<Button>(chat,"ShellAvatar");Click(avatar);Dispatcher.UIThread.RunJobs();
            var entry=avatar.ContextMenu!.Items.OfType<MenuItem>().Single(x=>x.Name=="ProfileCompanionHome");
            Check(entry.IsEnabled&&entry.Focusable&&AutomationProperties.GetName(entry)!.Contains(owner.Skin.DisplayName),"profile is accessible and keyboard focusable");
            Check(entry.Icon is AvatarView a&&ReferenceEquals(a.Skin,owner.Skin)&&entry.Opacity==1,"original-color canonical avatar");
            entry.Focus();Check(entry.IsFocused,"profile accepts real keyboard focus");entry.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Dispatcher.UIThread.RunJobs();
            var page=chat.GetLogicalDescendants().OfType<CompanionHomePage>().Single();Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
            Check(chat.CurrentPage=="companion"&&!avatar.ContextMenu.IsOpen,"profile closes and navigates in main page host");
            Check(page.View!.Snapshot.State.Score==156&&Named<TextBlock>(page,"CompanionRelationshipScore").Text=="156 / 240","home visit renders committed actual values");
            chat.NavigateHistory(-1);Check(chat.CurrentPage=="chat"&&draft.Text=="preserved companion draft"&&ReferenceEquals(chat.BoundSession,session),"draft and session survive companion navigation");chat.NavigateHistory(1);chat.ShowImages();chat.NavigateHistory(-1);Check(chat.CurrentPage=="companion","images and companion share history");
            Check(page.View!.ActiveDays==16&&page.View!.AcquaintanceDays==21,"active days and acquaintance span have exact distinct meanings");
            var originalSkin=owner.Skin;string originalId=owner.RelationshipCompanionId;
            var otherSkin=new PetSkin{Id="home-fixture",PickerKey="custom-pack:home-fixture",Name="Second companion",Prompt="Fixture",Atlas=originalSkin.Atlas,Avatar=SpriteAssets.AvatarPath(originalSkin),Description=new string('x',220)};owner.Skins.Add(otherSkin);
            string otherId=RelationshipIdentity.Resolve(directory,otherSkin.PickerKey,null,otherSkin.Id);
            Complete(Task.Run(()=>RelationshipHomeFixtures.SeedMaximum(owner.RelationshipService,otherId)),120000);
            Complete(owner.SelectSkinAsync(otherSkin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());
            Check(page.View!.CompanionId==otherId&&page.View!.Progress.Score==1000&&page.View!.Progress.NextStageId==null&&ReferenceEquals(Named<AvatarView>(page,"CompanionHomeAvatar").Skin,otherSkin),"actual skin switch binds avatar to independent max state");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();var maxCaptures=Path.GetFullPath(Path.Combine("work","relationship-validation","avalonia"));Directory.CreateDirectory(maxCaptures);using(var maxImage=chat.CaptureRenderedFrame())maxImage!.Save(Path.Combine(maxCaptures,language+"-maximum.png"),new PngBitmapEncoderOptions());
            Complete(owner.SelectSkinAsync(originalSkin));Complete(owner.Relationships.FlushAsync());Complete(page.RefreshAsync());Check(page.View!.CompanionId==originalId&&page.View!.Progress.Score==156,"switching back preserves saved identity score");
            var settings=Named<Expander>(page,"CompanionRelationshipSettings");settings.IsExpanded=true;Dispatcher.UIThread.RunJobs();
            var growth=Named<CheckBox>(page,"RelationshipGrowthEnabled");growth.IsChecked=false;growth.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Complete(page.PendingSettings);Complete(page.RefreshAsync());
            Check(!owner.RelationshipService.GetSnapshot(owner.RelationshipCompanionId).Settings.GrowthEnabled,"growth checkbox saves global setting");
            var captures=Path.GetFullPath(Path.Combine("work","relationship-validation","avalonia"));Directory.CreateDirectory(captures);
            {
                Complete(page.RefreshAsync());
                foreach(string theme in new[]{"light","dark"})foreach(int width in new[]{390,1100}){DesktopTheme.Apply(theme);chat.Width=width;chat.Height=800;Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();var relation=(StackPanel)Named<TextBlock>(page,"CompanionRelationshipStage").Parent!;Check(Grid.GetRow(relation)==(width<780?1:0),"responsive relation row "+width+" viewport="+page.Bounds.Width);var score=Named<TextBlock>(page,"CompanionRelationshipScore");Check(score.TranslatePoint(new Point(score.Bounds.Width,0),page)!.Value.X<=page.Bounds.Width,"score stays in viewport "+width);using var image=chat.CaptureRenderedFrame();Check(image!=null,"rendered "+language+" "+theme+" "+width);image!.Save(Path.Combine(captures,language+"-"+theme+"-"+width+".png"),new PngBitmapEncoderOptions());}
            }
            var zeroSkin=new PetSkin{Id="home-zero",PickerKey="custom-pack:home-zero",Name="Unmet companion",Prompt="Fixture",Atlas=originalSkin.Atlas,Avatar=SpriteAssets.AvatarPath(originalSkin)};owner.Skins.Add(zeroSkin);Complete(owner.SelectSkinAsync(zeroSkin));Complete(page.RefreshAsync());Check(page.View!.Snapshot.State==null&&page.View!.Progress.Score==0,"unmet companion has real empty zero state");Complete(owner.SelectSkinAsync(originalSkin));Complete(page.RefreshAsync());
            string file=Path.Combine(directory,"relationships","state.json");byte[] original=File.ReadAllBytes(file);File.WriteAllText(file,"{broken");var feedback=Named<CheckBox>(page,"RelationshipFeedbackEnabled");feedback.IsChecked=false;feedback.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));Complete(page.PendingSettings);Complete(page.RefreshAsync());Check(Named<CheckBox>(page,"RelationshipFeedbackEnabled").IsChecked==true&&File.ReadAllText(file)=="{broken","failed setting preserves checkbox and ledger bytes");
            var retained=page.View;var retainedState=retained?.Snapshot?.State;
            Check(retained?.ReadFailed==true&&retainedState!=null&&retainedState.Score==156&&Named<TextBlock>(page,"RelationshipWarning").IsVisible,"corruption retains saved score and retry warning");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();using(var failed=chat.CaptureRenderedFrame())failed!.Save(Path.Combine(captures,language+"-read-failure.png"),new PngBitmapEncoderOptions());File.WriteAllBytes(file,original);Complete(page.RefreshAsync());
            var context=Named<CheckBox>(page,"RelationshipAiContextEnabled");var priorSave=page.PendingSettings;
            using(var lease=new FileStream(Path.Combine(directory,"relationships",".transaction.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                Click(Named<Button>(page,"RelationshipRetry"));
                Check(!context.IsEffectivelyEnabled,"retry immediately disables the already-rendered settings controls");
                context.IsChecked=false;context.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Check(ReferenceEquals(priorSave,page.PendingSettings)&&context.IsChecked==true,"settings handler rejects concurrent admission during retry");
                var admittedRetry=page.PendingRetry;Click(Named<Button>(page,"RelationshipRetry"));Check(ReferenceEquals(admittedRetry,page.PendingRetry),"retry handler rejects duplicate admission while busy");
            }
            Complete(page.PendingRetry);Complete(page.RefreshAsync());Check(!page.View!.ReadFailed&&!owner.RelationshipService.GetSnapshot(originalId).Settings.FeedbackEnabled,"retry recovers preserved ledger and failed settings intent");
        }finally{chat.Close();owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
        return checks;
    }
    private sealed class FixtureRuntime(string directory):DesktopRuntime
    {
        public override string DataDirectory=>directory;public override bool ShowTray=>false;
        public override void InitializeProtection()=>SecretProtection.Configure(new TestProtector());
        public override IReplySpeech CreateSpeech(ApiClient api)=>new TestSpeech();
        public override IAudioRecorder CreateRecorder()=>new TestRecorder();
        public override Task<bool> RequestMicrophoneAccessAsync()=>Task.FromResult(false);
        public override IPcmMicrophone CreateMicrophone()=>throw new InvalidOperationException("Fixture has no audio");
        public override ILiveAudioOutput CreateLiveOutput()=>throw new InvalidOperationException("Fixture has no audio");
    }
}
