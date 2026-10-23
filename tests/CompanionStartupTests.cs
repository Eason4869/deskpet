using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class CompanionStartupTests
    {
        static int checks;
        static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            string data=Path.Combine(Path.GetTempPath(),"dp-reopen-"+Guid.NewGuid().ToString("N").Substring(0,8));
            try
            {
                using(var fixture=new OfficialCompanionFixture())
                using(var transport=fixture.Transport())
                using(var downloads=new OfficialCompanionDownloads(data,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,new WpfOfficialNativeValidator().ValidateAsync))
                {
                    fixture.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=WpfOfficialCompanionTests.NativePackage("dafeiyu",100);
                    Task.Run(()=>downloads.EnsureAsync("dafeiyu",new OfficialDownloadRouting(false,"",false,""),OfficialCompanionCheckMode.Cached,null,CancellationToken.None)).GetAwaiter().GetResult();
                    var store=new SettingsStore(Path.Combine(data,"settings.json"));
                    var settings=new PetSettings{SkinId="dafeiyu",SelectedCompanionKey="official:dafeiyu",OnboardingState="skipped",AutoUpdate=false,GreetingEnabled=false,FollowCursor=false,Wander=false,SaveChatHistory=false};
                    store.Save(settings);int requests=fixture.Requests.Count;
                    pet=new PetWindow(null,store.Load(),store,null);
                    Check(pet.Skin.BuiltinCharacterId=="dafeiyu"&&!pet.Skin.IsPlaceholder,"startup restores installed last companion before showing Ebao");
                    Check(!pet.Skin.Embedded,"startup uses installed package, not test-only embedded artwork");
                    Check(pet.Settings.SelectedCompanionKey=="official:dafeiyu"&&pet.Settings.SkinId==pet.Skin.Id,"selection and active persona remain the saved companion");
                    Check(fixture.Requests.Count==requests,"restore does not require another network request");
                    pet.SaveSettings();pet.Close();pet=null;
                    pet=new PetWindow(null,store.Load(),store,null);
                    Check(pet.Skin.BuiltinCharacterId=="dafeiyu","exit and reopen retain the same companion again");
                    pet.Close();pet=null;
                    File.WriteAllText(Path.Combine(data,"official-companions","dafeiyu","current.txt"),"invalid-pointer");
                    pet=new PetWindow(null,store.Load(),store,null);
                    Check(pet.Skin.BuiltinCharacterId=="e-bao"&&pet.Settings.PendingOfficialCompanionId=="dafeiyu","damaged cache falls back safely while retaining restore intent");
                }
                Console.WriteLine("CompanionStartupTests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL CompanionStartupTests: "+error);return 1;}
            finally{if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
