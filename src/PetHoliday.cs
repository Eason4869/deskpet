using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    // Drawing and alpha testing share the very same decoded frame.
    internal sealed class HolidayVisualAssets
    {
        readonly Func<OutfitClipDefinition,Stream> open;
        readonly Dictionary<OutfitClipDefinition,MotionClip> clips=new Dictionary<OutfitClipDefinition,MotionClip>();
        readonly LinkedList<OutfitClipDefinition> recent=new LinkedList<OutfitClipDefinition>();
        OutfitClipDefinition gazeSource;MotionClip gaze;
        internal HolidayVisualAssets(Func<OutfitClipDefinition,Stream> source)
        {open=source??(clip=>clip.OpenResource());}
        internal MotionClip Get(OutfitClipDefinition source)
        {
            if(source==null)throw new InvalidDataException(TextResources.Get("petHoliday.get.text"));
            MotionClip clip;if(source.FrameCount==16 && source.Columns==4 && source==gazeSource)return gaze;
            if(clips.TryGetValue(source,out clip)){recent.Remove(source);recent.AddLast(source);return clip;}
            using(var stream=open(source)) {
                if(stream==null)throw new InvalidDataException(TextResources.Get("petHoliday.get.text.alternativeA"));
                var image=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
                clip=new MotionClip(image,source.FrameCount,source.FrameCount/source.Fps,source.Loop,source.FrameWidth,source.FrameHeight,source.Columns);
            }
            // WIC may recover invalid compressed PNG data as empty pixels rather
            // than throwing. A usable costume frame must still meet the same
            // visible-art/transparent-background contract as the original atlas.
            for(int frame=0;frame<clip.Count;frame++) {
                bool visible=false,transparent=false;
                for(int y=0;y<clip.Height && !(visible && transparent);y++)for(int x=0;x<clip.Width && !(visible && transparent);x++) {
                    byte alpha=clip.AlphaAt(frame,x,y);if(alpha>16)visible=true;if(alpha==0)transparent=true;
                }
                if(!visible || !transparent)throw new InvalidDataException(TextResources.Get("petHoliday.get.text.alternativeB"));
            }
            if(source.FrameCount==16 && source.Columns==4){gazeSource=source;gaze=clip;return clip;}
            if(clips.Count==3){clips.Remove(recent.First.Value);recent.RemoveFirst();}
            clips[source]=clip;recent.AddLast(source);return clip;
        }
        internal void Clear(){clips.Clear();recent.Clear();gaze=null;gazeSource=null;}
    }
    // Preserve the Image content contract used by existing UI/accessibility code.
    internal sealed class HolidayPetImage:Image
    {
        internal Action<DrawingContext> DrawClouds;
        internal Action FrameRendered;
        protected override void OnRender(DrawingContext context)
        {base.OnRender(context);if(DrawClouds!=null)DrawClouds(context);if(FrameRendered!=null)FrameRendered();}
    }
    public sealed partial class PetWindow
    {
        public HolidayController Holidays {get;private set;}
        HolidayVisualAssets holidayAssets;
        readonly HolidayPlayback holidayPlayback=new HolidayPlayback();
        readonly HolidayTransition holidayTransition=new HolidayTransition();
        OutfitDefinition holidayCommitted,holidayTarget;
        string holidaySourceKey,holidayFailedKey,holidayWarningShown;
        bool holidayRefreshing;
        internal Func<DateTime> HolidayLocalTime=()=>DateTime.Now;
        internal bool HolidayPlaying {get{return holidayPlayback.Active;}}
        internal void InitializeHoliday(HolidayController controller=null,Func<OutfitClipDefinition,Stream> open=null)
        {
            if(Holidays!=null)Holidays.Changed-=HolidayChanged;
            holidayPlayback.Cancel();holidayTransition.FinishImmediately();holidayTransition.TakeCommit();
            holidayCommitted=holidayTarget=null;holidaySourceKey=holidayFailedKey=null;
            holidayAssets=new HolidayVisualAssets(open);
            Holidays=controller??new HolidayController(new HolidayCalendar(),new HolidayCatalog(),new HolidayStateStore(Path.Combine(Store.DirectoryPath,"holiday-state.json")));
            Holidays.Changed+=HolidayChanged;
            var surface=image as HolidayPetImage;if(surface!=null){surface.DrawClouds=DrawHolidayClouds;surface.FrameRendered=HolidayFrameRendered;}
            RefreshHoliday();
        }
        bool HolidayBusinessBusy
        {get{return DialogueBusy || DictationActive || LiveActive || HasQueuedMessages || ConversationSessions.Any(s=>s.IsBusy || s.NeedsExecutionReconciliation) || watch.Elapsed.TotalSeconds<completionUntil;}}
        bool HolidayCanAnimate
        {get{return !closing && IsPetVisible && !Paused && !dragging && !walking && !dropMotion.Active && !interaction.Active && Animation.State=="idle" && !Settings.QuietMode && !Settings.ClickThrough && !sessionLocked && !IsUpdating && !GuideActive && !HolidayBusinessBusy && !clickTimer.IsEnabled && (updates==null || !updates.HasRestartCountdown) && (ContextMenu==null || !ContextMenu.IsOpen) && (trayMenu==null || !trayMenu.Visible) && (palette==null || !palette.IsVisible) && !DesktopInterop.ForegroundIsFullScreen(handle);}}
        internal void RefreshHoliday()
        {
            if(Holidays==null || holidayRefreshing)return;
            holidayRefreshing=true;
            try {
                bool can=HolidayCanAnimate;
                Holidays.Refresh(new HolidayContext{PersonaId=Skin.Id,CharacterId=Skin.BuiltinCharacterId,LocalTime=HolidayLocalTime(),MonotonicSeconds=watch.Elapsed.TotalSeconds,CanPlayEasterEgg=can && Animation.State=="idle" && !holidayTransition.Active && holidayFailedKey==null,CanAnimateTransition=can,ResourceVersion=Skin.BuiltinCharacterId+":"+Skin.NativeSourceKey});
                if(!can || (holidayPlayback.Active && (holidayPlayback.Ticket==null || holidayPlayback.Ticket.Revision!=Holidays.Revision)))CancelHolidayPlayback();
                RefreshHolidayAppearance(can);
                if(!String.IsNullOrEmpty(Holidays.PersistenceWarning) && Holidays.PersistenceWarning!=holidayWarningShown){holidayWarningShown=Holidays.PersistenceWarning;Notice=TextResources.Format("petHoliday.refreshHoliday.noticeHolidaysPersistenceWarning",Holidays.PersistenceWarning);if(preferences!=null)preferences.ShowNotice(Notice);}
                if(can && !holidayPlayback.Active && !holidayTransition.Active) {
                    string notice=Holidays.TakeUnlockNotice();
                    if(!String.IsNullOrEmpty(notice))ShowHolidayNotice(notice);
                }
            }finally{holidayRefreshing=false;}
        }
        void HolidayChanged(object sender,EventArgs args)
        {if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>HolidayChanged(sender,args)));return;}if(!holidayRefreshing){RefreshHoliday();UpdateFrame();}}
        static string HolidayKey(OutfitDefinition outfit)
        {return outfit==null?"":outfit.CharacterId+":"+outfit.OutfitId+":"+outfit.Version+":"+outfit.SourceKey;}
        void RefreshHolidayAppearance(bool animate)
        {
            var desired=Holidays.EffectiveOutfit;string desiredKey=HolidayKey(desired);
            if(holidaySourceKey!=Skin.BuiltinCharacterId){holidayAssets.Clear();holidaySourceKey=Skin.BuiltinCharacterId;holidayFailedKey=null;holidayCommitted=null;}
            if(HolidayKey(holidayTarget)!=desiredKey) {
                CancelHolidayPlayback();holidayTarget=desired;
                if(holidayFailedKey!=desiredKey)holidayFailedKey=null;
                if(animate && Holidays.TransitionRequested && holidayCommitted!=desired){holidayTransition.Start(desiredKey);}
                else {holidayTransition.FinishImmediately();holidayTransition.TakeCommit();CommitHolidayAppearance();}
                Holidays.AcknowledgeTransition();
            }
            if(!animate && holidayTransition.Active){holidayTransition.FinishImmediately();holidayTransition.TakeCommit();CommitHolidayAppearance();}
            if(!holidayTransition.Active && HolidayKey(holidayCommitted)!=desiredKey && holidayFailedKey!=desiredKey)CommitHolidayAppearance();
        }
        void CommitHolidayAppearance()
        {holidayCommitted=holidayTarget;if(image!=null)image.InvalidateVisual();}
        void CancelHolidayPlayback()
        {if(Holidays!=null)Holidays.CancelHover();if(holidayPlayback.Active || holidayPlayback.Completed)holidayPlayback.Cancel();}
        void CancelHolidayEffects()
        {
            CancelHolidayPlayback();
            if(holidayTransition.Active){holidayTransition.FinishImmediately();holidayTransition.TakeCommit();holidayTarget=Holidays==null?null:Holidays.EffectiveOutfit;CommitHolidayAppearance();}
            if(image!=null)image.InvalidateVisual();
        }
        void RejectHolidayOutfit(OutfitDefinition outfit)
        {
            bool refreshing=holidayRefreshing;holidayRefreshing=true;
            try {
                // Retire every local owner before ReportUnavailable synchronously
                // notifies settings and greeting observers. Their context and the
                // already-published bitmap/hit frame must agree at that boundary.
                holidayFailedKey=outfit==null?null:HolidayKey(outfit);holidayCommitted=holidayTarget=null;
                holidayPlayback.Cancel();holidayTransition.FinishImmediately();holidayTransition.TakeCommit();
                if(Holidays!=null)Holidays.CancelHover();holidayAssets.Clear();
                UpdateFrame();
                if(Holidays!=null) {
                    Holidays.ReportUnavailable(outfit);Holidays.AcknowledgeTransition();
                    // An observer may have explicitly chosen a different valid
                    // outfit. Prepare that latest choice without a delayed cloud.
                    if(Holidays.EffectiveOutfit!=null){RefreshHolidayAppearance(false);UpdateFrame();}
                }
                Notice=TextResources.Get("petHoliday.rejectHolidayOutfit.notice");
            }finally{holidayRefreshing=refreshing;}
            if(image!=null)image.InvalidateVisual();
        }
        bool TryHolidayHover()
        {
            if(Holidays==null)return false;RefreshHoliday();
            var ticket=Holidays.TryBeginHover(random.NextDouble());if(ticket==null)return false;
            var outfit=Holidays.EffectiveOutfit;
            try {holidayAssets.Get(ticket.Clip);holidayPlayback.Start(ticket);DismissGreeting();UpdateFrame();return holidayPlayback.Active;}
            catch(Exception error){if(!HolidayAssetError(error))throw;RejectHolidayOutfit(outfit);return false;}
        }
        void HolidayFrameRendered()
        {
            var observed=ObserveHolidayEncounter(IsPetVisible&&Opacity>0&&image.Source!=null,HolidayEncounterKind.VisibleBuddy);
            if(!holidayPlayback.Active || holidayPlayback.FirstFrameRendered || shownMotion==null)return;
            holidayPlayback.MarkFirstFrameRendered();Holidays.MarkHoverFirstFrame(holidayPlayback.Ticket);
        }
        bool AdvanceHoliday(double delta)
        {
            if(holidayTransition.Active){holidayTransition.Advance(delta);if(holidayTransition.TakeCommit())CommitHolidayAppearance();image.InvalidateVisual();}
            if(!holidayPlayback.Active)return false;
            holidayPlayback.Advance(delta);
            if(holidayPlayback.Completed){var ticket=holidayPlayback.Ticket;RefreshHoliday();holidayPlayback.Cancel();Holidays.CompleteHover(ticket);ConversationChanged();}
            UpdateFrame();return true;
        }
        static bool HolidayAssetError(Exception error)
        {return error is IOException || error is InvalidDataException || error is FileFormatException || error is UnauthorizedAccessException || error is InvalidOperationException || error is NotSupportedException || error is ArgumentException;}
        bool TryHolidayFrame()
        {
            var outfit=holidayCommitted;if(outfit==null && !holidayPlayback.Active)return false;
            if(outfit!=null && HolidayKey(outfit)==holidayFailedKey)return false;
            try {
                OutfitClipDefinition spec;int sample;bool egg=holidayPlayback.Active;
                if(egg){spec=holidayPlayback.Ticket.Clip;sample=holidayPlayback.Frame;}
                else {
                    string action=interaction.Active?interaction.Name:Animation.State;
                    bool gazeFrame=!interaction.Active && !Paused && !dragging && !walking && !hover.Active && Settings.FollowCursor && Animation.State=="idle" && handle!=IntPtr.Zero && watch.Elapsed.TotalSeconds-lastCursorMove<1.4;
                    if(gazeFrame){var bounds=DesktopInterop.Bounds(handle);var cursor=DesktopInterop.Cursor();int direction=PetMath.LookDirection(cursor.X-bounds.Left-bounds.Width*.5,cursor.Y-bounds.Top-bounds.Height*.4);if(direction>=0)gaze.Reset(direction);spec=outfit.Gaze;sample=gaze.Frame(1);}
                    else {
                        bool original=interaction.Active?originalInteraction:originalAction;
                        bool alternate=!original && outfit.FindClip(action,"alternate")!=null && (AdditionalInteractions.Has(Skin,action));
                        spec=outfit.FindClip(action,alternate?"alternate":"default");
                        var clip=holidayAssets.Get(spec);double elapsed=interaction.Active?interaction.Elapsed:Animation.Elapsed;
                        double sourceDuration=HolidaySourceDuration(action,interaction.Active,original);
                        double phase=sourceDuration>0?elapsed/sourceDuration:0;
                        sample=clip.Sample(phase*clip.Duration);
                    }
                }
                var visual=holidayAssets.Get(spec);sample=Math.Max(0,Math.Min(visual.Count-1,sample));
                if(shownMotion==visual && shownSample==sample && shownWidth==Width && shownHeight==Height)return true;
                shownMotion=visual;shownSample=sample;shownRow=interaction.Active?interaction.Row:Animation.Row;shownCol=sample;shownSupplemental=interaction.Active;
                shownMirrored=false;shownOffset=new Point(spec.OffsetX,spec.OffsetY);shownWidth=Width;shownHeight=Height;gazeActive=false;
                image.RenderTransform=spec.OffsetX==0 && spec.OffsetY==0?Transform.Identity:new TranslateTransform(spec.OffsetX*Width/480,spec.OffsetY*Height/520);
                image.Source=visual.Frame(sample);return true;
            }catch(Exception error) {
                if(!HolidayAssetError(error))throw;
                RejectHolidayOutfit(outfit??(Holidays==null?null:Holidays.EffectiveOutfit));return true;
            }
        }
        double HolidaySourceDuration(string action,bool interacting,bool original)
        {
            var quiet=AdditionalInteractions.Quiet(Skin,action,original);if(quiet!=null)return quiet.Duration;
            IllustratedAnimation specification;
            if(AdditionalInteractions.Specification(Skin,action,original,out specification))return specification.Frames/specification.Fps;
            if(original && AdditionalInteractions.HasOriginal(Skin,action))return AdditionalInteractions.OriginalFrameCount(Skin,action)/8.0;
            if(AdditionalInteractions.Has(Skin,action))return 1;
            if(Skin.Illustrated){IllustratedAnimation clip;if(Skin.Manifest.Animations.TryGetValue(action,out clip))return clip.Frames/clip.Fps;}
            if(interacting){int i=Array.IndexOf(new[]{"lifted","landing","petting","poked","feeding","happy","sleeping","special"},action);if(i>=0)return new[]{1,.64,1.6,.8,1.6,1.2,3.2,2}[i];}
            int row=Array.IndexOf(AnimationPlayer.States,action);if(row<0)return 1;
            return AnimationPlayer.Counts[row]*new[]{.17,.22,.22,.16,.13,.16,.19,.18,.18}[row];
        }
        ImageSource HolidayPreview(string action,bool isInteraction)
        {
            var outfit=holidayCommitted;if(outfit==null || HolidayKey(outfit)==holidayFailedKey)return null;
            bool original=isInteraction && interaction.Name==action && originalInteraction;
            string variant=!original && AdditionalInteractions.Has(Skin,action) && outfit.FindClip(action,"alternate")!=null?"alternate":"default";
            try{var clip=holidayAssets.Get(outfit.FindClip(action,variant));return clip.Frame(clip.Count/2);}
            catch(Exception error){if(!HolidayAssetError(error))throw;RejectHolidayOutfit(outfit);return null;}
        }
        void DrawHolidayClouds(DrawingContext context)
        {
            if(!holidayTransition.Active)return;
            foreach(var cloud in holidayTransition.Clouds) {
                var color=Color.FromArgb((byte)(255*Math.Max(0,Math.Min(1,cloud.Opacity))),246,248,255);
                var brush=new RadialGradientBrush();brush.GradientStops.Add(new GradientStop(color,0));brush.GradientStops.Add(new GradientStop(color,.7));brush.GradientStops.Add(new GradientStop(Color.FromArgb(0,246,248,255),1));
                double radius=cloud.Radius*(1+cloud.Opacity);
                context.DrawEllipse(brush,null,new Point(cloud.X*image.ActualWidth,cloud.Y*image.ActualHeight),radius*image.ActualWidth,radius*image.ActualHeight);
            }
        }
        void ShowHolidayNotice(string line)
        {if(greeting==null)greeting=new GreetingBubbleWindow(this);greeting.ShowGreeting(line);}
        void DisposeHoliday()
        {CancelHolidayEffects();if(Holidays!=null){Holidays.RetrySave();Holidays.Changed-=HolidayChanged;}if(holidayAssets!=null)holidayAssets.Clear();}
        void HolidayTimeChanged(object sender,EventArgs args)
        {if(!closing)Dispatcher.BeginInvoke(new Action(delegate{if(!closing){CancelHolidayEffects();RefreshHoliday();UpdateFrame();}}));}
    }
}
