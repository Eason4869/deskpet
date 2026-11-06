using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu.Tests
{
    public static class SecretFieldChecks
    {
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
        static void Capture(Window window,string state)
        {
            window.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using(var stream=System.IO.File.Create(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"secret-field-"+state+".png")))encoder.Save(stream);
        }
        public static void Run()
        {
            var password=new PasswordBox{Name="FixtureKey",Password="dummy-key-中文",MaxLength=8192};
            var field=new SecretField(password);var window=new Window();Ui.Window(window,"Key",300,120);window.Content=new Border{Padding=new Thickness(16),Child=field};window.Show();window.UpdateLayout();
            try{
                var plain=field.Children.OfType<TextBox>().Single();var button=field.Children.OfType<Button>().Single();int changes=0;password.PasswordChanged+=delegate{changes++;};
                Check(password.IsVisible&&!plain.IsVisible&&plain.Text=="","secret starts masked without a plaintext copy");
                Capture(window,"light-hidden");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(plain.IsVisible&&!password.IsVisible&&plain.Text==password.Password,"eye reveals current value");
                Check(changes==0,"revealing does not edit or save the key");
                password.IsEnabled=false;Check(!plain.IsEnabled&&!button.IsEnabled,"disabled credentials also disable visible editing and the eye");password.IsEnabled=true;
                Capture(window,"light-visible");ThemeManager.Apply("dark");Capture(window,"dark-visible");Check(((SolidColorBrush)plain.Foreground).Color==((SolidColorBrush)Ui.Ink).Color,"revealed text follows the dark theme ink");ThemeManager.Apply("light");
                plain.Text="edited-dummy-key";Check(password.Password==plain.Text&&changes==1,"visible edits reach the original password field once");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(password.IsVisible&&!plain.IsVisible&&plain.Text==""&&password.Password=="edited-dummy-key","hiding clears only the visible copy");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));password.Password="other-provider-dummy";
                Check(password.IsVisible&&!plain.IsVisible&&plain.Text=="","programmatic replacement masks the next provider");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.Content=null;
                Check(plain.Text==""&&plain.Visibility==Visibility.Collapsed,"leaving the page clears the visible copy");
                Check(button.ActualWidth>=28,"eye has a usable click target");
            }finally{window.Close();}
        }
    }
}
