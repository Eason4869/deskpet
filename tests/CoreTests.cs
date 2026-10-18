using System;
using System.IO;
using System.Windows;

namespace Dafeiyu.Tests
{
    public static class CoreTests
    {
        private static int passed;
        private static int failed;
        public static int Main()
        {
            Test("eightfold samples preserve original animation time", delegate {
                var original=new AnimationPlayer(); var smooth=new AnimationPlayer(8);
                foreach(string state in AnimationPlayer.States) {
                    original.Play(state,0); smooth.Play(state,0);
                    for(int tick=0;tick<400;tick++) {
                        original.Advance(1.0/60); smooth.Advance(1.0/60);
                        Equal(original.Frame,smooth.Frame/8);
                    }
                }
                smooth.Play("idle",0);smooth.Advance(.04);Assert(smooth.Frame>0,"intermediate frame missing");
                smooth.Advance(Double.PositiveInfinity);Assert(smooth.Frame<48,"invalid clock corrupted frame");
                smooth.Play("jumping",.7);smooth.Advance(.8);Equal("idle",smooth.State);
            });
            Test("gaze takes shortest wrap route and ignores center noise", delegate {
                var gaze=new GazeController();gaze.Reset(15.5);gaze.Advance(0,-100,.02);
                Assert(gaze.Direction>15.5,"wrap moved the long way");
                double before=gaze.Direction;gaze.Advance(2,2,.2);Equal(before,gaze.Direction);
                gaze.Advance(100,0,Double.NaN);Equal(before,gaze.Direction);
                var a=new GazeController();var b=new GazeController();a.Reset(0);b.Reset(0);
                for(int i=0;i<60;i++)a.Advance(100,0,1.0/60);
                for(int i=0;i<30;i++)b.Advance(100,0,1.0/30);
                Assert(Math.Abs(a.Direction-b.Direction)<1e-9,"gaze depends on refresh rate");
            });
            Test("new installs default to Ebao without overriding saved selections", delegate {
                Equal("e-bao",new PetSettings().SkinId);
                var saved=new PetSettings {SkinId="dafeiyu"};saved.Normalize();Equal("dafeiyu",saved.SkinId);
                var empty=new PetSettings {SkinId=""};empty.Normalize();Equal("e-bao",empty.SkinId);
            });
            Test("cursor cardinals use screen coordinates", delegate {
                Equal(0, PetMath.LookDirection(0, -100));
                Equal(4, PetMath.LookDirection(100, 0));
                Equal(8, PetMath.LookDirection(0, 100));
                Equal(12, PetMath.LookDirection(-100, 0));
                Equal(-1, PetMath.LookDirection(0, 0));
            });
            Test("cursor diagonals wrap evenly", delegate {
                Equal(1, PetMath.LookDirection(38, -92));
                Equal(3, PetMath.LookDirection(92, -38));
                Equal(6, PetMath.LookDirection(70, 70));
                Equal(10, PetMath.LookDirection(-70, 70));
                Equal(13, PetMath.LookDirection(-92, -38));
                Equal(15, PetMath.LookDirection(-38, -92));
            });
            Test("negative-coordinate monitor remains reachable", delegate {
                Point p = PetMath.ClampPosition(-2500, 1200, 240, 260, new Rect(-1920, 0, 1920, 1040));
                Equal(-1920.0, p.X); Equal(780.0, p.Y);
                p = PetMath.ClampPosition(20, -5, 240, 260, new Rect(-1920, 0, 1920, 1040));
                Equal(-240.0, p.X); Equal(0.0, p.Y);
            });
            Test("oversized pet still anchors inside a small monitor", delegate {
                Point p = PetMath.ClampPosition(500, 500, 500, 500, new Rect(100, 20, 200, 200));
                Equal(100.0, p.X); Equal(20.0, p.Y);
            });
            Test("short action returns to idle without stale frame", delegate {
                AnimationPlayer player = new AnimationPlayer();
                player.Play("jumping", 0.7); player.Advance(0.31);
                Equal("jumping", player.State); Assert(player.Frame >= 1 && player.Frame < 5, "jump must animate");
                player.Advance(0.6); Equal("idle", player.State); Equal(0, player.Frame);
            });
            Test("timed jump holds landing instead of wrapping before idle", delegate {
                foreach(int multiplier in new[]{1,8}) {
                    var player=new AnimationPlayer(multiplier);
                    foreach(double at in new[]{.65,.675,.699}) {
                        player.Play("jumping",.7);player.Advance(at);
                        Equal("jumping",player.State);Equal(5*multiplier-1,player.Frame);
                    }
                    player.Play("jumping",.7);player.Advance(.7);Equal("idle",player.State);Equal(0,player.Frame);
                    player.Play("jumping",0);player.Advance(.65);Equal(0,player.Frame);
                    player.Play("waving",2);player.Advance(.65);Equal("waving",player.State);
                    Assert(player.Frame<multiplier,"timed waving must still loop");
                }
            });
            Test("looped action advances within its row", delegate {
                AnimationPlayer player = new AnimationPlayer();
                player.Play("running-right", 0); player.Advance(0.36);
                Equal("running-right", player.State); Equal(3, player.Frame);
                player.Advance(2.0); Assert(player.Frame >= 0 && player.Frame < 8, "frame wrapped");
            });
            Test("invalid action fails without corrupting animation", delegate {
                AnimationPlayer player = new AnimationPlayer();
                try { player.Play("missing", 1); throw new Exception("missing action accepted"); }
                catch (ArgumentException) { }
                Equal("idle", player.State);
            });
            Test("successful context retains complete turns before budget selection", delegate {
                ConversationLog log = new ConversationLog();
                for (int i = 0; i < 12; i++) log.Commit("u" + i, "a" + i);
                Equal(24, log.Messages.Count); Equal("u0", log.Messages[0].Content);
                var request = log.ForRequest("persona", "new question");
                Equal(26, request.Count); Equal("system", request[0].Role); Equal("new question", request[25].Content);
                Equal(24, log.Messages.Count); log.Clear(); Equal(0, log.Messages.Count);
            });
            Test("settings persist without plaintext secrets", delegate {
                string path = Temp("settings.json");
                SettingsStore store = new SettingsStore(path);
                PetSettings settings = new PetSettings();
                settings.Text.ApiKey = "test-secret-only";
                settings.Text.BaseUrl = "https://example.com/v1";
                settings.Text.Model = "custom-model"; settings.Volume = 35; settings.Scale = 1.5;
                store.Save(settings);
                Assert(!File.ReadAllText(path).Contains("test-secret-only"), "secret is stored in plaintext");
                PetSettings loaded = store.Load(); Equal("test-secret-only", loaded.Text.ApiKey);
                Equal("custom-model", loaded.Text.Model); Equal(35, loaded.Volume); Equal(1.5, loaded.Scale);
            });
            Test("damaged configuration is preserved with safe defaults", delegate {
                string path = Temp("broken.json"); File.WriteAllText(path, "{ invalid configuration");
                SettingsStore store = new SettingsStore(path); PetSettings loaded = store.Load();
                Equal(1.25, loaded.Scale); Equal(50, loaded.Volume); Assert(!loaded.Wander, "wander starts disabled");
                Assert(Directory.GetFiles(Path.GetDirectoryName(path), "broken.json.invalid-*").Length == 1, "broken file was not preserved");
                Assert(!String.IsNullOrEmpty(store.Warning), "missing configuration warning");
            });
            Test("unsafe numeric settings normalize", delegate {
                PetSettings settings = new PetSettings(); settings.Scale = Double.NaN; settings.Volume = 500;
                settings.X = Double.PositiveInfinity; settings.Y = -400; settings.Normalize();
                Equal(1.25, settings.Scale); Equal(100, settings.Volume); Assert(!settings.X.HasValue, "bad position retained");
                Equal(-400.0, settings.Y.Value);
            });
            Test("legacy settings cannot redirect the official update repository", delegate {
                PetSettings settings = new PetSettings { RepositoryUrl = "https://github.com/other-owner/other-repo" };
                settings.Normalize(); Equal("https://github.com/Eason4869/deskpet", settings.RepositoryUrl);
            });
            Console.WriteLine("Core tests: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
        private static string Temp(string name)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); return Path.Combine(folder, name);
        }
        private static void Test(string name, Action run)
        {
            try { run(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual) { if (!Object.Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual); }
    }
}
