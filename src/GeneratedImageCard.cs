using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu
{
    internal sealed class GeneratedImageCard:Border
    {
        readonly PetWindow pet;readonly Window host;readonly GeneratedImage asset;readonly TextBlock notice;
        internal GeneratedImageCard(PetWindow owner,Window window,GeneratedImage image,Action<GeneratedImage> regenerate=null)
        {
            pet=owner;host=window;asset=image.Copy();Name="GeneratedImageCard";Tag=asset.Id;CornerRadius=new CornerRadius(12);BorderThickness=new Thickness(1);BorderBrush=Ui.Line;Background=ThemeManager.Surface;Padding=new Thickness(10);Margin=new Thickness(0,0,12,14);MaxWidth=460;HorizontalAlignment=HorizontalAlignment.Left;
            var body=new StackPanel();Child=body;
            var thumbnail=new Image{Name="GeneratedImageThumbnail",MaxHeight=300,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Cursor=System.Windows.Input.Cursors.Hand};
            try{int width,height;bool limited;thumbnail.Source=ChatImagePreview.DecodeGeneratedImage(pet.ImageStore.Read(asset.Id),480,out width,out height,out limited);thumbnail.MouseLeftButtonUp+=delegate{Preview();};body.Children.Add(thumbnail);}
            catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException||e is FormatException))throw;body.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeD")),12,Ui.Muted));}
            var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};var preview=LocalizedUi.Button(()=>(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeE")));preview.Name="PreviewGeneratedImage";preview.Click+=delegate{Preview();};actions.Children.Add(preview);
            var save=LocalizedUi.Button(()=>(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeF")));save.Name="SaveGeneratedImage";save.Click+=delegate{Run(()=>SaveOriginal(host,pet.ImageStore,asset));};actions.Children.Add(save);
            var copy=LocalizedUi.Button(()=>(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeG")));copy.Name="CopyGeneratedImage";copy.Click+=delegate{Run(()=>CopyOriginal(pet.ImageStore,asset));};actions.Children.Add(copy);
            var folder=LocalizedUi.Button(()=>(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeH")));folder.Click+=delegate{Run(()=>Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+pet.ImageStore.PathFor(asset.Id)+"\""){UseShellExecute=true}));};actions.Children.Add(folder);
            if(regenerate!=null){var again=LocalizedUi.Button(()=>(TextResources.Get("generatedImageCard.generatedImageCard.text.alternativeI")));again.Name="RegenerateImage";again.Click+=delegate{regenerate(asset.Copy());};actions.Children.Add(again);}body.Children.Add(actions);
            var detail=Ui.Text(TextResources.Format("generatedImageCard.generatedImageCard.template",asset.Prompt,asset.Model,asset.Width,asset.Height,asset.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),12,Ui.Muted);body.Children.Add(LocalizedUi.With(new Expander{Header=TextResources.Get("generatedImageCard.generatedImageCard.header"),Foreground=Ui.Ink,Content=detail,Margin=new Thickness(0,6,0,0)},"Header",()=>(TextResources.Get("generatedImageCard.generatedImageCard.header"))));
            notice=Ui.Text("",11,Ui.Muted);body.Children.Add(notice);
        }
        void Run(Action action){try{action();notice.Text="";}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException||error is FormatException||error is System.Runtime.InteropServices.COMException||error is System.ComponentModel.Win32Exception))throw;LocalizedUi.Set(notice,"Text",()=>(TextResources.Get("generatedImageCard.run.notice")));}}
        void Preview(){Run(()=>ChatImagePreview.OpenGenerated(host,pet.ImageStore,asset));}
        internal static void SaveOriginal(Window host,GeneratedImageStore store,GeneratedImage image)
        {
            string extension=image.MediaType=="image/jpeg"?".jpg":image.MediaType=="image/webp"?".webp":image.MediaType=="image/gif"?".gif":".png";
            var picker=LocalizedUi.With(new Microsoft.Win32.SaveFileDialog{Title=TextResources.Get("generatedImageCard.saveOriginal.title"),FileName="DeskPet-"+image.CreatedUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss")+extension,Filter=TextResources.Format("generatedImageCard.saveOriginal.filterExtensionExtension",extension,extension),AddExtension=true,DefaultExt=extension},"Title",()=>(TextResources.Get("generatedImageCard.saveOriginal.title")));
            if(picker.ShowDialog(host)==true)File.WriteAllBytes(picker.FileName,store.Read(image.Id));
        }
        internal static void CopyOriginal(GeneratedImageStore store,GeneratedImage image)
        {
            var source=new BitmapImage();using(var stream=new MemoryStream(store.Read(image.Id))){source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.StreamSource=stream;source.EndInit();source.Freeze();}Clipboard.SetImage(source);
        }
    }
    internal sealed partial class ChatMessageView
    {
        readonly StackPanel generatedImagesHost=new StackPanel{Name="GeneratedImages"};GeneratedImageGroupCard imageGroup;
        void RefreshGeneratedImages(ChatMessage message)
        {
            if(imageGroup==null){imageGroup=new GeneratedImageGroupCard(pet,host);generatedImagesHost.Children.Add(imageGroup);}imageGroup.Update(message);
        }
    }
}