using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
namespace Dafeiyu
{
    internal sealed class TerminalPane:DockPanel,IDisposable
    {
        readonly string workspace;readonly bool sandbox;readonly string[] tools;readonly TextBlock state=LocalizedUi.Text(()=>(TextResources.Get("terminalPane.state.text")),11,AgentUi.Muted);readonly ContentControl content=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        readonly object outputGate=new object();readonly StringBuilder pending=new StringBuilder();readonly DispatcherTimer flush=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};WindowsTerminalSession process;TerminalBrowser browser;bool disposed,starting,draining,truncated;
        internal Task Ready{get;private set;}internal string Workspace{get{return workspace;}}internal bool Sandboxed{get{return sandbox;}}internal bool IsRunning{get{return process!=null&&process.IsRunning;}}
        internal string StartupError{get;private set;}
        internal TerminalPane(string workspace,bool sandbox,string[] tools)
        {
            this.workspace=Path.GetFullPath(workspace);this.sandbox=sandbox;this.tools=(string[])(tools??new string[0]).Clone();Name="TerminalPane";Background=Ui.Brush("#F8F9FB");
            var bar=new DockPanel{Margin=new Thickness(10,8,8,8)};var interrupt=UiIcons.Button("stop",()=>(TextResources.Get("terminalPane.terminalPane.uiIcons")));interrupt.Name="InterruptTerminal";interrupt.Width=interrupt.Height=interrupt.MinHeight=28;interrupt.Padding=new Thickness(6);LocalizedUi.Set(interrupt,"ToolTip",()=>(TextResources.Get("terminalPane.terminalPane.interruptToolTip")));interrupt.Click+=delegate{Send("\x03");};DockPanel.SetDock(interrupt,Dock.Right);bar.Children.Add(interrupt);var clear=UiIcons.Button("trash",()=>(TextResources.Get("terminalPane.terminalPane.uiIcons.alternativeA")));clear.Name="ClearTerminal";clear.Width=clear.Height=clear.MinHeight=28;clear.Padding=new Thickness(6);LocalizedUi.Set(clear,"ToolTip",()=>(TextResources.Get("terminalPane.terminalPane.clearToolTip")));clear.Click+=delegate{if(browser!=null)browser.Clear();};DockPanel.SetDock(clear,Dock.Right);bar.Children.Add(clear);
            var identity=new StackPanel();var directory=Ui.Text(Path.GetFileName(this.workspace.TrimEnd('\\')),12,AgentUi.Ink);directory.FontWeight=FontWeights.SemiBold;directory.TextWrapping=TextWrapping.NoWrap;directory.TextTrimming=TextTrimming.CharacterEllipsis;directory.ToolTip=this.workspace;identity.Children.Add(directory);state.Name="TerminalSessionState";LocalizedUi.Set(state,"Text",()=>(sandbox?TextResources.Get("terminalPane.terminalPane.stateSandbox"):TextResources.Get("terminalPane.terminalPane.stateSandbox.alternativeA")));LocalizedUi.Set(state,"ToolTip",()=>(TextResources.Get("terminalPane.terminalPane.stateToolTip")));identity.Children.Add(state);bar.Children.Add(identity);DockPanel.SetDock(bar,Dock.Top);Children.Add(bar);Children.Add(content);
            flush.Tick+=delegate{FlushOutput();};Ready=Task.FromResult(0);Loaded+=delegate{if(!starting){starting=true;Ready=StartAsync();}};
        }
        async Task StartAsync()
        {
            try{WebPreviewRuntime.Ensure();await StartBrowserAsync();if(disposed)return;process=await Task.Run(()=>WindowsTerminalSession.Start(workspace,sandbox,tools,ReceiveOutput,OnExited));if(disposed){process.Dispose();process=null;return;}flush.Start();browser.FocusInput();}
            catch(Exception error){if(disposed)return;StartupError=error.ToString();LocalizedUi.Set(state,"Text",()=>(TextResources.Get("terminalPane.startAsync.state")));var message=LocalizedUi.Text(()=>(TextResources.Format("terminalPane.startAsync.template",error.Message)),12,AgentUi.Muted);message.Margin=new Thickness(18);content.Content=message;}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]async Task StartBrowserAsync(){var next=new TerminalBrowser(Send,Resize,delegate{draining=false;FlushOutput();});browser=next;content.Content=next;await next.InitializeAsync();}
        void ReceiveOutput(string data){if(disposed)return;lock(outputGate){pending.Append(data);if(pending.Length>256*1024){pending.Remove(0,pending.Length-256*1024);truncated=true;}}}
        void OnExited(int code){if(Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)LocalizedUi.Set(state,"Text",()=>(TextResources.Format("terminalPane.onExited.stateCode",code)));}));}
        void FlushOutput(){if(disposed||browser==null||draining)return;string data;lock(outputGate){if(pending.Length==0)return;int length=Math.Min(32768,pending.Length);data=pending.ToString(0,length);pending.Remove(0,length);if(truncated){data=TextResources.Format("terminalPane.flushOutput.dataData",data);truncated=false;}}draining=true;browser.Write(data);}
        internal void Send(string data){if(disposed||process==null)return;try{process.Input(data);}catch(IOException){LocalizedUi.Set(state,"Text",()=>(TextResources.Get("terminalPane.send.state")));}catch(ObjectDisposedException){LocalizedUi.Set(state,"Text",()=>(TextResources.Get("terminalPane.send.state")));}}
        void Resize(short cols,short rows){if(disposed||process==null)return;try{process.Resize(cols,rows);}catch(ObjectDisposedException){}catch(System.ComponentModel.Win32Exception){LocalizedUi.Set(state,"Text",()=>(TextResources.Get("terminalPane.resize.state")));}}
        internal Task<string> EvaluateAsync(string script){if(browser==null)throw new InvalidOperationException(TextResources.Get("terminalPane.evaluateAsync.text"));return browser.EvaluateAsync(script);}
        internal Task CaptureAsync(Stream stream){return browser.CaptureAsync(stream);}
        public void Dispose(){if(disposed)return;disposed=true;flush.Stop();if(browser!=null){browser.Dispose();browser=null;}content.Content=null;if(process!=null){process.Dispose();process=null;}lock(outputGate)pending.Clear();}
    }
}
