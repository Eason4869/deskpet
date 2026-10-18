using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ComposerRenderingTests
    {
        static int checks; static string root;
        static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
        static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var node in All<T>(VisualTreeHelper.GetChild(root, i))) yield return node; }
        static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
        static void Capture(Window host, string name) { host.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(host); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(root, name))) png.Save(file); }
        [STAThread]
        public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; PetWindow pet = null; ChatWindow full = null; QuickChatWindow quick = null;
            try
            {
                root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "composer-render-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                var cfg = new PetSettings { AutoUpdate = false, GreetingEnabled = false, ReadReplies = false, SaveChatHistory = false, ShowPredictedMessages = false }; foreach (var key in cfg.Hotkeys.Keys.ToArray()) cfg.Hotkeys[key] = "";
                pet = new PetWindow(null, cfg, new SettingsStore(Path.Combine(root, "settings.json")), null);
                pet.Conversation.AddLiveTurn(pet.Skin.Id, "测试", "https://github.com/Eason4869/deskpet/tree/dev");
                pet.Conversation.AddLiveTurn(pet.Skin.Id, new string('长', 120), "reply");
                pet.Conversation.AddLiveTurn(pet.Skin.Id, "第一行\n第二行", "reply");
                pet.Conversation.AddLiveTurn(pet.Skin.Id, "https://github.com/Eason4869/deskpet/tree/dev", "reply");
                full = new ChatWindow(pet) { Width = 1050, Height = 850 }; quick = new QuickChatWindow(pet); full.Show(); quick.Show(); Pump();
                foreach (Window host in new Window[] { full, quick })
                {
                    var users = All<Border>(host).Where(b => b.Name == "SentUserBubble").ToArray();
                    Check(users.Length == 4, "all message bubbles render in " + host.GetType().Name);
                    Check(users[0].ActualWidth < users[1].ActualWidth * .55, "short bubble follows its content rather than maximum width");
                    Check(users[1].ActualHeight > users[0].ActualHeight && users[2].ActualHeight > users[0].ActualHeight, "long and explicit multiline messages grow vertically");
                    var firstLine = ((MarkdownView)users[2].Child).Document.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().First(); users[2].BringIntoView(); Pump();
                    Check(Math.Abs(firstLine.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top - firstLine.ContentEnd.GetPositionAtOffset(-1).GetCharacterRect(LogicalDirection.Forward).Top) < 1, "natural-width measurement does not add spurious wrapping to a short line");
                    foreach (var bubble in users) { var message = All<ChatMessageView>(host).Single(m => All<Border>(m).Contains(bubble)); Check(bubble.ActualWidth <= message.MaxWidth + 1 && bubble.HorizontalAlignment == HorizontalAlignment.Right, "bubble stays right aligned within its original maximum"); }
                    Check(All<FrameworkElement>(users[3]).Any(b => b.Name == "MarkdownGitHubIcon") && !All<Border>(users[3]).Any(b=>b.Name=="MarkdownDomainChip"), "user URL uses a GitHub logo and clickable address without a domain chip");
                    var reply = All<ChatMessageView>(host).First(m => m.Name == "AssistantMessage"); Check(All<FrameworkElement>(reply).Any(v => v.Name == "MarkdownGitHubIcon") && !All<Border>(reply).Any(b => b.Name == "MarkdownDomainChip"), "assistant GitHub URL uses a lightweight brand icon");
                    var link = reply.Body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()).Single(); Check(link.NavigateUri.Host == "github.com" && new TextRange(link.ContentStart, link.ContentEnd).Text.Contains("https://github.com/"), "assistant hyperlink preserves the address and destination");
                    var input = All<MarkdownEditor>(host).Single(t => t.Name == "MessageInput" || t.Name == "QuickMessageInput");
                    foreach (string draft in new[] { "**测试**", "*斜体*", "# 标题", "- 第一项\n- 第二项", "```csharp\nint a = 1;\n```", "[github.com](https://github.com/Eason4869/deskpet/tree/dev)" })
                    {
                        input.Text = draft; input.CaretIndex = draft.Length; Pump();
                        Check(input.IsVisible && input.ActualHeight > 8 && input.Document.Blocks.Count > 0, "formatted draft is the actual editable document: " + draft.Split('\n')[0]);
                        Check(input.Text == draft && input.CaretIndex == draft.Length, "rich editor preserves Markdown transport and caret");
                    }
                    input.Text = "**测试**"; Pump(); Check(input.Document.Blocks.OfType<Paragraph>().Any(p => p.Inlines.OfType<Bold>().Any()), "bold is rendered inside the editable document");
                    Check(!new TextRange(input.Document.ContentStart,input.Document.ContentEnd).Text.Contains("**"),"rich editing hides source markers");
                    input.PlainTextMode=true;Check(new TextRange(input.Document.ContentStart,input.Document.ContentEnd).Text.Contains("**测试**"),"plain mode exposes original Markdown");
                    input.PlainTextMode=false;Check(input.Text=="**测试**" && !new TextRange(input.Document.ContentStart,input.Document.ContentEnd).Text.Contains("**"),"switching back preserves source and restores formatting");
                    var paragraph=input.Document.Blocks.OfType<Paragraph>().First();var run=paragraph.Inlines.OfType<Bold>().First().Inlines.OfType<Run>().First();input.Selection.Select(run.ContentEnd,run.ContentEnd);input.Selection.Text="新增";Pump();
                    Check(input.Text.Contains("**测试新增**"),"editing rendered bold serializes valid Markdown");
                    input.Text="";input.Selection.Text="**现场输入**";Pump();Check(input.Document.Blocks.OfType<Paragraph>().Any(p=>p.Inlines.OfType<Bold>().Any()) && !new TextRange(input.Document.ContentStart,input.Document.ContentEnd).Text.Contains("**"),"typed Markdown transforms in the real input document");
                    input.Undo();input.Redo();Pump();Check(input.Text.Contains("现场输入"),"native undo and redo retain edited content");
                    input.Text="**测试**";input.Focus();input.CaretIndex=input.Text.Length;
                    EditingCommands.EnterLineBreak.Execute(null,input);Pump();
                    input.Selection.Text="下一行";Pump();
                    Check(input.Text.Contains("\n") && input.Text.Contains("下一行"),"bold draft keeps a real newline and following input");
                    Check(!input.Text.Contains("下一行**"),"newline ends bold formatting for subsequent input");
                    input.Text="**测试**";input.CaretIndex=input.Text.Length;TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input," "));Pump();TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input,"普通文字"));Pump();
                    Check(input.Text=="**测试** 普通文字","space ends bold formatting without removing preceding emphasis: "+input.Text);
                    foreach(string formattedDraft in new[]{"*斜体*","~~删除线~~","`代码`"}){
                        input.Text=formattedDraft;input.CaretIndex=input.Text.Length;TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input," "));Pump();TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input,"普通"));Pump();
                        Check(input.Text==formattedDraft+" 普通","space ends inline style: "+formattedDraft+" actual="+input.Text);
                    }
                    input.Text="**测试**\n";input.CaretIndex=input.Text.Length;TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input,"新行"));Pump();
                    Check(input.Text.Contains("\n") && input.Text.Contains("新行"),"rendering and editing retains trailing draft newline");
                    input.Text="**测试**";input.CaretIndex=input.Text.Length;EditingCommands.EnterParagraphBreak.Execute(null,input);Pump();TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input,"段落"));Pump();
                    Check(input.Text.Contains("\n")&&!input.Text.Contains("段落**"),"paragraph break also resets inline emphasis");
                    input.Text="# 标题";input.CaretIndex=input.Text.Length;EditingCommands.EnterParagraphBreak.Execute(null,input);Pump();TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,input,"正文"));Pump();Check(input.Document.Blocks.OfType<Paragraph>().Last().FontWeight==FontWeights.Normal&&input.Text.Contains("\n\n正文"),"newline after heading starts an ordinary paragraph");
                    input.Text="";input.RaiseEvent(new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,"https://github.com/Eason4869/deskpet"),false,DataFormats.UnicodeText){RoutedEvent=DataObject.PastingEvent});input.Selection.Text="https://github.com/Eason4869/deskpet";Pump();Check(input.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Hyperlink>()).Any(),"pasted URL renders as an editable link without changing the clipboard");
                    input.Text="https://www.bilibili.com/";Pump();Check(All<FrameworkElement>(input).Any(v=>v.Name=="MarkdownGlobeIcon") && !All<Border>(input).Any(b=>b.Name=="MarkdownDomainChip"),"editor uses a globe and clickable URL without a domain chip");var website=input.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Hyperlink>()).Single();Check(website.NavigateUri.AbsoluteUri=="https://www.bilibili.com/" && new TextRange(website.ContentStart,website.ContentEnd).Text.Contains("https://www.bilibili.com/"),"website link preserves its visible address and navigation target");
                    Capture(host,host is QuickChatWindow?"quick-links.png":"full-links.png");
                    input.Text="# 标题";var heading=input.Document.Blocks.OfType<Paragraph>().First();input.Selection.Select(heading.ContentEnd,heading.ContentEnd);input.Selection.Text="追加";Check(input.Text=="# 标题追加","editing a heading retains its Markdown structure without adding bold markers");
                    input.Text="- parent\n  - child";var list=input.Document.Blocks.OfType<System.Windows.Documents.List>().First();var parent=list.ListItems.FirstListItem.Blocks.OfType<Paragraph>().First();input.Selection.Select(parent.ContentEnd,parent.ContentEnd);input.Selection.Text=" edited";Check(input.Text.Contains("\n  - child") && !input.Text.Contains("- - child"),"editing a nested list preserves indentation and item ownership");
                    input.Text="- first\n\n  second paragraph";list=input.Document.Blocks.OfType<System.Windows.Documents.List>().First();parent=list.ListItems.FirstListItem.Blocks.OfType<Paragraph>().First();input.Selection.Select(parent.ContentEnd,parent.ContentEnd);input.Selection.Text=" edited";Check(input.Text.Contains("\n\n  second paragraph"),"editing a list preserves continuation paragraphs in the same item");
                    cfg.PlainTextEditor=true;cfg.ShowContextUsage=false;if(host is ChatWindow)((ChatWindow)host).RefreshAppearance();else ((QuickChatWindow)host).RefreshPersona();Pump();Check(input.PlainTextMode && !All<Button>(host).Single(b=>b.Name=="ContextUsagePreview").IsVisible,"saved preferences apply to the live editor and hide context usage");
                    cfg.PlainTextEditor=false;cfg.ShowContextUsage=true;if(host is ChatWindow)((ChatWindow)host).RefreshAppearance();else ((QuickChatWindow)host).RefreshPersona();input.Text="**测试**";Pump();
                    Capture(host, host is QuickChatWindow ? "quick.png" : "full.png"); input.Clear(); Pump();Check(input.Text.Length==0,"clearing removes draft");
                }
                Console.WriteLine("Composer rendering tests: " + checks + " passed; screenshots: " + root); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { if (quick != null) quick.Close(); if (full != null) full.Close(); if (pet != null) pet.Close(); app.Shutdown(); }
        }
    }
}
