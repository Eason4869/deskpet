using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.Serialization.Json;
using System.Collections.Generic;
namespace Dafeiyu.Tests
{
    // Compiled only into the unique AppId/mutex Inno fixture, never the app.
    public static class UninstallInstalledUi
    {
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{var e=root as T;if(e!=null&&e.Name==name)return e;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;}
        static void Click(Button button){if(button==null)throw new Exception("missing installed UI action");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
        [STAThread]public static int Main(string[] args)
        {
            string root=AppDomain.CurrentDomain.BaseDirectory,data=Path.Combine(root,"data"),mode=args[0];Directory.CreateDirectory(data);
            using(var installation=new Mutex(false,ReleaseInfo.InstallationMutex)){
                if(mode=="holder"){File.WriteAllText(Path.Combine(data,"holder-ready"),"ready");while(!File.Exists(Path.Combine(data,"holder-exit")))Thread.Sleep(25);return 0;}
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
                app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){File.WriteAllText(Path.Combine(data,"fixture-error"),e.Exception.ToString());e.Handled=true;app.Shutdown(2);};
                try{
                    var store=new SettingsStore(Path.Combine(data,"settings.json"));var cfg=File.Exists(Path.Combine(data,"settings.json"))?store.Load():new PetSettings();
                    cfg.AutoUpdate=false;cfg.GreetingEnabled=false;cfg.Wander=false;cfg.FollowCursor=false;cfg.OnboardingState="completed";foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                    if(!cfg.TextProviders.Any(p=>p.Id=="uninstall-fixture")){var provider=new TextProviderSettings{Id="uninstall-fixture",Name="Owned fixture",Connection=new ConnectionSettings{BaseUrl="https://fixture.invalid",Model="owned"}};provider.Connection.ApiKey="owned-test-key";cfg.TextProviders.Add(provider);}cfg.ActiveTextProviderId="uninstall-fixture";store.Save(cfg);
                    string ciphertext=cfg.TextProviders.First(p=>p.Id=="uninstall-fixture").Connection.EncryptedKey;
                    pet=new PetWindow(null,cfg,store,null);pet.Closed+=async delegate{await Program.ShutdownAfterCloseAsync(app,pet);};
                    pet.Loaded+=async delegate{
                        try{
                            await pet.AgentJobs.InitializeAsync();
                            // Earn a real holiday artifact from the current embedded original asset.
                            var catalog=new HolidayCatalog(new[]{new OutfitDefinition("e-bao","halloween-2026","halloween","fixture","1.0.0",true,false,new OutfitClipDefinition[0],null,null,null,null)});
                            var result=await pet.HolidayKeepsakes.ObserveAsync(new HolidayKeepsakeEncounter("official:e-bao","e-bao","Kept companion",new RelationshipTime(new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),TimeSpan.Zero)),catalog);
                            Assert(result.Status==HolidayKeepsakeObservationStatus.Completed&&result.Results.Any(r=>r.Status==HolidayKeepsakeStatus.Saved||r.Status==HolidayKeepsakeStatus.AlreadyOwned),"real holiday keepsake capture failed");
                            var about=new AboutPanel(pet);var host=new Window{Content=about,Width=700,Height=680};host.Show();
                            bool found=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
                            timer.Tick+=async delegate{
                                var dialog=app.Windows.OfType<UninstallConfirmationWindow>().FirstOrDefault();if(dialog==null)return;timer.Stop();found=true;
                                try{
                                    if(mode=="cancel"){Click(Find<Button>(dialog,"CancelUninstall"));return;}
                                    System.ComponentModel.CancelEventHandler veto=delegate(object s,System.ComponentModel.CancelEventArgs e){e.Cancel=true;};
                                    if(mode=="veto")pet.Closing+=veto;
                                    IDisposable busy=mode=="busy"?pet.DataMaintenance.EnterActivity():null;
                                    FileStream held=mode=="savefail"?new FileStream(Path.Combine(data,"settings.json.tmp"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None):null;
                                    try{Click(Find<Button>(dialog,"ConfirmUninstall"));await dialog.PendingOperation;}
                                    finally{if(held!=null)held.Dispose();if(busy!=null)busy.Dispose();}
                                    if(mode=="busy"||mode=="savefail"){Assert(pet.IsVisible&&!pet.DataMaintenance.IsMaintenanceActive,"failure must keep host alive");string expected=mode=="busy"?TextResources.Get("dataMaintenance.busy"):TextResources.Get("uninstall.prepareFailed");Assert(Find<TextBlock>(dialog,"UninstallStatus").Text==expected,"actual confirmation failure message must be accurate");dialog.Close();}
                                    if(mode=="veto"){
                                        pet.Closing-=veto;Assert(pet.IsVisible&&!pet.DataMaintenance.IsMaintenanceActive,"installed close veto damaged live host");
                                        Assert(!Directory.EnumerateFiles(Path.Combine(data,"uninstall"),"commit",SearchOption.AllDirectories).Any(),"veto committed helper");dialog.Close();
                                    }
                                }catch(Exception error){File.WriteAllText(Path.Combine(data,"fixture-error"),error.ToString());app.Shutdown(2);}
                            };timer.Start();Click(Find<Button>(about,"UninstallApplication"));timer.Stop();Assert(found,"actual installed About confirmation missing");
                            if(mode!="commit"){
                                Assert(pet.IsVisible&&!pet.DataMaintenance.IsMaintenanceActive,"cancel/veto must leave installed pet alive");
                                using(pet.DataMaintenance.EnterActivity()){}pet.Conversation.SelectPersona(pet.Conversation.PersonaId);
                                File.WriteAllText(Path.Combine(data,mode+"-alive"),"alive with timer/session/gate");host.Close();pet.Close();
                            }
                        }catch(Exception error){File.WriteAllText(Path.Combine(data,"fixture-error"),error.ToString());app.Shutdown(2);}
                    };
                    int code=app.Run(pet);
                    if(mode=="commit"&&code==0){
                        // Program's actual shutdown hook has awaited final drains and commit.
                        var retained=new SettingsStore(Path.Combine(data,"settings.json")).Load().TextProviders.First(p=>p.Id=="uninstall-fixture").Connection;
                        Assert(retained.EncryptedKey==ciphertext&&retained.ApiKey=="owned-test-key","actual encrypted key changed or cannot decrypt after preflight");
                        var saved=Directory.GetFiles(data,"*",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+"uninstall"+Path.DirectorySeparatorChar)&&!p.EndsWith("uninstall-result.json")&&!p.EndsWith("writer.lock")&&!p.EndsWith("-ready")&&!p.EndsWith("-exit")&&!p.EndsWith("pre-uninstall.json")).ToDictionary(p=>p.Substring(data.Length+1),UpdateInstall.Hash);
                        using(var stream=File.Create(Path.Combine(data,"pre-uninstall.json")))new DataContractJsonSerializer(typeof(Dictionary<string,string>)).WriteObject(stream,saved);
                        File.WriteAllText(Path.Combine(data,"closed-and-committed"),"actual Closed, drained, committed, Program shutdown completed");
                        while(!File.Exists(Path.Combine(data,"parent-exit")))Thread.Sleep(25);
                    }
                    return code;
                }catch(Exception error){File.WriteAllText(Path.Combine(data,"fixture-error"),error.ToString());return 2;}
            }
        }
    }
}
