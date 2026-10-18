using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ComposerSmoke
    {
        static int passed;static string folder;
        [StructLayout(LayoutKind.Sequential)] struct NativePoint{public int X,Y;}
        [StructLayout(LayoutKind.Sequential)] struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
        [StructLayout(LayoutKind.Sequential)] struct NativeInput{public uint Type;public MouseInput Mouse;}
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,NativeInput[] inputs,int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [STAThread] public static int Main(string[] args)
        {
            bool nativeDrag=args.Length==1 && args[0]=="--native-drag",placeholderOnly=args.Length==1 && args[0]=="--placeholder-only";if(args.Length>0 && !nativeDrag && !placeholderOnly){Console.Error.WriteLine("Usage: ComposerSmoke.exe [--native-drag|--placeholder-only]");return 2;}
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"composer-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                var cfg=new PetSettings{AgentEnabled=false,ReadReplies=false,AutoUpdate=false,FollowCursor=false,Wander=false,GreetingEnabled=false,HideOnFullscreen=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};
                cfg.EnsureTextProviders();string alias="深度思考";cfg.TextProviders[0].ModelDisplayNames["fixture"]=alias;string longModel="second-provider/very-long-model-name-with-thinking-and-multimodal-capabilities-2026-10";
                cfg.TextProviders.Add(new TextProviderSettings{Id="second",Name="另一个服务商",Connection=new ConnectionSettings{BaseUrl="http://127.0.0.1:2/v1",ApiKey="fixture-secret",Model=longModel},Models=new List<string>{longModel,"short-model"},ModelDisplayNames=new Dictionary<string,string>{{longModel,alias}}});
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);var api=new Api();var session=new ConversationSession(api,new Speech(),()=>null);session.SelectPersona(pet.Skin.Id);typeof(PetWindow).GetField("conversation",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,session);
                cfg.Transcription=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="asr-fixture"};Recorder recorder=null;var dictation=new DictationSession(new Api(),()=>recorder=new Recorder(),()=>cfg);typeof(PetWindow).GetField("dictation",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,dictation);
                pet.Show();var fixtureChat=new ChatWindow(pet);typeof(PetWindow).GetField("chat",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,fixtureChat);fixtureChat.Show();var sharedQuick=new QuickChatWindow(pet);typeof(PetWindow).GetField("quick",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,sharedQuick);sharedQuick.Closed+=delegate{typeof(PetWindow).GetField("quick",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,null);};sharedQuick.Show();Pump(.2);
                var chat=app.Windows.OfType<ChatWindow>().Single();var quick=app.Windows.OfType<QuickChatWindow>().Single();
                if(placeholderOnly){pet.HidePet();PlaceholderSmoke(chat,quick);Console.WriteLine("Placeholder smoke: "+passed+" passed; screenshots: "+folder);return 0;}
                chat.Show();var initialInput=Find<MarkdownEditor>(chat,"MessageInput");initialInput.Text="before switch";Click(Find<Button>(chat,"SendMessage"));Until(()=>api.Requests.Count==1);
                SelectModel(quick,"second",longModel);initialInput.Text="queued after switch";Click(Find<Button>(chat,"SendMessage"));
                Check(api.Requests[0].Model=="fixture" && api.Requests[0].BaseUrl=="http://127.0.0.1:1/v1","switching providers leaves the running request snapshot unchanged");
                Check(Find<TextBlock>(chat,"CurrentTextModel").Text==alias && Find<TextBlock>(quick,"CurrentTextModel").Text==alias,"both windows immediately show selected Chinese model display name");
                api.Complete();Until(()=>api.Requests.Count==2);
                Check(api.Requests[1].Model==longModel && api.Requests[1].BaseUrl=="http://127.0.0.1:2/v1" && api.Requests[1].ApiKey=="fixture-secret","next queued request uses selected provider endpoint, model and credentials");api.Complete();Until(()=>!pet.InputQueue.IsRunning);
                var saved=new SettingsStore(Path.Combine(folder,"settings.json")).Load();Check(saved.ActiveTextProviderId=="second" && saved.Text.Model==longModel && saved.TextProviders.Single(p=>p.Id=="second").DisplayName(longModel)==alias,"model selection and Chinese display name survive settings reload");
                chat.Close();chat=new ChatWindow(pet);typeof(PetWindow).GetField("chat",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,chat);chat.Show();Pump(.1);Check(Find<TextBlock>(chat,"CurrentTextModel").Text==alias,"reopened full chat retains the Chinese display name");
                quick.Hide();pet.ShowQuickChat();Pump(.1);Check(Find<TextBlock>(quick,"CurrentTextModel").Text==alias,"reopened quick chat retains the Chinese display name");
                foreach(Window window in new Window[]{chat,quick})
                {
                    window.Show();window.UpdateLayout();Pump(.1);
                    bool small=window==quick;var input=Find<MarkdownEditor>(window,small?"QuickMessageInput":"MessageInput");var button=Find<Button>(window,small?"SendQuickMessage":"SendMessage");
                    if(input==null || button==null)throw new Exception("Composer controls missing in "+window.GetType().Name+"; visible="+window.IsVisible);
                    var picker=Find<Button>(window,"TextModelPicker");var mic=Find<Button>(window,"VoiceRecord");Rect inputBounds=Bounds(input,window),pickerBounds=Bounds(picker,window),micBounds=Bounds(mic,window),sendBounds=Bounds(button,window);
                    Check(inputBounds.Bottom<=pickerBounds.Top && inputBounds.Width>pickerBounds.Width+micBounds.Width+sendBounds.Width,"full-width input stays above the toolbar");
                    Check(pickerBounds.Right<=micBounds.Left && micBounds.Right<=sendBounds.Left && Math.Abs(pickerBounds.Bottom-sendBounds.Bottom)<=5 && Math.Abs(micBounds.Bottom-sendBounds.Bottom)<1,"model optical offset stays within toolbar while microphone and blue action align");
                    input.Text="第一行，输入区域横向完整\n第二行不会被按钮遮住\n第三行保留换行";window.UpdateLayout();Check(Bounds(input,window).Bottom<=Bounds(picker,window).Top,"multiline draft never overlaps model controls");Save(window,small?"quick-model-layout.png":"chat-model-layout.png");input.Clear();
                    Check(((string)picker.ToolTip).Contains(longModel),"aliased picker tooltip retains the full API model ID");
                    var aliasedProvider=cfg.TextProviders.Single(p=>p.Id=="second");aliasedProvider.ModelDisplayNames.Remove(longModel);pet.RefreshChatAppearance();window.UpdateLayout();Check(Find<TextBlock>(window,"CurrentTextModel").Text==longModel,"model without custom display name falls back to full ID");
                    if(!small){double previousWidth=window.Width;window.Width=520;window.UpdateLayout();Check(Bounds(picker,window).Left>=Bounds(input,window).Left-1 && Bounds(button,window).Right<=Bounds(input,window).Right+1,"long model fits minimum chat width");Save(window,"chat-model-narrow.png");window.Width=previousWidth;window.UpdateLayout();}
                    else{Check(Bounds(picker,window).Left>=Bounds(input,window).Left-1 && Bounds(button,window).Right<=Bounds(input,window).Right+1,"long model ID fits quick chat width");Save(window,"quick-model-long-id.png");}
                    aliasedProvider.ModelDisplayNames[longModel]=alias;pet.RefreshChatAppearance();window.UpdateLayout();
                    picker.Focus();picker.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,Key.F4){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump(.05);Check(picker.ContextMenu.IsOpen,"focused model picker opens with F4");Check(picker.ContextMenu.Items.OfType<MenuItem>().Count(m=>m.Name=="SelectTextModel")==3,"menu groups configured models across both providers");Save(picker.ContextMenu,small?"quick-model-menu.png":"chat-model-menu.png");picker.ContextMenu.IsOpen=false;
                    var aliasedItems=picker.ContextMenu.Items.OfType<MenuItem>().Where(m=>m.Header is TextBlock && ((TextBlock)m.Header).Text==alias).ToList();Check(aliasedItems.Count==2 && aliasedItems.Count(m=>m.IsChecked)==1 && (string)aliasedItems.Single(m=>m.IsChecked).Tag=="second/"+longModel,"identical Chinese aliases across providers remain distinct by provider and real model ID");
                    SaveHint(Find<Button>(window,"VoiceRecord"),small?"quick-dictation-hint.png":"chat-dictation-hint.png");
                    Check((string)button.Tag=="live" && Find<Button>(window,small?"StopQuickConversation":"StopConversation")==null,"idle composer has one live action");
                    input.Text="first";Check((string)button.Tag=="send","draft changes live action to send");Click(button);Pump(.05);
                    Check((string)button.Tag=="stop","empty busy composer shows stop");input.Text="queued";Check((string)button.Tag=="send" && button.IsEnabled,"busy composer with draft keeps send enabled");Click(button);Pump(.05);
                    Check(pet.InputQueue.Items.Count==1 && Find<Button>(window,"EditQueuedMessage")!=null && Find<Button>(window,"GuideQueuedMessage")!=null,"pending row exposes direct edit and guide actions");
                    Check(Find<Button>(small?(Window)chat:quick,"EditQueuedMessage")!=null,"both windows share queue immediately");
                    Check(!pet.CanApplyUpdate(true),"pending input blocks auto replacement");
                    Click(Find<Button>(window,"EditQueuedMessage"));var edit=Find<TextBox>(window,"QueueEditInput");edit.Text="edited while waiting";Click(Find<Button>(window,"SaveQueuedEdit"));
                    Check(pet.InputQueue.Items.Single().Text=="edited while waiting","inline queue edit updates stored entry");
                    Save(window,small?"quick-queue.png":"chat-queue.png");Click(button);Until(()=>!pet.InputQueue.IsRunning && !session.IsBusy);
                    Check(pet.InputQueue.IsPaused && pet.InputQueue.Items.Count==1,"stop pauses visible queue without discarding it");
                    Click(Find<Button>(window,"RemoveQueuedMessage"));Check(pet.InputQueue.Items.Count==0,"delete removes pending row");
                    pet.InputQueue.Resume();Pump(.1);
                    int history=session.History.Messages.Count;input.Text="已有草稿";window.UpdateLayout();Rect actionBounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));Click(Find<Button>(window,"VoiceRecord"));Pump(.1);
                    var finish=Find<Button>(window,"FinishDictation");Rect finishBounds=finish.TransformToAncestor(window).TransformBounds(new Rect(finish.RenderSize));
                    Check(Math.Abs(actionBounds.X-finishBounds.X)<1 && Math.Abs(actionBounds.Y-finishBounds.Y)<1 && Math.Abs(actionBounds.Width-finishBounds.Width)<1,"blue and black action buttons retain the same position and size: "+actionBounds+" / "+finishBounds);
                    Check(dictation.IsBusy && !session.IsBusy && Find<Button>(window,"FinishDictation").IsVisible,"idle dictation opens waveform without starting AI");
                    SaveHint(Find<Button>(window,"StopDictation"),small?"quick-stop-hint.png":"chat-stop-hint.png");SaveHint(finish,small?"quick-send-hint.png":"chat-send-hint.png");
                    Save(window,small?"quick-dictation.png":"chat-dictation.png");Click(Find<Button>(window,"StopDictation"));Pump(.2);
                    Check(input.Text.Contains("已有草稿") && input.Text.Contains("识别文字") && session.History.Messages.Count==history && !dictation.IsBusy,"dictation appends to draft without sending");
                    input.Clear();Click(Find<Button>(window,"VoiceRecord"));Pump(.1);window.Hide();Pump(.2);window.Show();Pump(.1);
                    Check(recorder.Disposed && input.Text.Length==0 && !dictation.IsBusy,"hiding the host cancels dictation and releases microphone without filling draft");
                    input.Text="AI in progress";Click(button);Pump(.05);Click(Find<Button>(window,"VoiceRecord"));Pump(.1);
                    Check(session.IsBusy && dictation.IsBusy && Find<Button>(window,"StopDictation").IsVisible,"dictation can run while AI replies");
                    Save(window,small?"quick-dictation-busy.png":"chat-dictation-busy.png");Click(Find<Button>(window,"StopDictation"));Pump(.15);
                    Check(session.IsBusy && !dictation.IsBusy && input.Text=="识别文字","left dictation stop fills draft while AI continues");input.Clear();
                    Click(Find<Button>(window,"VoiceRecord"));Pump(.1);Click(Find<Button>(window,"FinishDictation"));Pump(.2);
                    Check(input.Text.Length==0 && pet.InputQueue.Items.Single().Text=="识别文字","right dictation arrow queues recognized text while AI is busy");Click(button);Pump(.1);Click(Find<Button>(window,"RemoveQueuedMessage"));pet.InputQueue.Resume();
                    input.Text="原草稿";Click(Find<Button>(window,"VoiceRecord"));Pump(.1);Click(Find<Button>(window,"FinishDictation"));Pump(.2);
                    Check(session.IsBusy && input.Text.Length==0 && session.History.Messages.Any(m=>m.Role=="user" && m.Content.Contains("原草稿") && m.Content.Contains("识别文字")),"idle right dictation arrow sends combined draft and transcription");Click(button);Pump(.1);pet.InputQueue.Resume();
                    int requestsBeforeFailure=api.Requests.Count;input.Text="设备故障前的草稿";Click(Find<Button>(window,"VoiceRecord"));Pump(.05);recorder.Fail();Until(()=>!dictation.IsBusy);Pump(.1);
                    var failureStatus=Find<TextBlock>(window,small?"QuickStatus":"ChatStatus");
                    Check(recorder.Disposed && failureStatus.IsVisible && failureStatus.Text.Contains("麦克风设备已断开") && input.Text=="设备故障前的草稿" && api.Requests.Count==requestsBeforeFailure,"recording device failure restores composer and shows its reason without sending or losing draft");input.Clear();

                }
                ReorderSmoke(pet,chat,quick,api);ReorderSmoke(pet,quick,chat,api);
                cfg.Text=new ConnectionSettings();cfg.TextProviders.Clear();pet.RefreshChatAppearance();var emptyPicker=Find<Button>(quick,"TextModelPicker");Click(emptyPicker);Pump(.05);
                Check(Find<TextBlock>(quick,"CurrentTextModel").Text=="选择模型" && emptyPicker.ContextMenu.Items.OfType<MenuItem>().Any(m=>m.Name=="ConfigureTextModels" && m.IsEnabled),"empty model list keeps an accessible AI settings action");emptyPicker.ContextMenu.IsOpen=false;
                if(nativeDrag){cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"};quick.Hide();NativeReorderSmoke(pet,chat,api);}
                Console.WriteLine("Composer smoke: "+passed+" passed; native drag coverage: "+(nativeDrag?"PASSED":"NOT TESTED")+"; screenshots: "+folder);return 0;
            }
            catch(Exception e){Console.WriteLine(e);return 1;}
            finally{if(pet!=null)pet.Close();Pump(.3);app.Shutdown();}
        }
        [StructLayout(LayoutKind.Explicit,Size=40)]struct KeyboardEvent
        {[FieldOffset(0)]public uint Type;[FieldOffset(8)]public ushort Key;[FieldOffset(10)]public ushort Scan;[FieldOffset(12)]public uint Flags;[FieldOffset(16)]public uint Time;[FieldOffset(24)]public UIntPtr Extra;}
        [DllImport("user32.dll",EntryPoint="SendInput",SetLastError=true)]static extern uint SendKeysNative(uint count,KeyboardEvent[] inputs,int size);
        static void KeyInput(ushort key,ushort scan,uint flags)
        {if(IntPtr.Size!=8)throw new Exception("Native keyboard fixture requires x64");if(SendKeysNative(1,new[]{new KeyboardEvent{Type=1,Key=key,Scan=scan,Flags=flags}},40)!=1)throw new Exception("Native keyboard input failed");}
        static void NativeClick(Window window,FrameworkElement target)
        {
            var hwnd=new WindowInteropHelper(window).Handle;window.Activate();SetForegroundWindow(hwnd);Pump(.06);Point p=target.PointToScreen(new Point(Math.Min(25,target.ActualWidth/2),Math.Min(15,target.ActualHeight/2)));
            if(!SetCursorPos((int)Math.Round(p.X),(int)Math.Round(p.Y)))throw new Exception("SetCursorPos failed");Pump(.03);
            if(WindowFromPoint(new NativePoint{X=(int)Math.Round(p.X),Y=(int)Math.Round(p.Y)})!=hwnd)throw new Exception("Placeholder native click aborted: target is not fixture; expected="+hwnd+" foreground="+GetForegroundWindow()+" hit="+WindowFromPoint(new NativePoint{X=(int)Math.Round(p.X),Y=(int)Math.Round(p.Y)})+" point="+p+" bounds="+DesktopInterop.Bounds(hwnd));
            MouseInputEvent(2,0,0);MouseInputEvent(4,0,0);Pump(.08);
        }
        static TextBlock Placeholder(DependencyObject node)
        {var text=node as TextBlock;if(text!=null&&text.Name=="ComposerPlaceholder")return text;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var result=Placeholder(VisualTreeHelper.GetChild(node,i));if(result!=null)return result;}return null;}
        static void PlaceholderSmoke(Window chat,Window quick)
        {
            Point cursor=DesktopInterop.Cursor();
            try {foreach(var window in new[]{chat,quick}) {
                (window==chat?quick:chat).Hide();window.Show();window.Topmost=true;window.Activate();Pump(.15);
                string tag=window==chat?"chat":"quick";var input=Find<MarkdownEditor>(window,window==chat?"MessageInput":"QuickMessageInput");var picker=Find<Button>(window,"TextModelPicker");var hint=Placeholder(window);Check(hint!=null,"placeholder exists "+tag);
                input.Clear();NativeClick(window,picker);picker.ContextMenu.IsOpen=false;Pump(.08);
                var field=(FrameworkElement)VisualTreeHelper.GetParent(input);window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);Console.WriteLine("DPI native "+tag+" "+dpi.PixelsPerInchX+"x"+dpi.PixelsPerInchY);
                Check(!input.IsKeyboardFocusWithin && hint.IsVisible,"unfocused empty input shows hint "+tag);Save(window,tag+"-placeholder-empty.png");
                Console.WriteLine("TEXT rect="+input.GetRectFromCharacterIndex(0)+" hint margin="+hint.Margin+" lineheight="+hint.LineHeight+" input padding="+input.Padding);var blankBounds=new Dictionary<int,Rect>();foreach(int scale in new[]{96,120,144,192})blankBounds[scale]=GlyphBounds(field,scale,tag+"-hint-"+scale+"dpi.png");
                input.Text=hint.Text;window.UpdateLayout();Pump(.06);
                foreach(int scale in new[]{96,120,144,192}){Rect actual=GlyphBounds(field,scale,tag+"-text-"+scale+"dpi.png"),expected=blankBounds[scale];Console.WriteLine("GLYPH "+tag+" dpi="+scale+" hint="+expected+" text="+actual);Check(Math.Abs(actual.Left-expected.Left)<=1 && Math.Abs(actual.Top-expected.Top)<=1 && Math.Abs(actual.Height-expected.Height)<=1,"placeholder and actual glyph origins match at render DPI "+scale+" "+tag);}
                input.Clear();NativeClick(window,input);Check(input.IsKeyboardFocused && hint.IsVisible,"native focus preserves empty hint beside caret "+tag);
                var editorSurface=(Border)VisualTreeHelper.GetChild(input,0);Check(editorSurface.Background==Brushes.Transparent,"focused composer editor has no gray overlay "+tag);Save(window,tag+"-placeholder-focused.png");
                if(GetForegroundWindow()!=new WindowInteropHelper(window).Handle)throw new Exception("Native typing aborted: foreground is not fixture");foreach(char c in "中文输入 ABC"){KeyInput(0,c,4);KeyInput(0,c,6);}Pump(.12);Check(input.Text=="中文输入 ABC" && !hint.IsVisible,"native Unicode input displays without hint overlap "+tag);Save(window,tag+"-placeholder-typed.png");
                input.SelectAll();KeyInput(8,0,0);KeyInput(8,0,2);Pump(.1);Check(input.Text.Length==0 && input.IsKeyboardFocused && hint.IsVisible,"native clearing restores hint while focused "+tag);
                NativeClick(window,picker);picker.ContextMenu.IsOpen=false;Pump(.1);Check(hint.IsVisible,"native blur restores empty hint "+tag);Save(window,tag+"-placeholder-cleared.png");
                input.Text="第一行中文\n第二行输入\n第三行输入";window.UpdateLayout();Pump(.08);Check(!hint.IsVisible && input.GetRectFromCharacterIndex(6).Top>input.GetRectFromCharacterIndex(0).Top,"multiline text stays unobstructed "+tag);Save(window,tag+"-placeholder-multiline.png");input.Clear();
            }}finally{MouseInputEvent(4,0,0);SetCursorPos((int)cursor.X,(int)cursor.Y);}
        }
        static Rect GlyphBounds(FrameworkElement field,int dpi,string file)
        {
            field.UpdateLayout();double ratio=dpi/96d;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(field.ActualWidth*ratio),(int)Math.Ceiling(field.ActualHeight*ratio),dpi,dpi,PixelFormats.Pbgra32);bitmap.Render(field);var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(folder,file)))png.Save(stream);
            int left=bitmap.PixelWidth,top=bitmap.PixelHeight,right=-1,bottom=-1;for(int y=0;y<Math.Min(bitmap.PixelHeight,(int)(40*ratio));y++)for(int x=0;x<Math.Min(bitmap.PixelWidth,(int)(180*ratio));x++){int p=(y*bitmap.PixelWidth+x)*4;if(bytes[p+3]<100)continue;int a=bytes[p+3];if(bytes[p]*255/a>225 && bytes[p+1]*255/a>225 && bytes[p+2]*255/a>225)continue;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            if(right<left)throw new Exception("No visible glyph pixels in "+file);return new Rect(left,top,right-left+1,bottom-top+1);
        }
        static void NativeReorderSmoke(PetWindow pet,Window window,Api api)
        {
            Point originalCursor=DesktopInterop.Cursor();bool mouseDown=false,moved=false;
            try
            {
                if((GetAsyncKeyState(1)&0x8000)!=0)throw new InvalidOperationException("Native drag aborted: left mouse button is already held.");
                window.Show();window.WindowState=WindowState.Normal;window.Topmost=true;window.Activate();var hwnd=new WindowInteropHelper(window).Handle;SetForegroundWindow(hwnd);Pump(.2);
                pet.InputQueue.Enqueue("原生拖拽期间正在回复");Until(()=>pet.Conversation.IsBusy);
                foreach(string text in new[]{"原生排队第一条","原生排队第二条","原生排队第三条"})pet.InputQueue.Enqueue(text);Pump(.15);window.UpdateLayout();
                var items=pet.InputQueue.Items;var composer=Find<ConversationComposer>(window,"");var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var controller=(QueueReorderController)typeof(ConversationComposer).GetField("reorder",flags).GetValue(composer);var rows=(StackPanel)typeof(ConversationComposer).GetField("rows",flags).GetValue(composer);
                var source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==items[2].Id);var target=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==items[0].Id);var grip=Find<Button>(source,"ReorderQueuedMessage");
                Point start=grip.PointToScreen(new Point(grip.ActualWidth*.5,grip.ActualHeight*.5)),end=target.PointToScreen(new Point(grip.ActualWidth*.5,target.ActualHeight*.5));
                if(GetForegroundWindow()!=hwnd)throw new InvalidOperationException("Native drag aborted: fixture is not the foreground window.");
                if(!SetCursorPos((int)Math.Round(start.X),(int)Math.Round(start.Y)))throw new InvalidOperationException("SetCursorPos failed; Win32 "+Marshal.GetLastWin32Error());moved=true;Pump(.1);
                Check(grip.IsMouseOver && grip.Opacity==1,"native pointer hover exposes the actual six-dot handle");
                // Re-check immediately before EVERY injected button down. Never
                // click if another process or an overlapping popup took focus.
                if(GetForegroundWindow()!=hwnd || WindowFromPoint(new NativePoint{X=(int)Math.Round(start.X),Y=(int)Math.Round(start.Y)})!=hwnd)throw new InvalidOperationException("Native drag aborted: foreground or hit window changed before mouse down.");
                MouseInputEvent(2,0,0);mouseDown=true;Pump(.05);
                for(int step=1;step<=12;step++)
                {
                    if(GetForegroundWindow()!=hwnd)throw new InvalidOperationException("Native drag aborted: foreground changed during drag.");
                    var desktop=System.Windows.Forms.SystemInformation.VirtualScreen;double fraction=step/12d;
                    int x=(int)Math.Round(start.X+(end.X-start.X)*fraction),y=(int)Math.Round(start.Y+(end.Y-start.Y)*fraction);
                    MouseInputEvent(0x8000|0x4000|1,(int)Math.Round((x-desktop.Left)*65535d/(desktop.Width-1)),(int)Math.Round((y-desktop.Top)*65535d/(desktop.Height-1)));Pump(.025);
                }
                Check(controller.IsDragging && grip.IsMouseCaptured && source.Opacity==0,"native mouse down and movement create the captured floating row");
                Check(typeof(QueueReorderController).GetField("ghost",flags).GetValue(controller)!=null && pet.InputQueue.Items[0].Id==items[0].Id,"native drag shows an overlay without prematurely changing the queue");
                Save(window,"native-queue-dragging.png");MouseInputEvent(4,0,0);mouseDown=false;Pump(.25);
                Check(!controller.IsDragging && String.Join(",",pet.InputQueue.Items.Select(m=>m.Id))==String.Join(",",new[]{items[2].Id,items[0].Id,items[1].Id}),"native mouse release commits the requested pending order");Save(window,"native-queue-reordered.png");
                for(int remaining=3;remaining>=0;remaining--){api.Complete();int expected=remaining;Until(()=>expected==0?!pet.InputQueue.IsRunning:pet.InputQueue.Items.Count==expected-1 && pet.Conversation.IsBusy);}
                Check(String.Join(",",api.Texts.Skip(api.Texts.Count-4))=="原生拖拽期间正在回复,原生排队第三条,原生排队第一条,原生排队第二条","native drag order controls subsequent real queue dispatch");
            }
            finally{try{if(mouseDown)MouseInputEvent(4,0,0);}finally{if(moved && !SetCursorPos((int)originalCursor.X,(int)originalCursor.Y))Console.Error.WriteLine("Cursor restoration failed; Win32 "+Marshal.GetLastWin32Error());}}
        }
        static void MouseInputEvent(uint flags,int x,int y)
        {
            var input=new NativeInput{Type=0,Mouse=new MouseInput{X=x,Y=y,Flags=flags}};
            if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(NativeInput)))!=1)throw new InvalidOperationException("SendInput failed; Win32 "+Marshal.GetLastWin32Error());
        }
        static void ReorderSmoke(PetWindow pet,Window window,Window other,Api api)
        {
            Console.WriteLine("Programmatic WPF reorder block; this block does not exercise native mouse drag.");
            window.Show();window.Activate();pet.InputQueue.Enqueue("正在回复的消息");Until(()=>pet.Conversation.IsBusy);
            foreach(string text in new[]{"第一条排队内容","第二条排队内容","第三条排队内容"})pet.InputQueue.Enqueue(text);Pump(.1);window.UpdateLayout();
            var original=pet.InputQueue.Items;string a=original[0].Id,b=original[1].Id,c=original[2].Id;
            var composer=Find<ConversationComposer>(window,"");var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var controller=(QueueReorderController)typeof(ConversationComposer).GetField("reorder",flags).GetValue(composer);
            var rows=(StackPanel)typeof(ConversationComposer).GetField("rows",flags).GetValue(composer);
            var source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==c);var grip=Find<Button>(source,"ReorderQueuedMessage");
            source.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});Check(grip.Opacity==1,"hover exposes six-dot reorder handle");
            Point start=source.TranslatePoint(new Point(8,13),rows);controller.Begin(grip,source,start);controller.Move(new Point(start.X+7,start.Y-60));Pump(.08);
            Check(controller.IsDragging && source.Opacity==0,"drag uses a floating preview while preserving the source layout slot");
            var ghost=typeof(QueueReorderController).GetField("ghost",flags).GetValue(controller);var bitmap=(BitmapSource)ghost.GetType().GetField("bitmap",flags).GetValue(ghost);var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            Check(Enumerable.Range(0,pixels.Length/4).Count(i=>pixels[i*4+3]>30)>100,"floating drag preview retains visible message text and tools");
            var displaced=rows.Children.OfType<Grid>().First(r=>(string)r.Tag==a).RenderTransform as TranslateTransform;
            Check(displaced!=null && displaced.Y>0,"neighbor rows animate toward the insertion gap");
            Check(pet.InputQueue.Items[0].Id==a,"drag preview does not prematurely reorder pending requests");
            Save(window,window is QuickChatWindow?"quick-queue-dragging.png":"chat-queue-dragging.png");controller.Drop();Pump(.25);
            Check(String.Join(",",pet.InputQueue.Items.Select(m=>m.Id))==String.Join(",",new[]{c,a,b}),"dropping commits the new pending sequence");
            var otherComposer=Find<ConversationComposer>(other,"");var otherRows=(StackPanel)typeof(ConversationComposer).GetField("rows",flags).GetValue(otherComposer);
            Check(String.Join(",",otherRows.Children.OfType<Grid>().Select(r=>(string)r.Tag))==String.Join(",",new[]{c,a,b}),"reordered sequence appears in both shared composers");
            Save(window,window is QuickChatWindow?"quick-queue-reordered.png":"chat-queue-reordered.png");
            source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==c);controller.MoveByKeyboard(source,1);source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==c);
            Check(pet.InputQueue.Items[1].Id==c && Find<Button>(source,"ReorderQueuedMessage").IsKeyboardFocused,"keyboard reorder retains focus on the moved message handle");controller.MoveByKeyboard(source,-1);
            source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==b);grip=Find<Button>(source,"ReorderQueuedMessage");start=source.TranslatePoint(new Point(8,13),rows);controller.Begin(grip,source,start);controller.Move(new Point(start.X,start.Y-60));
            grip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Check(!controller.IsDragging && pet.InputQueue.Items[0].Id==c && source.Opacity==1,"Escape restores the row without committing the preview");
            source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==b);grip=Find<Button>(source,"ReorderQueuedMessage");start=source.TranslatePoint(new Point(8,13),rows);controller.Begin(grip,source,start);controller.Move(new Point(start.X,start.Y-60));
            controller.Drop();pet.InputQueue.Remove(a);Pump(.2);Check(!controller.IsDragging && String.Join(",",pet.InputQueue.Items.Select(m=>m.Id))==c+","+b,"deleting a row during drop animation cancels its stale commit safely");
            source=rows.Children.OfType<Grid>().Single(r=>(string)r.Tag==c);grip=Find<Button>(source,"ReorderQueuedMessage");start=source.TranslatePoint(new Point(8,13),rows);controller.Begin(grip,source,start);controller.Move(new Point(start.X,start.Y+35));
            api.Complete();Until(()=>pet.InputQueue.Items.Count==1);controller.Drop();Pump(.15);
            Check(!controller.IsDragging && pet.InputQueue.Items.Single().Id==b,"asynchronous dequeue during drag cannot reinsert the now-running message");
            api.Complete();Until(()=>pet.InputQueue.Items.Count==0);api.Complete();Until(()=>!pet.InputQueue.IsRunning);
            Check(String.Join(",",api.Texts.Skip(api.Texts.Count-3))=="正在回复的消息,第三条排队内容,第二条排队内容","UI drop controls actual subsequent request order");
        }
        static void Click(Button b){if(b==null || !b.IsEnabled)throw new Exception("Button unavailable");b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static Rect Bounds(FrameworkElement element,Window window){return element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));}
        static void SelectModel(Window window,string provider,string model){var picker=Find<Button>(window,"TextModelPicker");Click(picker);var item=picker.ContextMenu.Items.OfType<MenuItem>().Single(m=>(string)m.Tag==provider+"/"+model);item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));picker.ContextMenu.IsOpen=false;Pump(.05);}
        static void Check(bool c,string s){if(!c)throw new Exception(s);passed++;Console.WriteLine("PASS "+s);}
        static T Find<T>(DependencyObject n,string name)where T:FrameworkElement{var t=n as T;if(t!=null && t.Name==name)return t;for(int i=0;i<VisualTreeHelper.GetChildrenCount(n);i++){t=Find<T>(VisualTreeHelper.GetChild(n,i),name);if(t!=null)return t;}return null;}
        static void Until(Func<bool> ready){var deadline=DateTime.UtcNow.AddSeconds(5);while(!ready() && DateTime.UtcNow<deadline)Pump(.05);if(!ready())throw new Exception("Timed out waiting for operation cleanup");}
        static void Pump(double seconds){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};t.Tick+=delegate{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
        static void SaveHint(Button button,string file){var hint=(ToolTip)button.ToolTip;hint.IsOpen=true;Pump(.1);Save(hint,file);hint.IsOpen=false;}
        static void Save(FrameworkElement w,string file){w.UpdateLayout();var b=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);b.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));using(var s=File.Create(Path.Combine(folder,file)))png.Save(s);}
        sealed class Api:IConversationApi
        {public readonly List<ConnectionSettings> Requests=new List<ConnectionSettings>();public readonly List<string> Texts=new List<string>();TaskCompletionSource<string> pending;public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken t){Requests.Add(s);Texts.Add(m.Last().Content);var current=new TaskCompletionSource<string>();pending=current;using(t.Register(()=>current.TrySetCanceled()))return await current.Task;}public void Complete(){pending.TrySetResult("done");}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken t){return Task.FromResult("识别文字");}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string x,CancellationToken t){throw new NotSupportedException();}public void Dispose(){}}
        sealed class Recorder:IAudioRecorder,IPcmMicrophone
        {public bool Disposed;public bool IsRecording{get{return !Disposed;}}public event Action<byte[]> PcmAvailable;public event Action<string> Failed;public void Start(){if(PcmAvailable!=null){var data=new byte[3200];for(int i=0;i<data.Length;i+=2){short sample=(short)(Math.Sin(i*.04)*15000);data[i]=(byte)sample;data[i+1]=(byte)(sample>>8);}PcmAvailable(data);}}public void Fail(){if(Failed!=null)Failed("麦克风设备已断开，请检查输入设备。");}public Task<byte[]> FinishAsync(){return Task.FromResult(WaveData.EncodePcm(new byte[16000]));}public void Dispose(){Disposed=true;}}
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int v){}public void Dispose(){}}
    }
}
