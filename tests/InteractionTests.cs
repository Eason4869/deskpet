using System;

using System.IO;
using System.Linq;

namespace Dafeiyu.Tests
{
    public static class InteractionTests
    {
        private static int passed;

        [STAThread] public static int Main()
        {
            try
            {
                AdditionalActions();
                DropRules();
                IdleSleepRules();
                NamedHoverChoices();
                ReferenceMatchedClips();
                OriginalVariantClips();
                GugaVariantClips();
                DropMotionRules();
                WanderRules();
                EmbeddedLegacyTiming();
                LoopsAndInterruption();
                CompletionAndReplay();
                FeedingSequence();
                ClockInputs();
                OptionalArtwork();
                HoverEntry();
                Console.WriteLine("Interaction tests: " + passed + " passed");
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL interaction: " + error.Message);
                return 1;
            }
        }

        private static void AdditionalActions()
        {
            foreach(string name in new[]{"stumble","special2"})
            {
                var animation=new InteractionAnimation();animation.Start(name);
                Check(animation.Active && animation.Name==name,name+" starts as a distinct interaction");
                animation.Advance(.35);Check(animation.Frame>0,name+" advances original poses");
                animation.Advance(10);Check(!animation.Active,name+" finishes and returns to idle");
                animation.Start(name);animation.Cancel();animation.Advance(10);
                Check(!animation.Active,name+" never resumes after cancellation");
            }
        }

        private static void DropRules()
        {
            int calls=0;double roll=.399999;var drop=new DropReaction(delegate{calls++;return roll;});
            Check(!drop.TryStumble(false,true,true,0) && calls==0,"click never rolls");
            Check(!drop.TryStumble(true,false,true,0) && calls==0,"paused or blocked drag never rolls");
            Check(!drop.TryStumble(true,true,false,0) && calls==0,"missing artwork lands normally without rolling");
            Check(drop.TryStumble(true,true,true,0),"probability just below 40 percent triggers");
            Check(!drop.TryStumble(true,true,true,9.999) && calls==1,"rapid repeated drag respects ten second cooldown");
            Check(drop.TryStumble(true,true,true,10),"cooldown expires exactly at ten seconds");
            roll=.4;Check(!drop.TryStumble(true,true,true,20),"40 percent boundary does not trigger");
            roll=.99;Check(!drop.TryStumble(true,true,true,20),"ordinary drops remain ordinary landing");
            roll=0;Check(drop.TryStumble(true,true,true,20),"failed roll does not consume cooldown");
            Check(!drop.TryStumble(true,true,true,Double.NaN),"invalid clock never triggers");
            var animation=new InteractionAnimation();animation.Start("stumble");
            for(int i=0;i<8;i++){Check(animation.Frame==i,"stumble visits frame "+i+" without skipping");animation.Advance(.125);}
            Check(!animation.Active,"stumble completes after eight frames");
            animation.Start("stumble");animation.Advance(.375);animation.Start("lifted");animation.Advance(2);
            Check(animation.Name=="lifted","new drag cancels stumble with no deferred recovery");
            var cfg=new PetSettings{StumbleEnabled=false};Check(!cfg.Copy().StumbleEnabled,"copy preserves disabled preference");
            Check(new PetSettings().StumbleEnabled,"new settings enable stumble by default");
        }

        private static void IdleSleepRules()
        {
            int rolls=0;double sample=.6999;var sleep=new IdleSleepSchedule(0,delegate{rolls++;return sample;});
            Check(!sleep.TrySleep(299,299,true,true) && rolls==0,"system idle below five minutes stays awake");
            Check(!sleep.TrySleep(900,0,false,true) && rolls==0,"busy pet cannot sleep");
            Check(!sleep.TrySleep(900,0,true,false) && rolls==0,"auto sleep switch disables probing");
            Check(sleep.TrySleep(300,300,true,true),"five minutes system idle and high probability can sleep");
            sleep.Touch(300);Check(!sleep.TrySleep(301,999,true,true),"waking resets both idle thresholds");
            Check(!sleep.TrySleep(599,999,true,true),"old system idle time cannot immediately put an awakened pet back to sleep");
            sample=.7;Check(!sleep.TrySleep(600,300,true,true),"seventy percent probability boundary stays awake");
            Check(!sleep.TrySleep(659,999,true,true) && rolls==2,"failed sleep attempt retries no more than once a minute");
            sample=0;Check(sleep.TrySleep(660,999,true,true),"failed sleep attempt can retry at one minute");
            sleep.Touch(660);Check(!sleep.TrySleep(1559,0,true,true),"active computer and pet idle below fifteen minutes stays awake");
            Check(sleep.TrySleep(1560,0,true,true),"fifteen minutes without pet interaction can sleep even while computer remains active");
            sleep.Touch(1560);Check(!sleep.TrySleep(1800,Double.NaN,true,true),"failed OS idle query never invents inactivity");
            Check(sleep.TrySleep(2460,Double.NaN,true,true),"pet interaction timeout remains available when OS idle query fails");
        }

