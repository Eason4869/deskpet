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
    public static class DeleteDialogTests
    {
        static int checks; static string root;
        static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
        static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var node in All<T>(VisualTreeHelper.GetChild(root, i))) yield return node; }
        static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { })); }
        static void Capture(Window dialog,string name="delete-dialog")
        {
            dialog.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(root, name+".png"))) encoder.Save(file);
        }
        static void Prompt(Action action, params Action<DeleteConversationDialog>[] inspect)
        {
            int next=0;Exception failure = null; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += delegate { var dialog = Application.Current.Windows.OfType<DeleteConversationDialog>().FirstOrDefault(); if (dialog == null || next>=inspect.Length) return;var step=inspect[next++];if(next==inspect.Length)timer.Stop();try { step(dialog); } catch (Exception e) { failure = e;timer.Stop();dialog.Close(); } };
            timer.Start(); try { action();var deadline=DateTime.UtcNow.AddSeconds(8);while(next<inspect.Length&&failure==null&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}if(next<inspect.Length&&failure==null)throw new Exception("Expected deletion dialog did not appear"); } finally { timer.Stop(); }
            if (failure != null) throw failure;
        }
        [STAThread]
        public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); PetWindow pet = null;
            try
            {
                root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "delete-ui-" + Guid.NewGuid().ToString("N")); var cfg = new PetSettings { AutoUpdate = false, GreetingEnabled = false, ReadReplies = false, ShowPredictedMessages = false }; foreach (var key in cfg.Hotkeys.Keys.ToArray()) cfg.Hotkeys[key] = "";
                pet = new PetWindow(null, cfg, new SettingsStore(Path.Combine(root, "settings.json")), null); var project = ProjectCatalogStore.CreateProject(pet.Projects, "fixture", root); var definition = project.Conversations[0]; definition.Name = "新对话"; pet.OpenProjectConversation(project, definition); Pump(); var workspace = pet.Workspaces.OpenWorkspaces.Single();
                workspace.Session.AddLiveTurn("work", "keep until confirmed", "reply"); workspace.Session.FlushHistoryAsync().GetAwaiter().GetResult(); string data = workspace.DataDirectory;
                Prompt(() => pet.DeleteConversation(project, definition), delegate (DeleteConversationDialog dialog)
                {
                    Check(dialog.WindowStyle == WindowStyle.None && dialog.Owner is ChatWindow, "delete uses the application dialog owned by the conversation window");
                    Check(All<TextBlock>(dialog).Any(t => t.Text == "要删除聊天吗？") && All<TextBlock>(dialog).Any(t => t.Text.Contains("无法撤销")), "dialog explains deletion and irreversible result");
                    var cancel = All<Button>(dialog).Single(b => b.Name == "CancelDeleteConversation"); Check(cancel.IsCancel && cancel.IsDefault && cancel.IsKeyboardFocused, "cancel receives the default action and keyboard focus"); Capture(dialog); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Check(project.Conversations.Contains(definition) && Directory.Exists(data), "cancel preserves the conversation and files");
                Prompt(() => pet.DeleteConversation(project, definition), dialog => All<Button>(dialog).Single(b => b.Name == "CloseDialog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Check(project.Conversations.Contains(definition) && Directory.Exists(data), "close button preserves the conversation and files");
                Prompt(() => pet.DeleteConversation(project, definition), dialog => All<Button>(dialog).Single(b => b.Name == "ConfirmDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                var deadline = DateTime.UtcNow.AddSeconds(5); while (Directory.Exists(data) && DateTime.UtcNow < deadline) { Pump(); System.Threading.Thread.Sleep(10); }
                Check(!project.Conversations.Contains(definition) && !Directory.Exists(data), "explicit delete removes the confirmed record and its data");
                Check(!pet.ProjectStore.Load().Projects.SelectMany(p => p.Conversations).Any(c => c.Id == definition.Id), "confirmed deletion is persisted in the catalog");
                Check(app.Windows.OfType<ChatWindow>().Single().ConversationId != definition.Id, "deleting the active conversation opens an available draft");
                Prompt(()=>pet.DeleteProject(project),dialog=>
                {
                    Capture(dialog,"delete-project-light");Check(dialog.WindowStyle==WindowStyle.None&&All<TextBlock>(dialog).Any(t=>t.Text.Contains("源文件")),"project deletion uses the built-in dialog and explains file scope");
                    Check(All<Button>(dialog).Single(b=>b.Name=="CancelDeleteConversation").IsDefault,"project deletion defaults to cancel");
                    TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Pump();
                    Check(All<TextBlock>(dialog).Any(t=>t.Text==TextResources.Get("petWindow.deleteProject.messageBoxShow.alternativeA")),"open project confirmation follows live language changes");
                    Capture(dialog,"delete-project-english");TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));
                    All<Button>(dialog).Single(b=>b.Name=="CancelDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Check(pet.Projects.Projects.Contains(project),"cancel preserves the project");
                var sidebar=new ProjectSidebar(pet.ProjectStore,pet.Projects,delegate{},delegate{},delegate{},delegate{},delegate{});
                var section=sidebar.CreateSection("待删除分区");sidebar.MoveProjectToSection(project,section.Id);
                Action requestSection=()=>
                {
                    var more=All<Button>(sidebar).Single(b=>b.Name=="SectionMore_"+section.Id);
                    more.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="DeleteSection_"+section.Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                };
                Prompt(requestSection,dialog=>All<Button>(dialog).Single(b=>b.Name=="CloseDialog").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Check(pet.Projects.Sections.Contains(section)&&project.SectionId==section.Id,"closing section confirmation preserves membership");
                ThemeManager.Apply("dark");
                Prompt(requestSection,dialog=>
                {
                    Check(dialog.WindowStyle==WindowStyle.None&&All<TextBlock>(dialog).Any(t=>t.Text.Contains("默认分区")),"section deletion explains preserved contents in a built-in dialog");
                    Capture(dialog,"delete-section-dark");All<Button>(dialog).Single(b=>b.Name=="ConfirmDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });ThemeManager.Apply("light");
                Check(!pet.Projects.Sections.Contains(section)&&project.SectionId=="projects","confirm deletes the section and retains its project");
                var retained=ProjectCatalogStore.CreateConversation(pet.Projects,project.Id,"保存失败时保留");pet.ProjectStore.Save(pet.Projects);var keep=pet.Workspaces.CreateWorkspace(project,retained);
                pet.ProjectStore.PreserveAfterReadFailure();
                try
                {
                    Prompt(()=>pet.DeleteConversation(project,retained),
                        dialog=>All<Button>(dialog).Single(b=>b.Name=="ConfirmDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                        delegate(DeleteConversationDialog dialog)
                        {
                            Check(All<TextBlock>(dialog).Any(t=>t.Text=="删除未完成")&&!All<Button>(dialog).Single(b=>b.Name=="ConfirmDeleteConversation").IsVisible,"storage errors also use a silent application dialog");
                            All<Button>(dialog).Single(b=>b.Name=="CancelDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        });
                    Check(project.Conversations.Contains(retained)&&Directory.Exists(keep.DataDirectory),"catalog save failure rolls back membership and preserves data");
                    Prompt(()=>pet.DeleteProject(project),
                        dialog=>All<Button>(dialog).Single(b=>b.Name=="ConfirmDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                        dialog=>{Check(!All<Button>(dialog).Single(b=>b.Name=="ConfirmDeleteConversation").IsVisible,"project save failure uses built-in error dialog");All<Button>(dialog).Single(b=>b.Name=="CancelDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});
                    Check(pet.Projects.Projects.Contains(project)&&Directory.Exists(keep.DataDirectory),"project save failure preserves membership and data");
                }
                finally{typeof(ProjectCatalogStore).GetField("readFailed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(pet.ProjectStore,false);}
                File.WriteAllText(Path.Combine(root,"source.txt"),"keep source files");
                Prompt(()=>pet.DeleteProject(project),dialog=>All<Button>(dialog).Single(b=>b.Name=="ConfirmDeleteConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                var projectDeadline=DateTime.UtcNow.AddSeconds(8);while(pet.Projects.Projects.Contains(project)&&DateTime.UtcNow<projectDeadline){Pump();System.Threading.Thread.Sleep(10);}
                Check(!pet.Projects.Projects.Contains(project)&&!pet.ProjectStore.Load().Projects.Any(p=>p.Id==project.Id),"confirmed project deletion persists");
                Check(File.Exists(Path.Combine(root,"source.txt")),"project deletion preserves workspace source files");
                Console.WriteLine("Delete dialog tests: " + checks + " passed; screenshot: " + Path.Combine(root, "delete-dialog.png")); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { foreach (var chat in app.Windows.OfType<ChatWindow>().ToArray()) chat.Close(); if (pet != null) pet.Close(); app.Shutdown(); }
        }
    }
}
