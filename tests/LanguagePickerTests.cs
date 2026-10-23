using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
namespace Dafeiyu.Tests
{
    public static class LanguagePickerTests
    {
        static Application app;static int checks;
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Click(Window window,string name){All<Button>(window).Single(button=>button.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static async Task Settle(){await app.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);await Task.Delay(20);}
        static byte[] Pixel(BitmapSource image){var pixel=new byte[4];image.CopyPixels(new Int32Rect(10,60,1,1),pixel,4,0);return pixel;}
        static string Rgb(BitmapSource image){var p=Pixel(image);return p[2]+","+p[1]+","+p[0];}
        static void Save(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))encoder.Save(output);}
        static void Capture(FrameworkElement view,string path)
        {
            view.UpdateLayout();var frame=(Border)((Window)view).Content;var expected=((SolidColorBrush)frame.Background).Color;
            // Snapshot only this control tree, resolving bound Freezable colors to
            // their current values instead of reusing VisualBrush channel caches.
            var image=UiControlSnapshot.Capture(view);var pixel=Pixel(image);
            Console.WriteLine("Capture "+path+" logical="+expected+" snapshot="+Rgb(image));
            Save(image,path);
            Check(pixel[2]==expected.R&&pixel[1]==expected.G&&pixel[0]==expected.B,"rendered dialog surface matches the active theme: "+path+" actual="+Rgb(image)+" expected="+expected);
        }
        static async Task Run(string locale)
        {
            TextResources.ConfigureStartupLanguage(locale);string root=Path.GetFullPath(Path.Combine("work","language-picker","wpf",locale));Directory.CreateDirectory(root);
            var settings=new PetSettings();var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings);
            foreach(string mode in new[]{"light","dark"})
            {
                ThemeManager.Apply(mode);int saves=0;var dialog=new LanguagePickerWindow("system",code=>{store.SaveUiLanguage(settings,code);saves++;});dialog.Show();await Settle();
                var list=All<ListBox>(dialog).Single(item=>item.Name=="LanguageChoiceList");
                var frame=(Border)dialog.Content;var expected=(Color)ColorConverter.ConvertFromString(ThemeColors.Resolve("#FAFAFB",mode=="dark"));
                Check(ThemeManager.IsDark==(mode=="dark")&&((SolidColorBrush)frame.Background).Color==expected,mode+" dialog surface follows the shared theme palette");
                Check(list.Items.Count==UiLanguages.Choices.Count&&((ListBoxItem)list.SelectedItem).Tag as string=="system",mode+" shows all choices and the saved system preference");
                foreach(int width in new[]{640,360})
                {
                    dialog.Width=width;dialog.Height=width==640?560:320;await Settle();
                    Check(list.ActualWidth>100&&list.ActualHeight>40&&list.ActualWidth<=dialog.ActualWidth,mode+" language list fits dialog "+width);
                    var scroll=All<ScrollViewer>(list).First();scroll.ScrollToBottom();await Settle();
                    Check(width!=360||scroll.ScrollableHeight>0&&scroll.VerticalOffset>0,mode+" narrow list scrolls to the last language");
                    var last=(ListBoxItem)list.Items[list.Items.Count-1];Check(last.IsVisible&&last.ActualWidth<=list.ActualWidth,mode+" final choice fits list width");
                    Check(All<ScrollBar>(list).Any(bar=>bar.Orientation==Orientation.Vertical),mode+" uses the standard WPF scroll bar");
                    Capture(dialog,Path.Combine(root,mode+"-"+width+".png"));
                }
                list.SelectedItem=null;await Settle();Check(!All<Button>(dialog).Single(button=>button.Name=="SaveLanguageSelection").IsEnabled,mode+" clearing selection disables Save instead of submitting an empty language");
                list.SelectedIndex=0;((ListBoxItem)list.SelectedItem).Focus();list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(list),0,Key.End){RoutedEvent=Keyboard.KeyDownEvent});await Settle();
                Check(list.SelectedIndex==list.Items.Count-1,mode+" keyboard End selects the last language");
                list.SelectedItem=list.Items.OfType<ListBoxItem>().Single(item=>(string)item.Tag=="en_US");Click(dialog,"SaveLanguageSelection");await Settle();
                Check(saves==1&&!dialog.IsVisible&&store.Load().UiLanguage=="en_US",mode+" Save persists the choice once and closes the dialog");
                var cancel=new LanguagePickerWindow("en_US",code=>saves++);cancel.Show();await Settle();All<ListBox>(cancel).Single().SelectedIndex=0;Click(cancel,"CancelLanguageSelection");await Settle();Check(saves==1&&store.Load().UiLanguage=="en_US",mode+" Cancel preserves the previous preference");
                var failed=new LanguagePickerWindow("en_US",code=>{throw new IOException("fixture");});failed.Show();await Settle();Click(failed,"SaveLanguageSelection");await Settle();Check(failed.IsVisible&&All<TextBlock>(failed).Any(text=>text.Name=="LanguageSelectionNotice"&&text.Text==TextResources.Get("petWindow.saveSettings.notice")),mode+" persistence failure remains visible and does not close the selector");failed.Close();
            }
            Console.WriteLine("LanguagePickerTests "+locale+": "+checks+" passed");
        }
        [STAThread]public static int Main(string[] args)
        {
            string locale=args.Length==0?"zh_CN":args[0];app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(locale);result=0;}catch(Exception error){Console.Error.WriteLine("FAIL LanguagePickerTests: "+error);}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();app.Shutdown();}}));app.Run();return result;
        }
    }
}
