using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Dafeiyu;
using DeskPet.Desktop;

internal static class AgentJobNativeUiChecks
{
    private static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
    private static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    private static async Task WaitAsync(Func<bool> ready,int milliseconds,string description){var watch=Stopwatch.StartNew();while(!ready()){if(watch.ElapsedMilliseconds>milliseconds)throw new TimeoutException(description);await Task.Delay(25);}}
    internal static async Task RunInHostAsync(DesktopController owner,Window host,string root,ProjectDefinition project,ConversationDefinition conversation)
    {
        int checks=0;void Check(bool value,string label){if(!value)throw new Exception("Native Avalonia preview: "+label);checks++;Console.WriteLine("PASS "+label);}
        string output=Path.GetFullPath(Path.Combine("work","agent-jobs-ui","avalonia-native"));Directory.CreateDirectory(output);
        int port=Port();string ready=Path.Combine(root,"server.ready"),requests=Path.Combine(root,"server.requests");
        string command="$listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,"+port+"); $listener.Start(); [IO.File]::WriteAllText("+Quote(ready)+",'ready'); Write-Output 'http://user:secret@127.0.0.1:"+port+"/fixture?token=secret#part'; while($true){$client=$listener.AcceptTcpClient(); try{$stream=$client.GetStream(); $reader=New-Object IO.StreamReader($stream); $line=$reader.ReadLine(); [IO.File]::AppendAllText("+Quote(requests)+",$line+[Environment]::NewLine); while($reader.ReadLine()){}; $body='<html><body><p id=fixture>DeskPet Avalonia native preview</p></body></html>'; $bytes=[Text.Encoding]::UTF8.GetBytes($body); $header=[Text.Encoding]::ASCII.GetBytes(('HTTP/1.1 200 OK'+[char]13+[char]10+'Content-Type: text/html; charset=utf-8'+[char]13+[char]10+'Content-Length: '+$bytes.Length+[char]13+[char]10+'Connection: close'+[char]13+[char]10+[char]13+[char]10)); $stream.Write($header,0,$header.Length); $stream.Write($bytes,0,$bytes.Length); $stream.Flush()}finally{$client.Close()}}";
        var browser=new Browser(root);var panel=new DesktopAgentJobsPanel(owner.WorkBindings.Jobs,project.Id,openPreview:browser.OpenAsync);var window=Ui.Window(TextResources.Get("agent.jobs.ui.title"),panel,720,700);window.Show(host);
        try
        {
            Check(window.TryGetPlatformHandle()?.HandleDescriptor=="HWND","task panel uses an actual native Avalonia Windows window");
            var watch=Stopwatch.StartNew();var job=await AgentJobUiChecks.Start(owner.WorkBindings.Jobs.Manager,root,project.Id,conversation.Id,"native-preview",command);Check(watch.ElapsedMilliseconds<2000,"native server task hands off within two seconds");
            await WaitAsync(()=>File.Exists(ready)&&AgentJobUiChecks.All<Expander>(panel).Any(),7000,"native server readiness");AgentJobUiChecks.All<Expander>(panel).Single().IsExpanded=true;
            await WaitAsync(()=>AgentJobUiChecks.All<Button>(panel).Any(button=>button.Name=="AgentJobPreview"),7000,"native preview candidate");Check(!File.Exists(requests),"displaying a native preview candidate sends zero HTTP requests");
            var preview=AgentJobUiChecks.All<Button>(panel).Single(button=>button.Name=="AgentJobPreview");Check(ToolTip.GetTip(preview)?.ToString()=="http://127.0.0.1:"+port+"/fixture","native preview candidate removes credentials, query and fragment");
            preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await browser.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Check(File.Exists(requests)&&File.ReadAllText(requests).Contains("GET /fixture HTTP/")&&!File.ReadAllText(requests).Contains("secret"),"explicit Avalonia click loads the fixture body in actual isolated Edge");
            await browser.CaptureAsync(Path.Combine(output,"native-browser.png"));using(var capture=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96))){capture.Render(window);capture.Save(Path.Combine(output,"native-panel.png"));}
            AgentJobUiChecks.All<Button>(panel).Single(button=>button.Name=="AgentJobStop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitAsync(()=>owner.WorkBindings.Jobs.Manager.ListForProject(project.Id).Single(value=>value.Id==job.Id).CleanupConfirmed,6000,"native UI stop confirmation");
            var reuse=new TcpListener(IPAddress.Loopback,port);reuse.Start();reuse.Stop();Check(true,"native stop button confirms cleanup and releases the real listening port");
            var timings=new List<object>();foreach(int code in new[]{0,7})
            {
                watch.Restart();var terminal=await AgentJobUiChecks.Start(owner.WorkBindings.Jobs.Manager,root,project.Id,conversation.Id,"native-exit-"+code,"exit "+code);string expected=AgentJobUiText.State(code==0?AgentJobState.Succeeded:AgentJobState.Failed);
                await WaitAsync(()=>AgentJobUiChecks.All<DesktopAgentJobCard>(panel).Any(card=>card.DataContext is DesktopAgentJobsPanel.Row row&&row.Job.Id==terminal.Id&&AgentJobUiChecks.All<TextBlock>(card).Any(text=>text.Name=="AgentJobStatus"&&text.Text!.Contains(expected)&&text.Text.Contains(TextResources.Format("agent.jobs.ui.exitCode",code)))),2000,"native exit state "+code);
                var snapshot=owner.WorkBindings.Jobs.Manager.ListForProject(project.Id).Single(value=>value.Id==terminal.Id);Check(watch.ElapsedMilliseconds<2000&&snapshot.RootExitCode==code&&snapshot.RootExitObservedUtc.HasValue&&snapshot.TreeExitObservedUtc.HasValue,"native exit "+code+" is visible within two seconds with separate root/tree observations");timings.Add(new{code,visible_ms=watch.ElapsedMilliseconds,root_observed=snapshot.RootExitObservedUtc,tree_observed=snapshot.TreeExitObservedUtc});
            }
            await browser.StopAsync();Check(browser.CleanupConfirmed,"the explicit fixture browser's complete native process tree is cleaned");
            File.WriteAllText(Path.Combine(root,"preview.report.json"),JsonSerializer.Serialize(new{checks,timings,body="DeskPet Avalonia native preview",zero_http_before_click=true}));File.Copy(Path.Combine(root,"preview.report.json"),Path.Combine(output,"preview-report.json"),true);
        }
        finally{panel.Dispose();window.Close();await browser.DisposeAsync();}
    }
    private sealed class Browser(string root):IAsyncDisposable
    {
        private IAgentJobProcess? process;private ClientWebSocket? socket;private int nextId;
        internal readonly TaskCompletionSource<bool> Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CleanupConfirmed {get;private set;}
        internal async Task OpenAsync(Uri uri)
        {
            try
            {
                string edge=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}.Select(folder=>Path.Combine(folder,"Microsoft","Edge","Application","msedge.exe")).First(File.Exists);
                int port=Port();var arguments=new[]{"--headless=new","--disable-gpu","--no-first-run","--no-default-browser-check","--disable-background-networking","--disable-component-update","--disable-sync","--remote-debugging-address=127.0.0.1","--remote-debugging-port="+port,"--user-data-dir="+Path.Combine(root,"isolated-edge"),uri.AbsoluteUri};
                string command="& "+Quote(edge)+" "+string.Join(" ",arguments.Select(Quote));var factory=new WindowsAgentJobProcessFactory(new AgentJobUiChecks.Clock());process=await factory.PrepareAsync(new AgentJobSpec(command,"isolated native preview browser",root,1800),new AgentJobPolicySnapshot(root,"fixture-browser",1,1800,true,false,false,AgentApprovalMode.Full,null),_=>{},CancellationToken.None);process.Resume();
                using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(2)};string? address=null;var watch=Stopwatch.StartNew();
                while(address==null&&watch.ElapsedMilliseconds<15000)
                {
                    try{using var document=JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:"+port+"/json/list"));foreach(var page in document.RootElement.EnumerateArray())if(page.GetProperty("type").GetString()=="page"&&page.GetProperty("url").GetString()==uri.AbsoluteUri){address=page.GetProperty("webSocketDebuggerUrl").GetString();break;}}
                    catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException){}
                    if(address==null)await Task.Delay(100);
                }
                if(address==null)throw new TimeoutException("Actual Edge debugger did not expose the fixture page");socket=new ClientWebSocket();await socket.ConnectAsync(new Uri(address),CancellationToken.None);
                bool body=false;for(int attempt=0;attempt<50&&!body;attempt++){using var result=await CallAsync("Runtime.evaluate",new{expression="document.querySelector('#fixture')?.textContent",returnByValue=true});body=result.RootElement.GetProperty("result").GetProperty("result").TryGetProperty("value",out var value)&&value.GetString()=="DeskPet Avalonia native preview";if(!body)await Task.Delay(100);}
                if(!body)throw new Exception("Actual Edge did not render the fixture body");Ready.TrySetResult(true);
            }
            catch(Exception error){Ready.TrySetException(error);throw;}
        }
        private async Task<JsonDocument> CallAsync(string method,object parameters)
        {
            int id=++nextId;using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));var request=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,method,@params=parameters}));await socket!.SendAsync(request,WebSocketMessageType.Text,true,deadline.Token);
            var buffer=new byte[65536];using var message=new MemoryStream();
            while(true)
            {
                var frame=await socket.ReceiveAsync(buffer,deadline.Token);if(frame.MessageType==WebSocketMessageType.Close)throw new IOException("Native browser closed its debugger");message.Write(buffer,0,frame.Count);if(message.Length>8*1024*1024)throw new IOException("Native fixture debugger response exceeded its bound");if(!frame.EndOfMessage)continue;
                var result=JsonDocument.Parse(message.ToArray());message.SetLength(0);if(result.RootElement.TryGetProperty("id",out var response)&&response.GetInt32()==id){if(result.RootElement.TryGetProperty("error",out var error)){string messageText=error.ToString();result.Dispose();throw new IOException(messageText);}return result;}result.Dispose();
            }
        }
        internal async Task CaptureAsync(string path){using var result=await CallAsync("Page.captureScreenshot",new{format="png"});File.WriteAllBytes(path,Convert.FromBase64String(result.RootElement.GetProperty("result").GetProperty("data").GetString()!));}
        internal async Task StopAsync(){socket?.Dispose();socket=null;if(process==null)return;var cleanup=await process.StopAsync(CancellationToken.None);CleanupConfirmed=cleanup.CleanupConfirmed;if(!CleanupConfirmed)throw new IOException("Native fixture browser cleanup was not confirmed");process.Dispose();process=null;}
        public async ValueTask DisposeAsync(){try{await StopAsync();}finally{process?.Dispose();process=null;socket?.Dispose();socket=null;}}
    }
}
