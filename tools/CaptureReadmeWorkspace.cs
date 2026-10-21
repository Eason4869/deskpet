using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dafeiyu;

public static class CaptureReadmeWorkspace
{
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        var element = root as FrameworkElement;
        if(element != null)yield return element;
        for(int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach(var child in Elements(VisualTreeHelper.GetChild(root, index)))yield return child;
    }

    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [STAThread]
    public static int Main(string[] arguments)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        PetWindow pet = null;
        try
        {
            string output = Path.GetFullPath(arguments[0]);
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            var settings = new PetSettings {
                SkinId = "marfu", AutoUpdate = false, GreetingEnabled = false,
                AiGreetingEnabled = false, ReadReplies = false, SaveChatHistory = false,
                OnboardingState = "completed", ShowPredictedMessages = false,
                AgentEnabled = false, AgentWorkspace = data, Topmost = false,
                Text = new ConnectionSettings { BaseUrl = "http://127.0.0.1:1/v1", Model = "离线示例 · 不发送请求" }
            };
            foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key] = "";
            pet = new PetWindow(null, settings, new SettingsStore(Path.Combine(data, "settings.json")), null);
            string[] titles = { "今天的灵感清单", "周末去哪里走走", "把 README 讲清楚", "一道晚餐的小选择", "最近在读的书", "给桌面换个伙伴" };
            var conversations = new List<ConversationDefinition>();
            for(int index = 0; index < titles.Length; index++)
            {
                var conversation = ProjectCatalogStore.CreateConversation(pet.Projects, ProjectCatalogStore.QuickProjectId, titles[index]);
                conversation.PetId = pet.Skin.Id;
                conversation.NameEdited = true;
                conversation.LastOpenedUtcTicks = DateTime.UtcNow.AddMinutes(-index * 10).Ticks;
                conversations.Add(conversation);
            }
            var project = ProjectCatalogStore.FindProject(pet.Projects, ProjectCatalogStore.QuickProjectId, settings);
            var workspace = pet.Workspaces.CreateWorkspace(project, conversations[0]);
            workspace.Session.History.Commit(
                "想做的事有点多：整理 README、试试桌宠互动，晚上还想留点时间读书。帮我理一下？",
                "当然，不用把一天排得满满当当。我们先挑一件最想完成的事。\n\n### 今天的小清单\n\n- **先做正事**：整理 README，把项目介绍和界面截图放在一起。\n- **中途歇一会儿**：摸摸头、打开动作轮盘，换个心情。\n- **给自己留点时间**：晚上读几页书，不设太大的目标。\n\n先从 README 开始，还是先聊聊你今天想到的点子？");
            pet.OpenProjectConversation(project, conversations[0]);
            var window = app.Windows.OfType<ChatWindow>().Single();
            window.Width = 1440;
            window.Height = 960;
            Pump(650);
            window.UpdateLayout();
            foreach(string name in new[] { "ShellHome", "ShellUsage", "ShellMcp", "ShellSettings", "SendMessage", "VoiceRecord" })
            {
                var control = Elements(window).Single(element => element.Name == name);
                var point = control.TranslatePoint(new Point(), window);
                if(!control.IsVisible || point.X < 0 || point.Y < 0 || point.X + control.ActualWidth > window.ActualWidth + 1 || point.Y + control.ActualHeight > window.ActualHeight + 1)
                    throw new InvalidOperationException("Clipped capture control: " + name);
            }
            foreach(string title in titles)
                if(!Elements(window).OfType<TextBlock>().Any(block => block.IsVisible && block.Text == title))
                    throw new InvalidOperationException("Missing sample conversation: " + title);
            var visual = new DrawingVisual();
            using(var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                drawing.DrawRectangle(new VisualBrush(window), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using(var stream = File.Create(output))encoder.Save(stream);
            Console.WriteLine("PASS actual application capture: " + output + " (" + bitmap.PixelWidth + " x " + bitmap.PixelHeight + "), six sample conversations and complete controls");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            foreach(var window in app.Windows.OfType<ChatWindow>().ToArray())window.Close();
            if(pet != null)pet.Close();
            app.Shutdown();
        }
    }
}