        private static void NamedHoverChoices()
        {
            string root=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"hover-catalog-"+Guid.NewGuid().ToString("N"));
            var catalog=new SkinCatalog(root);var day=new DateTime(2026,10,31);
            var firstNames=new[]{"傲娇扭头","摇尾欢呼","挥杖流光","太极光环"};
            var secondNames=new[]{"比心鼓励","泡泡环游","荧光咒语","全息键盘"};int index=0;
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei"})
            {
                var skin=catalog.Find(id);var choices=HoverActions.Choices(skin,null,day);
                Check(choices.Count==4,"ordinary hover has exactly four real choices "+id);
                Check(choices[0].Id=="jumping" && choices[1].Id=="waving" && choices[2].Id=="special" && choices[3].Id=="special2","hover pool preserves all requested choices "+id);
                Check(choices[2].Name==firstNames[index] && choices[3].Name==secondNames[index],"both unique actions have concrete distinct names "+id);index++;
                var holiday=new HolidayFixture();choices=HoverActions.Choices(skin,holiday,day);
                Check(choices.Count==5 && choices[4].Name=="南瓜彩蛋","active holiday appends exactly one fifth choice "+id);
                choices[4].Play(null);Check(holiday.Played,"holiday uses the same action playback route "+id);
                Check(HoverActions.Choices(skin,holiday,day.AddDays(1)).Count==4,"nonholiday has no placeholder action "+id);
            }
        }
        private static void ReferenceMatchedClips()
        {
            Check(!AdditionalInteractions.Supports(new SkinDefinition("xiaobei","自定义小贝","",null,false)),"matching custom ID cannot acquire bundled-only actions");
            Check(!AdditionalInteractions.Supports(new SkinDefinition("e-bao","自定义E包","",null,false)),"matching custom Ebao ID cannot replace imported artwork");
            var catalog=new SkinCatalog(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"clip-tests"));
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei"})
            {
                var skin=catalog.Find(id);var art=new AdditionalInteractions();
                foreach(string action in AdditionalInteractions.Actions)
                {
                    var clip=art.Get(skin,action);bool loop=action=="lifted" || action=="sleeping";
                    Check(clip!=null && clip.Count==8 && clip.Duration==1 && clip.Loop==loop,id+" eight-pose optimized clip "+action);
                    var animation=new InteractionAnimation();animation.Configure(skin);animation.Start(action);
                    for(int i=0;i<8;i++){Check(animation.Frame==i && clip.Sample(animation.Elapsed)==i,id+" synchronized pose "+action+i);animation.Advance(.125);}
                    Check(loop?animation.Active && animation.Frame==0:action=="feeding"?animation.Name=="happy":!animation.Active,id+" optimized completion "+action);
                }
                Check(art.Get(skin,"petting")==null && art.Get(skin,"poked")==null,id+" unrelated original gestures use original artwork");
            }
            var ebao=catalog.Find("e-bao");Check(new AdditionalInteractions().Get(ebao,"jumping").Count==8,"Ebao redraw has eight actual airborne poses");
        }
        private static void WanderRules()
        {
            Check(WanderSchedule.Next(10,0)==100,"occasional walking waits at least ninety seconds");
            Check(WanderSchedule.Next(10,1)==190,"occasional walking waits up to three minutes");
            Check(WanderSchedule.Next(10,.5)==145,"walking retry spreads across the full quiet interval");
        }
        private static void DropMotionRules()
        {
            var motion=new DropMotion();
            Check(!motion.Active,"drop displacement begins only at release");
            foreach(string variant in new[]{"landing","landing-original","stumble","stumble-original"})
            {
                motion.Start(100,208,700);Check(motion.Position==100,"release never lifts pet "+variant);
                motion.Advance(.1);Check(motion.Position>100 && motion.Position<124,"fall moves smoothly between endpoints "+variant);
                motion.Advance(.2);Check(!motion.Active && Math.Abs(motion.Position-124)<.001,"all four variants fall exactly 24 logical pixels "+variant);
            }
            motion.Start(100,416,900);motion.Advance(1);Check(motion.Position==148,"fall scales with pet size");
            motion.Start(490,208,700);motion.Advance(1);Check(motion.Position==492,"fall is clamped to available screen space");
            motion.Start(500,208,700);motion.Advance(1);Check(motion.Position==500,"release never moves upward even outside screen bounds");
            motion.Start(100,208,700);motion.Advance(.1);double interrupted=motion.Position;motion.Cancel();motion.Advance(1);Check(motion.Position==interrupted,"interruption freezes fall without deferred movement");
        }
        private static void GugaVariantClips()
        {
            var skin=new SkinCatalog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"guga-interaction-data")).Find("gugugaga");
            Check(skin.Id=="gugugaga","Guga catalog identity exists");
            var art=new AdditionalInteractions();
            foreach(string action in AdditionalInteractions.Actions.Concat(new[]{"jumping"})) {
                Check(AdditionalInteractions.Has(skin,action) && AdditionalInteractions.HasOriginal(skin,action),"Guga has both variants "+action);
                var first=art.Get(skin,action+"-original");var second=art.Get(skin,action);
                Check(first!=null && second!=null && first.Count==8 && second.Count==8,"Guga variants decode eight poses "+action);
                Check(first.Duration==1 && second.Duration==1,"Guga variants share eight FPS "+action);
            }
            Check(!AdditionalInteractions.HasOriginal(new SkinDefinition("gugugaga","导入咕嘎","",null,false),"lifted"),"matching imported Guga cannot borrow built-in variants");
            SkinManifest manifest;using(var input=File.OpenRead(Path.Combine(skin.Source,"pet.json")))manifest=SkinCatalog.ReadManifest(input);
            manifest.AlternateAnimations=null;manifest.ExtraAnimations.Remove("stumble");manifest.SpecialAction2=null;
            var standalone=new SkinDefinition("test-signature",skin.Source,manifest);
            Check(HoverActions.Choices(standalone,null,DateTime.Today).Any(a=>a.Id=="special2"),"second signature remains available independently of stumble and has a fallback name");
        }
        private static void OriginalVariantClips()
        {
            var catalog=new SkinCatalog(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"variant-tests"));
            foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei"})
            {
                var skin=catalog.Find(id);var art=new AdditionalInteractions();
                foreach(string action in new[]{"lifted","landing","feeding","happy","sleeping","special","special2","stumble"})
                {
                    var original=art.Get(skin,action+"-original");
                    Check(original!=null,"original variant remains available "+id+action);
                    int count=id=="xiaobei" || action=="special2" || action=="stumble"?8:4;
                    Check(original.Count==count && original.Duration==count/8.0,"original poses retain true frame count at 8 FPS "+id+action);
                    Check(original.Loop==(action=="lifted" || action=="sleeping"),"original loop semantics "+id+action);
                    var clock=new InteractionAnimation();clock.Configure(skin,true);clock.Start(action);
                    for(int i=0;i<count;i++){Check(clock.Frame==i && original.Sample(clock.Elapsed)==i,"old artwork and clock stay synchronized "+id+action+i);clock.Advance(.125);}
                    Check(original.Loop?clock.Frame==0 && clock.Active:action=="feeding"?clock.Name=="happy":!clock.Active,"old artwork completes or loops without switching versions "+id+action);
                }
            }
        }
        private sealed class HolidayFixture:IHolidayHoverActionProvider
        {
            public bool Played;
            public CompanionAction GetAction(SkinDefinition skin,DateTime date){return date.Month==10 && date.Day==31?new CompanionAction("holiday-pumpkin","南瓜彩蛋",p=>Played=true):null;}
        }

        private static void EmbeddedLegacyTiming()
        {
            foreach(string id in new[]{"e-bao","dafeiyu","marfu"})
            {
                var skin=new SkinDefinition(id,id,"","",true);
                var ordinary=skin.CreateAnimation();ordinary.Play("jumping",.7);ordinary.Advance(.699);
                Check(ordinary.State=="jumping",id+" legacy jump remains active before 0.7 seconds");
                ordinary.Advance(id=="e-bao"?.302:.002);Check(ordinary.State=="idle",id+" jump ends at its artwork duration");
                ordinary.Play("running-right",0);ordinary.Advance(.219);Check(ordinary.Frame==0,id+" legacy running holds each phase for 0.22 seconds");
                ordinary.Advance(.002);Check(ordinary.Frame==1,id+" legacy running advances at original boundary");
                var animation=new InteractionAnimation();animation.Configure(skin);
                string[] names={"petting","poked"};
                double[] seconds={1.6,.8};
                for(int i=0;i<names.Length;i++)
                {
                    animation.Start(names[i]);animation.Advance(seconds[i]/4-.001);Check(animation.Frame==0,id+" "+names[i]+" holds first original pose");
                    animation.Advance(.002);Check(animation.Frame==1,id+" "+names[i]+" advances to second original pose");
                    animation.Advance(seconds[i]*.75-.001);
                    Check(!animation.Active,id+" "+names[i]+" original duration and completion behavior");
                }
            }
        }
        private static void HoverEntry()
        {
            var hover=new HoverInteraction();
            Check(!hover.Update(false,true,true),"transparent area never triggers hover jump");
            Check(hover.Update(true,true,true) && hover.Active,"entering visible body triggers one jump");
            Check(!hover.Update(false,true,true) && hover.Active,"animation moving away from stationary cursor does not retrigger hover");
            Check(!hover.Update(true,true,true),"returning animation frame stays in same hover visit");
            hover.Update(false,false,true);Check(!hover.Active && hover.Update(true,true,true),"leaving pet bounds rearms the next hover visit");
            hover.Update(false,false,true);Check(!hover.Update(true,true,false),"busy or guided interaction suppresses hover jump");
            Check(!hover.Update(true,true,true),"ending busy state under stationary cursor does not cause delayed jump");
        }
        private static void LoopsAndInterruption()
        {
            var animation = new InteractionAnimation();
            Check(!animation.Active && !animation.IsSleeping, "initial state leaves ordinary animation in control");
            animation.Start("lifted");
            Check(animation.Active && animation.Row == 0 && animation.Frame == 0, "drag starts lifted artwork");
            animation.Advance(.31);
            Check(animation.Frame > 0, "lifted advances poses");
            animation.Advance(10000);
            Check(animation.Active && animation.Name == "lifted" && animation.Frame >= 0 && animation.Frame < 4, "long drag keeps lifted loop bounded");
            animation.Start("landing");
            Check(animation.Name == "landing" && animation.Frame == 0 && animation.Row == 1, "release interrupts lifted with landing");
            animation.Advance(10);
            Check(!animation.Active, "landing returns control to ordinary animation");
            animation.Start("sleeping");
            animation.Advance(10000);
            Check(animation.Active && animation.IsSleeping && animation.Row == 6, "sleep remains asleep beyond ordinary action duration");
            animation.Start("poked");
            Check(!animation.IsSleeping && animation.Name == "poked", "direct interaction wakes sleeping pet");
            animation.Cancel();
            animation.Advance(10);
            Check(!animation.Active && !animation.IsSleeping && animation.Name == "", "cancel clears interaction without later resuming");
        }

        private static void CompletionAndReplay()
        {
            string[] actions = { "landing", "petting", "poked", "happy", "special" };
            int[] rows = { 1, 2, 3, 5, 7 };
            for (int i = 0; i < actions.Length; i++)
            {
                var animation = new InteractionAnimation();
                animation.Start(actions[i]);
                Check(animation.Row == rows[i], actions[i] + " selects its artwork row");
                animation.Advance(.3);
                animation.Start(actions[i]);
                Check(animation.Frame == 0 && animation.Active, actions[i] + " can be replayed from first pose");
                bool[] visited = new bool[4];
                for (int tick = 0; tick < 500 && animation.Active; tick++)
                {
                    visited[animation.Frame] = true;
                    animation.Advance(.01);
                }
                Check(!animation.Active, actions[i] + " finishes without an idle override");
                Check(visited[0] && visited[1] && visited[2] && visited[3], actions[i] + " shows all four poses");
            }
        }

        private static void FeedingSequence()
        {
            var animation = new InteractionAnimation();
            animation.Start("feeding");
            Check(animation.Row == 4, "feeding selects catch/eat artwork");
            for (int i = 0; i < 500 && animation.Name == "feeding"; i++) animation.Advance(.01);
            Check(animation.Active && animation.Name == "happy" && animation.Row == 5, "finishing a meal triggers happiness");
            animation.Advance(10);
            Check(!animation.Active, "feeding happiness finishes once");
            animation.Start("feeding");
            animation.Advance(100);
            Check(!animation.Active, "long frame consumes feeding and happiness without replay");
            animation.Start("feeding");
            animation.Cancel();
            animation.Advance(100);
            Check(!animation.Active, "cancelled meal cannot trigger delayed happiness");
            var coarse = new InteractionAnimation();
            var fine = new InteractionAnimation();
            coarse.Start("feeding"); fine.Start("feeding");
            coarse.Advance(1.75);
            for (int i = 0; i < 175; i++) fine.Advance(.01);
            Check(coarse.Active == fine.Active && coarse.Name == fine.Name && coarse.Frame == fine.Frame, "feeding transition preserves elapsed time across frame rates");
            coarse.Start("feeding"); fine.Start("feeding");
            coarse.Advance(2.8);
            for (int i = 0; i < 28; i++) fine.Advance(.1);
            Check(!coarse.Active && !fine.Active, "exact meal-plus-happiness duration completes at different frame rates");
        }

        private static void ClockInputs()
        {
            var animation = new InteractionAnimation();
            animation.Start("petting");
            animation.Advance(-1); animation.Advance(Double.NaN); animation.Advance(Double.PositiveInfinity); animation.Advance(0);
            Check(animation.Active && animation.Frame == 0, "invalid time inputs cannot corrupt playback");
            animation.Start("sleeping");
            animation.Advance(Double.MaxValue);
            Check(animation.Active && animation.Frame >= 0 && animation.Frame < 4, "huge finite elapsed time does not overflow loop");
            animation.Start("lifted");
            for (int i = 0; i < 10; i++) animation.Advance(.1);
            Check(animation.Frame == 0, "accumulated decimal clock values wrap at the loop boundary");
            animation.Start("unknown");
            Check(!animation.Active, "unknown action safely releases animation");
            animation.Start(null);
            Check(!animation.Active, "null action is safe");
        }

        private static void OptionalArtwork()
        {
            Check(InteractionAtlas.LoadForSkin((string)null) == null && InteractionAtlas.LoadForSkin("skin-custom") == null, "unknown and imported ids retain base artwork");
            Check(InteractionAtlas.LoadForSkin((SkinDefinition)null) == null, "no selected skin is safe");
            var imported = new SkinDefinition("e-bao", "Imported", "unused.png", null, false);
            Check(InteractionAtlas.LoadForSkin(imported) == null, "imported skins never borrow built-in artwork even with matching id");
            foreach (string id in new[] { "e-bao", "dafeiyu", "marfu" })
            {
                var atlas = InteractionAtlas.LoadForSkin(id);
                if (atlas == null) { Console.WriteLine("Optional interaction atlas absent: " + id); continue; }
                Check(atlas.FrameWidth == 480 && atlas.FrameHeight == 520, id + " interaction frame dimensions");
                for (int row = 0; row < 8; row++) for (int col = 0; col < 4; col++)
                {
                    var frame = atlas.GetFrame(row, col);
                    Check(frame.IsFrozen && frame.PixelWidth == 480 && frame.PixelHeight == 520, id + " immutable detached frame " + row + "/" + col);
                    bool visible = false, transparent = false;
                    for (int y = 0; y < 520; y += 4) for (int x = 0; x < 480; x += 4)
                    {
                        byte alpha = atlas.AlphaAt(row, col, x, y);
                        visible |= alpha > 16; transparent |= alpha == 0;
                    }
                    Check(visible && transparent, id + " frame contains character and transparent margin " + row + "/" + col);
                }
                Check(atlas.AlphaAt(0, 0, -1, 0) == 0 && atlas.AlphaAt(0, 0, 480, 0) == 0, "outside frame is click-through");
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            passed++;
        }
    }
}
