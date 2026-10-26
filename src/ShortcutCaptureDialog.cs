using System;using System.Linq;using System.Windows;using System.Windows.Controls;using System.Windows.Input;
namespace Dafeiyu {
internal static class ShortcutCapture {
internal static string Format(Key key,ModifierKeys modifiers){
if(key==Key.None||key==Key.System||key==Key.ImeProcessed||key==Key.DeadCharProcessed||key==Key.LeftCtrl||key==Key.RightCtrl||key==Key.LeftAlt||key==Key.RightAlt||key==Key.LeftShift||key==Key.RightShift||key==Key.LWin||key==Key.RWin)return null;
string value=((modifiers&ModifierKeys.Control)!=0?"Ctrl+":"")+((modifiers&ModifierKeys.Alt)!=0?"Alt+":"")+((modifiers&ModifierKeys.Shift)!=0?"Shift+":"")+((modifiers&ModifierKeys.Windows)!=0?"Win+":"")+((System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(key)).ToString();
string normalized=ShortcutCatalog.Normalize(value);return normalized.Length==0?null:normalized;
}}
internal sealed class ShortcutCaptureDialog:Window {
bool ending;readonly PetSettings settings;readonly string action;readonly TextBlock candidate,notice;readonly Button apply;
internal string Combination {get;private set;}
internal ShortcutCaptureDialog(PetSettings cfg,string id){settings=cfg;action=id;Name="ShortcutCaptureDialog";Title=TextResources.Get("shortcutCapture.title");Width=450;Height=290;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=ThemeManager.Surface;Foreground=Ui.Ink;
var root=new StackPanel{Margin=new Thickness(22)};root.Children.Add(LocalizedUi.Text(()=>TextResources.Get("shortcutCapture.prompt"),14));candidate=Ui.Text("",23);candidate.Name="CapturedCombination";candidate.Margin=new Thickness(0,16,0,12);root.Children.Add(candidate);notice=Ui.Text("",12,Ui.Muted);notice.TextWrapping=TextWrapping.Wrap;root.Children.Add(notice);
var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,0)};apply=LocalizedUi.Button(()=>TextResources.Get("common.save"));apply.IsEnabled=false;apply.IsDefault=true;apply.Name="AcceptCapturedShortcut";apply.Click+=delegate{Finish(true);};var clear=LocalizedUi.Button(()=>TextResources.Get("shortcutCapture.clear"));clear.Click+=delegate{Combination="";Finish(true);};var cancel=LocalizedUi.Button(()=>TextResources.Get("common.cancel"));cancel.IsCancel=true;cancel.Click+=delegate{Finish(false);};buttons.Children.Add(apply);buttons.Children.Add(clear);buttons.Children.Add(cancel);root.Children.Add(buttons);Content=root;LocalizedUi.Set(this,"Title",()=>TextResources.Get("shortcutCapture.title"));
PreviewKeyDown+=OnKey;Closing+=delegate{ending=true;};Deactivated+=delegate{if(IsVisible&&!ending){ending=true;Close();}};}
void Finish(bool accepted){if(ending)return;ending=true;DialogResult=accepted;}
void OnKey(object sender,KeyEventArgs e){var key=e.Key==Key.System?e.SystemKey:e.Key;var modifiers=Keyboard.Modifiers;
if(modifiers==ModifierKeys.None&&(key==Key.Tab||key==Key.Enter||key==Key.Space))return;
e.Handled=true;CaptureKey(key,modifiers,e.IsRepeat);}
internal void CaptureKey(Key key,ModifierKeys modifiers,bool repeat){if(repeat)return;if(key==Key.Escape||key==Key.F4&&(modifiers&ModifierKeys.Alt)!=0){Finish(false);return;}string value=ShortcutCapture.Format(key,modifiers);if(value==null)return;candidate.Text=ShortcutCatalog.Label(value);string error;if(!ShortcutCatalog.Validate(settings,action,value,out error)){notice.Text=error;Combination=null;apply.IsEnabled=false;return;}Combination=value;notice.Text="";apply.IsEnabled=true;}
}
public partial class PetWindow {
int hotkeyCaptureDepth;
internal IDisposable SuspendHotkeysForCapture(){hotkeyCaptureDepth++;if(hotkeyCaptureDepth==1&&hotkeys!=null){hotkeys.Dispose();hotkeys=null;}return new HotkeyCaptureScope(this);}
sealed class HotkeyCaptureScope:IDisposable {PetWindow owner;internal HotkeyCaptureScope(PetWindow p){owner=p;}public void Dispose(){var p=owner;if(p==null)return;owner=null;if(--p.hotkeyCaptureDepth!=0||p.closing||p.handle==IntPtr.Zero)return;p.hotkeys=new HotkeyRegistry(p.handle);foreach(var pair in p.Settings.Hotkeys.Where(x=>ShortcutCatalog.All.Any(d=>d.Id==x.Key&&d.Global))){string error;if(!p.hotkeys.TryBind(pair.Key,pair.Value,out error))p.HotkeyWarning=error;}}}
}
}
