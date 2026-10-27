using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Dafeiyu;
using DeskPet.Desktop;

internal static class LanguagePickerChecks
{
    internal static int Run(string code)
    {
        TextResources.ConfigureStartupLanguage(code);int checks=0;string root=Path.GetFullPath(Path.Combine("work","language-picker","avalonia",code));Directory.CreateDirectory(root);
        void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
        void Settle(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
        void Click(Window dialog,string name)=>dialog.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach(string mode in new[]{"light","dark"})
        {
            DesktopTheme.Apply(mode);int saves=0;string saved="system";var dialog=new LanguagePickerWindow(saved,selected=>{saved=selected;saves++;});dialog.Show();Settle();
            var list=dialog.GetLogicalDescendants().OfType<ListBox>().Single(item=>item.Name=="LanguageChoiceList");var items=list.ItemsSource!.Cast<ListBoxItem>().ToArray();
            Check(items.Length==UiLanguages.Choices.Count&&((ListBoxItem)list.SelectedItem!).Tag as string=="system",mode+" all languages and saved system preference");
            foreach(int width in new[]{640,360})
            {
                dialog.Width=width;dialog.Height=width==640?560:320;Settle();Check(list.Bounds.Width>100&&list.Bounds.Height>40&&list.Bounds.Width<=dialog.Bounds.Width,mode+" list fits "+width);
                var scroll=list.GetVisualDescendants().OfType<ScrollViewer>().First();scroll.Offset=new Vector(0,scroll.Extent.Height);Settle();
                Check(width!=360||scroll.Extent.Height>scroll.Viewport.Height&&scroll.Offset.Y>0,mode+" narrow list scrolls");Check(list.GetVisualDescendants().OfType<ScrollBar>().Any(bar=>bar.Orientation==Avalonia.Layout.Orientation.Vertical),mode+" standard themed scrollbar");
                using var image=dialog.CaptureRenderedFrame();Check(image!=null,mode+" dialog renders");image!.Save(Path.Combine(root,mode+"-"+width+".png"),new PngBitmapEncoderOptions());
            }
            list.SelectedItem=null;Settle();Check(!dialog.GetLogicalDescendants().OfType<Button>().Single(button=>button.Name=="SaveLanguageSelection").IsEnabled,mode+" clearing selection disables Save");
            list.SelectedIndex=0;items[0].Focus();dialog.KeyPress(Key.End,default,default,null);dialog.KeyRelease(Key.End,default,default,null);Settle();Check(list.SelectedIndex==items.Length-1,mode+" keyboard End selects the last language");
            list.SelectedItem=items.Single(item=>(string)item.Tag! == "en_US");Click(dialog,"SaveLanguageSelection");Settle();Check(saves==1&&saved=="en_US"&&!dialog.IsVisible,mode+" Save applies once and closes");
            var cancel=new LanguagePickerWindow(saved,selected=>saves++);cancel.Show();Settle();cancel.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex=0;Click(cancel,"CancelLanguageSelection");Settle();Check(saves==1,mode+" Cancel does not save");
            var failure=new LanguagePickerWindow(saved,selected=>throw new IOException("fixture"));failure.Show();Settle();Click(failure,"SaveLanguageSelection");Settle();Check(failure.IsVisible&&failure.GetLogicalDescendants().OfType<TextBlock>().Any(text=>text.Name=="LanguageSelectionNotice"&&text.Text==TextResources.Get("petWindow.saveSettings.notice")),mode+" failed save keeps selector open with feedback");failure.Close();
        }
        return checks;
    }
}
