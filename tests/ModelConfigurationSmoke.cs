using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ModelConfigurationSmoke
    {
        static int checks;static string folder;
        static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return (T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(node,i)))yield return item;}
        static T Find<T>(DependencyObject node,string name)where T:FrameworkElement{return All<T>(node).First(x=>x.Name==name);}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Click(DependencyObject node,string name){Find<Button>(node,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static void Save(Window w,string name){w.UpdateLayout();var content=(FrameworkElement)w.Content;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));}bitmap.Render(visual);using(var stream=File.Create(Path.Combine(folder,name+".png"))){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}}
        static void DuringModal(Action<Window> action){Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{var dialog=Application.Current.Windows.OfType<Window>().Last(w=>w.IsVisible&&w.Title.Contains("模型"));try{action(dialog);}catch{dialog.Close();throw;}}));}
        [STAThread]public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"feature-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);try{
            var blank=new TextProviderEditor(new PetSettings());var window=new Window();Ui.Window(window,"模型配置",900,780);window.Content=Ui.Scroll(blank);window.Show();Pump();Check(Find<ListBox>(blank,"TextProviderList").Items.Count==0&&blank.ReadProviders().Count==0,"first configuration has an empty provider list");Save(window,"empty-providers");window.Close();
            using(var server=new ApiTests.Fixture("{\"data\":[{\"id\":\"catalog-model\",\"context_length\":262144}]}")){
                var cfg=new PetSettings{Text=new ConnectionSettings{BaseUrl=server.Url,Model="configured",ApiKey="fixture",UseProxy=false}};var editor=new TextProviderEditor(cfg);window=new Window();Ui.Window(window,"模型配置",960,830);window.Content=Ui.Scroll(editor);window.Show();editor.ExpandConnection();Pump();Check(!Find<TextBox>(editor,"TextServiceModel").IsVisible,"connection settings omit current model ID");
                var until=DateTime.UtcNow.AddSeconds(8);while(!All<Button>(editor).Any(b=>b.Name=="AddDiscoveredModel")&&DateTime.UtcNow<until)Pump();Check(All<Button>(editor).Any(b=>b.Name=="AddDiscoveredModel"),"model catalog renders a direct add action");Save(window,"model-catalog");
                DuringModal(delegate(Window dialog){Check(Find<TextBox>(dialog,"TextModelContextCapacity").IsVisible,"model dialog exposes editable context capacity");Find<TextBox>(dialog,"TextModelContextCapacity").Text="475000";Find<TextBox>(dialog,"TextModelDisplayName").Text="日常对话";Save(dialog,"model-capacity");Click(dialog,"AddTextModel");});Click(editor,"AddDiscoveredModel");
                var provider=editor.ReadProviders().Single();Check(provider.Models.Contains("catalog-model")&&provider.ContextCapacity("catalog-model")==475000&&provider.ModelContextWindows["catalog-model"]==262144,"adding a catalog model retains discovery and manual override separately");
                DuringModal(delegate(Window dialog){Find<TextBox>(dialog,"TextProviderNewModel").Text="cancelled-model";Find<TextBox>(dialog,"TextModelContextCapacity").Text="123456";Click(dialog,"CancelModelConfiguration");});Click(editor,"CustomTextModel");Check(!editor.ReadProviders().Single().Models.Contains("cancelled-model"),"cancel does not commit a custom model draft");
                window.WindowState=WindowState.Maximized;Pump();var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;var bounds=DesktopInterop.Bounds(handle);var work=DesktopInterop.WorkArea(handle);Check(bounds.Left<=work.Left+1&&bounds.Top<=work.Top+1&&bounds.Right>=work.Right-1&&bounds.Bottom>=work.Bottom-1,"maximized window fills current monitor work area");window.WindowState=WindowState.Normal;Pump();Check(window.ActualWidth<work.Width,"restore returns to normal window size");window.Close();
            }
            var rename=new ConversationRenameDialog("审查 DeskPet dev 分支");rename.Show();Pump();Check(Find<TextBox>(rename,"ConversationTitle").ActualHeight>=38&&All<Button>(rename).Any(b=>b.Name=="SaveConversationTitle"&&b.IsVisible),"rename dialog provides styled field and visible footer");Save(rename,"rename-chat");rename.Close();
            var project=new ProjectDefinition{Name="deskpet",Workspace=AppDomain.CurrentDomain.BaseDirectory};var edit=new NewProjectDialog(project);edit.Show();Pump();Check(All<Button>(edit).Any(b=>b.Name=="RemoveEditedProject"&&b.IsVisible)&&Find<TextBox>(edit,"NewProjectName").Text=="deskpet","project editor shows source folder, name and removal action");Save(edit,"edit-project");edit.Close();
            var settings=new PetSettings{AgentEnabled=false,ReadReplies=false,AutoUpdate=false,GreetingEnabled=false};foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";settings.Text.Model="context-preview";settings.Text.ContextWindowTokens=475000;var pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(folder,"settings.json")),null);var chat=new ChatWindow(pet);chat.Show();Pump();var usage=Find<Button>(chat,"ContextUsagePreview");var picker=Find<Button>(chat,"TextModelPicker");var a=usage.TransformToAncestor(chat).TransformBounds(new Rect(usage.RenderSize));var modelBounds=picker.TransformToAncestor(chat).TransformBounds(new Rect(picker.RenderSize));Check(a.Right<=modelBounds.Left+3&&modelBounds.Left-a.Right<14,"context preview sits immediately left of the model picker (gap="+(modelBounds.Left-a.Right)+")");chat.WindowState=WindowState.Maximized;Pump();Save(chat,"chat-maximized");chat.Close();pet.Close();
            Console.WriteLine("Screenshots: "+folder);Console.WriteLine("Model configuration smoke: "+checks+" passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}
    }
}
