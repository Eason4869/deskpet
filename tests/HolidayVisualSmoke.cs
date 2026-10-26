using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    public static class HolidayVisualSmoke
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
        [STAThread]public static int Main()
        {
            try {
                var type=typeof(PetWindow).Assembly.GetType("Dafeiyu.HolidayVisualAssets");Check(type!=null,"outfit frame resolver exists");
                byte[] pixels=new byte[3840*520*4];for(int pose=0;pose<8;pose++)pixels[(120*3840+pose*480+80)*4+3]=255;
                var bitmap=BitmapSource.Create(3840,520,96,96,PixelFormats.Bgra32,null,pixels,3840*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));byte[] png;using(var output=new MemoryStream()){encoder.Save(output);png=output.ToArray();}
                Func<OutfitClipDefinition,Stream> open=spec=>new MemoryStream(png,false);
                object assets=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{open},null);
                var specification=new OutfitClipDefinition("test.png",8,8,4,false,480,520,0,0,"idle","default");
                var get=type.GetMethod("Get",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);var clip=(MotionClip)get.Invoke(assets,new object[]{specification});
                Check(clip.Count==8 && clip.Width==480 && clip.Height==520,"outfit dimensions and count retained");
                Check(clip.AlphaAt(0,80,120)==255 && clip.AlphaAt(0,81,120)==0,"current frame alpha mask matches costume pixels");
                var frame=new FormatConvertedBitmap(clip.Frame(0),PixelFormats.Bgra32,null,0);byte[] sample=new byte[480*520*4];frame.CopyPixels(sample,480*4,0);Check(sample[(120*480+80)*4+3]==clip.AlphaAt(0,80,120),"drawing and transparent hit use the same decoded frame");
                Check(Object.ReferenceEquals(clip,get.Invoke(assets,new object[]{specification})),"current decoded outfit frame is cached");
                VerifyPet(png);
                return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static void VerifyPet(byte[] png)
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"holiday-visual-"+Guid.NewGuid().ToString("N"));var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try {
                var settings=new PetSettings{SkinId="e-bao",FollowCursor=false,Wander=false,GreetingEnabled=false,AutoUpdate=false,SaveChatHistory=false};foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);
                // This test owns an artificial holiday catalog. Keep its embedded
                // role fixed; startup acquisition/hot reload has dedicated coverage.
                pet.Skin.FixtureEmbedded=true;
                var character=pet.Skin;var animation=pet.Animation;animation.Advance(.27);double phase=animation.Elapsed;
                var specs=IllustratedPack.Actions.Concat(new[]{"special2","stumble"}).Select(action=>new OutfitClipDefinition(action+".png",8,8,4,true,480,520,0,0,action,"default")).ToArray();
                var outfit=new OutfitDefinition("e-bao","halloween","halloween","夹具服饰","1",true,false,specs,null,new OutfitClipDefinition("egg.png",8,8,4,false,480,520,0,0,"egg","default"),null,"夹具动作");
                var state=new HolidayState();state.Unlock("e-bao","halloween");var controller=new HolidayController(new HolidayCalendar(),new HolidayCatalog(new[]{outfit}),state,s=>{});
                pet.HolidayLocalTime=()=>new DateTime(2026,10,31,12,0,0);pet.InitializeHoliday(controller,spec=>new MemoryStream(png,false));
                typeof(PetWindow).GetMethod("UpdateFrame",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{0d});
                var shown=(MotionClip)typeof(PetWindow).GetField("shownMotion",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);var image=(Image)pet.Content;
                int sample=(int)typeof(PetWindow).GetField("shownSample",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                Check(shown!=null && Object.ReferenceEquals(image.Source,shown.Frame(sample)),"PetWindow commits outfit bitmap and hit mask together");
                Check(Object.ReferenceEquals(character,pet.Skin) && Object.ReferenceEquals(animation,pet.Animation) && Math.Abs(animation.Elapsed-phase)<1e-9,"outfit swap preserves persona and ordinary action phase");
                controller.SetEnabled(false);Check(controller.EffectiveOutfit==null && Math.Abs(animation.Elapsed-phase)<1e-9,"disabling wardrobe restores visual without restarting action");
                state=new HolidayState();controller=new HolidayController(new HolidayCalendar(),new HolidayCatalog(new[]{outfit}),state,s=>{});pet.HolidayLocalTime=()=>new DateTime(2026,10,30,23,59,59);pet.PlayAction("waiting",0);pet.InitializeHoliday(controller,spec=>new MemoryStream(png,false));pet.Show();Pump(.12);
                pet.PlayAction("waiting",0);pet.HolidayLocalTime=()=>new DateTime(2026,10,31,0,0,0);pet.RefreshHoliday();
                var transition=(HolidayTransition)typeof(PetWindow).GetField("holidayTransition",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                Check(controller.EffectiveOutfit!=null && pet.Animation.State=="waiting" && !transition.Active,"midnight during ordinary busy action applies outfit without cloud or action reset");pet.PlayAction("idle",0);
                typeof(PetWindow).GetField("random",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,new ZeroRandom());controller.LeaveHover();pet.RefreshHoliday();
                var begin=typeof(PetWindow).GetMethod("TryHolidayHover",BindingFlags.Instance|BindingFlags.NonPublic);
                bool hoverStarted=(bool)begin.Invoke(pet,null);if(!hoverStarted)Console.WriteLine("hover fixture gate: visible="+pet.IsPetVisible+", state="+pet.Animation.State+", can="+typeof(PetWindow).GetProperty("HolidayCanAnimate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet,null)+", failed="+Field(pet,"holidayFailedKey")+", transition="+transition.Active+", ready="+outfit.Ready+", effective="+(controller.EffectiveOutfit!=null));
                Check(hoverStarted,"WPF adapter starts fixture egg from hover path");WaitVisual(pet,()=>((HolidayPlayback)Field(pet,"holidayPlayback")).FirstFrameRendered,"interrupted egg first frame");Check(pet.HolidayPlaying,"egg owns animation until interruption");
                pet.PlayAction("waiting",0);Check(!pet.HolidayPlaying && !controller.WardrobeVisible,"business action immediately cancels egg without unlocking");
                pet.PlayAction("idle",0);controller.LeaveHover();pet.RefreshHoliday();Check(!(bool)begin.Invoke(pet,null),"interrupted first rendered frame retains sixty second cooldown");
                controller=new HolidayController(new HolidayCalendar(),new HolidayCatalog(new[]{outfit}),new HolidayState(),s=>{});pet.InitializeHoliday(controller,spec=>new MemoryStream(png,false));
                // Native frames cap their delta; a delayed dispatcher must not turn
                // the wall-clock estimate into permission to start a second animation.
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send,new Action(()=>System.Threading.Thread.Sleep(1100)));
                WaitVisual(pet,()=>!transition.Active,"fresh outfit transition");controller.LeaveHover();pet.RefreshHoliday();
                Check((bool)begin.Invoke(pet,null),"fresh fixture starts complete egg");WaitVisual(pet,()=>!pet.HolidayPlaying,"complete egg playback");Check(!pet.HolidayPlaying && controller.WardrobeVisible,"only full WPF playback unlocks the wardrobe");
                VerifyUnavailable(pet,png,root);
            }finally {if(pet!=null)pet.Close();var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);app.Shutdown();}
        }
        static void WaitVisual(PetWindow pet,Func<bool> completed,string phase)
        {
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(!completed()) {
                if(DateTime.UtcNow>=deadline) {
                    var transition=(HolidayTransition)Field(pet,"holidayTransition");var playback=(HolidayPlayback)Field(pet,"holidayPlayback");
                    throw new TimeoutException(phase+": visible="+pet.IsPetVisible+", state="+pet.Animation.State+", transition="+transition.Active+", progress="+transition.Progress+", playing="+pet.HolidayPlaying+", firstFrame="+playback.FirstFrameRendered+", frame="+playback.Frame+", failed="+Field(pet,"holidayFailedKey"));
                }
                Pump(.01);
            }
        }
        static object Field(PetWindow pet,string name){return typeof(PetWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);}
        static void SetField(PetWindow pet,string name,object value){typeof(PetWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,value);}
        static void Render(PetWindow pet){typeof(PetWindow).GetMethod("UpdateFrame",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{0d});}
        static void OriginalFrame(PetWindow pet,HolidayController controller,string reason)
        {
            Check(controller.EffectiveOutfit==null && Field(pet,"holidayCommitted")==null && Field(pet,"holidayTarget")==null && !pet.HolidayPlaying && !((HolidayTransition)Field(pet,"holidayTransition")).Active,reason+" clears desired and local visual ownership");
            var motion=Field(pet,"shownMotion") as MotionClip;int row=(int)Field(pet,"shownRow"),col=(int)Field(pet,"shownCol"),sample=(int)Field(pet,"shownSample");
            var expected=motion!=null?motion.Frame(sample):(bool)Field(pet,"shownSupplemental")?((InteractionAtlas)Field(pet,"interactionAtlas")).GetFrame(row,col):((SpriteAtlas)Field(pet,"atlas")).GetFrame(row,col);
            Check(Object.ReferenceEquals(((Image)pet.Content).Source,expected),reason+" publishes original picture and matching alpha frame");
        }
        static HolidayCatalog CorruptFixture(string root,byte[] normal,string broken)
        {
            Directory.CreateDirectory(root);byte[] gaze=Png(1920,2080),preview=Png(480,520);
            var clips=new List<object>();
            foreach(string action in IllustratedPack.Actions.Concat(new[]{"stumble","special2"})) {
                string path=action+".png";File.WriteAllBytes(Path.Combine(root,path),broken==action?BadIdat(normal):normal);
                clips.Add(new{action=action,variant="default",path=path,frame_count=8,columns=8,fps=4,loop=action=="idle" || action=="lifted" || action=="sleeping",frame_width=480,frame_height=520});
            }
            foreach(string action in new[]{"lifted","landing","feeding","happy","sleeping","special","special2","stumble","jumping"})clips.Add(new{action=action,variant="alternate",path=action+".png",frame_count=8,columns=8,fps=4,loop=action=="lifted" || action=="sleeping",frame_width=480,frame_height=520});
            File.WriteAllBytes(Path.Combine(root,"gaze.png"),broken=="gaze"?BadIdat(gaze):gaze);File.WriteAllBytes(Path.Combine(root,"egg.png"),broken=="egg"?BadIdat(normal):normal);File.WriteAllBytes(Path.Combine(root,"preview.png"),preview);
            var json=new JavaScriptSerializer();
            File.WriteAllText(Path.Combine(root,"catalog.json"),json.Serialize(new{schema_version=1,outfits=new[]{new{character_id="e-bao",outfit_id="halloween",holiday_id="halloween",name="运行时损坏夹具",version="1",ready=true,greetings_ready=true,definition="outfit.json"}}}));
            File.WriteAllText(Path.Combine(root,"outfit.json"),json.Serialize(new{schema_version=1,character_id="e-bao",outfit_id="halloween",holiday_id="halloween",name="运行时损坏夹具",version="1",clips=clips,gaze=new{path="gaze.png",frame_count=16,columns=4,fps=4,loop=false,frame_width=480,frame_height=520},easter_egg=new{path="egg.png",frame_count=8,columns=8,fps=4,loop=false,frame_width=480,frame_height=520},preview="preview.png",action_name="夹具彩蛋"}));
            var catalog=new HolidayCatalog(root);Check(catalog.Find("e-bao","halloween")!=null && catalog.Find("e-bao","halloween").Ready,"correct PNG chunks and CRC pass catalog validation for corrupt "+broken+" IDAT");return catalog;
        }
        static void VerifyUnavailable(PetWindow pet,byte[] png,string root)
        {
            foreach(string broken in new[]{"feeding","gaze","preview","egg","active-preview"}) {
                pet.Settings.FollowCursor=false;pet.PlayAction("idle",0);
                string folder=Path.Combine(root,"corrupt-"+broken);var catalog=CorruptFixture(folder,png,broken=="preview" || broken=="active-preview"?"feeding":broken);var outfit=catalog.Find("e-bao","halloween");
                var state=new HolidayState();bool owned=broken!="egg" && broken!="active-preview";if(owned){state.Unlock("e-bao","halloween");state.ForCharacter("e-bao").PreferredOutfitId="halloween";}
                int writes=0;var controller=new HolidayController(new HolidayCalendar(),catalog,state,s=>writes++);pet.InitializeHoliday(controller,spec=>File.OpenRead(Path.Combine(folder,spec.Path)));WaitVisual(pet,()=>!((HolidayTransition)Field(pet,"holidayTransition")).Active,"valid outfit transition before "+broken);Render(pet);
                Check(controller.EffectiveOutfit==outfit && Field(pet,"holidayCommitted")==outfit,"valid idle commits before later "+broken+" decoder failure");long revision=controller.Revision;var greeting=controller.CreateGreetingContext(0);int before=writes;HolidayPlaybackTicket oldTicket=null;int notifications=0;
                controller.Changed+=delegate {if(!outfit.Ready){notifications++;OriginalFrame(pet,controller,broken+" synchronous Changed");}};
                using(var panel=new HolidayOutfitPanel(controller)) {
                    if(broken=="feeding")pet.PlayInteraction("feeding");
                    else if(broken=="gaze") {pet.Settings.FollowCursor=true;((HoverInteraction)Field(pet,"hover")).Update(false,false,false);SetField(pet,"lastCursorMove",((System.Diagnostics.Stopwatch)Field(pet,"watch")).Elapsed.TotalSeconds);Render(pet);}
                    else if(broken=="preview")pet.GetActionPreview("feeding",true);
                    else {
                        SetField(pet,"random",new ZeroRandom());controller.LeaveHover();pet.RefreshHoliday();bool started=(bool)typeof(PetWindow).GetMethod("TryHolidayHover",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                        if(broken=="active-preview"){Check(started,"valid egg begins before corrupt preview");WaitVisual(pet,()=>((HolidayPlayback)Field(pet,"holidayPlayback")).FirstFrameRendered,"active preview egg first frame");oldTicket=((HolidayPlayback)Field(pet,"holidayPlayback")).Ticket;Check(oldTicket!=null && oldTicket.FirstFrameRendered,"egg has rendered before costume rejection");pet.GetActionPreview("feeding",true);}
                        else Check(!started,"corrupt egg preparation never starts playback");
                    }
                    if(outfit.Ready)Console.WriteLine("rejection fixture: "+broken+", revision="+controller.Revision+"/"+revision+", failed="+Field(pet,"holidayFailedKey")+", interaction="+((InteractionAnimation)Field(pet,"interaction")).Name+", committed="+(Field(pet,"holidayCommitted")!=null)+", shown="+(Field(pet,"shownMotion")!=null));
                    Check(!outfit.Ready && controller.Revision>revision,broken+" decode rejection reaches shared availability and revision");OriginalFrame(pet,controller,broken);
                    Check(!controller.IsGreetingCurrent(greeting) && controller.CreateGreetingContext(0).OutfitId==null,broken+" invalidates stale greeting and describes original outfit");
                    Check(!controller.SelectOwnedOutfit("halloween") && !controller.OwnedOutfits.Any(o=>o.Ready),broken+" disables the owned outfit picker entry");
                    if(owned){var picker=panel.Children.OfType<ComboBox>().Single();Check(picker.Items.Cast<object>().Any(choice=>choice.ToString().Contains("暂不可用") && !(bool)choice.GetType().GetProperty("Available").GetValue(choice,null)),broken+" updates the already open picker to a disabled unavailable entry");}
                    if(owned)Check(state.ForCharacter("e-bao").PreferredOutfitId=="halloween" && state.ForCharacter("e-bao").UnlockedOutfits.Contains("halloween"),broken+" retains saved preference and ownership");
                    if(oldTicket!=null){Check(!controller.CompleteHover(oldTicket),"late completed callback cannot unlock rejected outfit");Pump(2.4);}
                    Check(writes==before && !controller.PersistencePending && controller.UnlockNotice==null,broken+" rejection and interrupted playback never save or announce an unlock");
                    if(!owned)Check(!state.WardrobeDiscovered && state.ForCharacter("e-bao").UnlockedOutfits.Count==0,broken+" never discovers wardrobe from invalid playback");
                    long unchanged=controller.Revision;pet.RefreshHoliday();Render(pet);Check(controller.Revision==unchanged && !controller.ReportUnavailable(outfit) && notifications==1,broken+" repeated rejection and refresh are stable without notification recursion");
                }
            }
            pet.Settings.FollowCursor=false;
        }
        static byte[] Png(int width,int height)
        {byte[] pixels=new byte[width*height*4];for(int y=120;y<height;y+=520)for(int x=80;x<width;x+=480)pixels[(y*width+x)*4+3]=255;var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));using(var output=new MemoryStream()){encoder.Save(output);return output.ToArray();}}
        static int U32(byte[] bytes,int start){return (bytes[start]<<24)|(bytes[start+1]<<16)|(bytes[start+2]<<8)|bytes[start+3];}
        static byte[] BadIdat(byte[] png)
        {
            var broken=(byte[])png.Clone();int at=8;bool found=false;
            while(at<broken.Length-12){int length=U32(broken,at);if(System.Text.Encoding.ASCII.GetString(broken,at+4,4)=="IDAT") {for(int i=0;i<length;i++)broken[at+8+i]=0;broken[at+8]=0x78;broken[at+9]=0x9c;broken[at+10]=0x07;uint crc=0xffffffff;for(int i=at+4;i<at+8+length;i++){crc^=broken[i];for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}crc^=0xffffffff;for(int i=0;i<4;i++)broken[at+8+length+i]=(byte)(crc>>(24-8*i));found=true;}at+=length+12;}
            if(!found)throw new Exception("PNG fixture lacks IDAT");return broken;
        }
        sealed class ZeroRandom:Random {public override double NextDouble(){return 0;}}
        static void Pump(double seconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    }
}
