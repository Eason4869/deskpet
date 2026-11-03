using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    // Release screenshots use isolated settings and authored demonstration data.
    // Never load the installed application's settings, history, or credentials.
    public static class ReleasePreviewSmoke
    {
        static Application app;static PetWindow pet;static ChatWindow chat;static string output,root;
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return(T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
        static async Task Settle(){await Task.Delay(180);if(chat!=null)chat.UpdateLayout();}
        static object Field(string name){return typeof(ChatWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(chat);}
        static void Save(FrameworkElement view,string name)
        {
            view.UpdateLayout();int width=(int)Math.Ceiling(view.ActualWidth),height=(int)Math.Ceiling(view.ActualHeight);if(width<1||height<1)throw new InvalidOperationException("Preview has no layout: "+name);
            var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,width,height));}
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))png.Save(file);Console.WriteLine("PREVIEW "+name+" "+width+"x"+height);
        }
        static void SaveWindow(Window host,string name){Save((FrameworkElement)host.Content,name);}
        static async Task Run()
        {
            output=Path.Combine(Directory.GetCurrentDirectory(),"previews","release-1.0.0");Directory.CreateDirectory(output);
            root=Path.Combine(Directory.GetCurrentDirectory(),"work","release-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,ShowPredictedMessages=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,OnboardingState="completed",SkinId="e-bao",AgentEnabled=true,AgentWorkspace=root};
            cfg.Text=new ConnectionSettings{BaseUrl="https://preview.example.invalid/v1",Model="Demo Model",ApiKey="",ToolSupport=CapabilitySupport.Supported};cfg.Transcription=new ConnectionSettings{BaseUrl="https://preview.example.invalid/v1",Model="Demo ASR",ApiKey=""};cfg.Speech=new ConnectionSettings{BaseUrl="https://preview.example.invalid/v1",Model="Demo Voice",ApiKey=""};
            foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
            pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"preview-data","settings.json")),null);
            var project=new ProjectDefinition{Id="release-demo",Name="演示项目",Workspace=root};var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="项目文档整理 · 演示",Mode="work"};var workspace=pet.Workspaces.CreateWorkspace(project,definition);
            cfg.AgentEnabled=false;chat=new ChatWindow(pet){Width=1440,MinWidth=1440,Height=900};chat.Show();await Settle();SaveWindow(chat,"conversation");chat.Close();chat=null;cfg.AgentEnabled=true;
            chat=new ChatWindow(pet,workspace){Width=1440,MinWidth=1440,Height=900};chat.Show();await Settle();
            workspace.Session.AddLiveTurn(workspace.Session.PersonaId,"请检查演示项目的说明文档，并整理一份可执行清单。","我会先查看项目文件，再整理可执行步骤。\n\n已核对文档结构，正在补充使用说明。\n\n## 整理结果\n\n1. 统一安装与首次配置说明。\n2. 补齐项目工作区与权限设置步骤。\n3. 记录验证结果，保留每个阶段的公开进度。\n\n**演示数据**：本截图不包含真实会话或账号配置。");await Settle();
            string document=Path.Combine(root,"演示说明.md");File.WriteAllText(document,"# 演示项目\n\n这是一份用于版本预览的示例文档。\n\n## 待办清单\n\n- 配置演示模型\n- 检查项目内容\n- 保留过程记录\n\n## 验证\n\n每个会话保留自己的文件、标签页和侧边聊天。\n");
            chat.OpenFilePreview(document);await Settle();SaveWindow(chat,"workspace");
            chat.OpenSideChat();await Settle();var side=((PreviewWorkspace)Field("filePreview")).Selected as SideChatPane;if(side==null)throw new InvalidOperationException("Side-chat preview was not selected.");
            side.Session.AddLiveTurn(side.Session.PersonaId,"根据主会话刚才的清单，下一步先做什么？","先补齐首次配置说明。主会话已确认文档结构，接下来可以只读查看项目中的示例说明，再核对配置步骤。\n\n侧边聊天使用独立记录，主会话可以继续原任务。");await Settle();SaveWindow(chat,"side-chat");
            chat.Close();chat=null;
            var editor=new TextProviderEditor(cfg,"text");var models=new Window{Width=1120,Height=820,Content=Ui.Scroll(editor),Background=Brushes.White,Title="模型配置 · 预览"};models.Show();await Settle();foreach(var details in All<Expander>(editor))details.IsExpanded=false;await Settle();SaveWindow(models,"models");models.Close();
            pet.ShowSettings();await Settle();var settingsHost=app.Windows.OfType<ChatWindow>().Single();settingsHost.Width=1200;settingsHost.MinWidth=1200;settingsHost.Height=850;var page=All<SettingsPage>(settingsHost).Single();page.SelectNavigation(5);await Settle();SaveWindow(settingsHost,"channels");page.SelectNavigation(0);await Settle();SaveWindow(settingsHost,"appearance");settingsHost.Close();
            string[] ids={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};string[] files={"assets/e-bao-pet-preview-hd.png","assets/pet-preview-hd.png","assets/marfu-pet-preview-hd.png","assets/bundled-skins/xiaobei/preview.png","assets/bundled-skins/gugugaga/preview.png"};
            for(int i=0;i<ids.Length;i++){
                var image=new BitmapImage(new Uri(Path.GetFullPath(files[i])));var body=new Grid{Width=320,Height=390,Background=Ui.Brush("#F7F9FC"),Margin=new Thickness(0)};body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(315)});body.RowDefinitions.Add(new RowDefinition());body.Children.Add(new Image{Source=image,Stretch=Stretch.Uniform,Margin=new Thickness(18,12,18,0)});var caption=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};caption.Children.Add(Ui.Text(pet.Catalog.FindOfficial(ids[i]).DisplayName,19,Ui.Ink));caption.Children.Add(Ui.Text("DeskPet 1.0.0 · 伙伴预览",11,Ui.Muted));Grid.SetRow(caption,1);body.Children.Add(caption);body.Measure(new Size(320,390));body.Arrange(new Rect(0,0,320,390));Save(body,"companion-"+ids[i]);
            }
            File.WriteAllText(Path.Combine(output,"README.txt"),"DeskPet 1.0.0 preview screenshots. Generated by tests/ReleasePreviewSmoke.cs from isolated settings and authored demo content. Model identifiers are placeholders. No installed settings, real conversations or credentials are loaded.\n");
        }
        [STAThread]public static int Main(){app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int code=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();code=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{if(chat!=null)chat.Close();if(pet!=null)pet.Close();app.Shutdown();}}));app.Run();return code;}
    }
}
