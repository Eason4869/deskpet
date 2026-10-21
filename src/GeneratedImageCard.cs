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
            catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is NotSupportedException||e is FormatException))throw;body.Children.Add(Ui.Text("原图无法预览，生成记录仍保留；可以另存原图后查看。",12,Ui.Muted));}
            var actions=new WrapPanel{Margin=new Thickness(0,8,0,0)};var preview=Ui.Button("预览");preview.Name="PreviewGeneratedImage";preview.Click+=delegate{Preview();};actions.Children.Add(preview);
            var save=Ui.Button("另存为…");save.Name="SaveGeneratedImage";save.Click+=delegate{Run(()=>SaveOriginal(host,pet.ImageStore,asset));};actions.Children.Add(save);
            var copy=Ui.Button("复制图片");copy.Name="CopyGeneratedImage";copy.Click+=delegate{Run(()=>CopyOriginal(pet.ImageStore,asset));};actions.Children.Add(copy);
            var folder=Ui.Button("打开所在文件夹");folder.Click+=delegate{Run(()=>Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+pet.ImageStore.PathFor(asset.Id)+"\""){UseShellExecute=true}));};actions.Children.Add(folder);
            if(regenerate!=null){var again=Ui.Button("再生成一张");again.Name="RegenerateImage";again.Click+=delegate{regenerate(asset.Copy());};actions.Children.Add(again);}body.Children.Add(actions);
            var detail=Ui.Text(asset.Prompt+"\n模型："+asset.Model+"\n尺寸："+asset.Width+" × "+asset.Height+"\n生成时间："+asset.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),12,Ui.Muted);body.Children.Add(new Expander{Header="图片详情",Content=detail,Margin=new Thickness(0,6,0,0)});
            notice=Ui.Text("",11,Ui.Muted);body.Children.Add(notice);
        }
        void Run(Action action){try{action();notice.Text="";}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException||error is FormatException||error is System.Runtime.InteropServices.COMException||error is System.ComponentModel.Win32Exception))throw;notice.Text="操作未完成，请检查原图、保存位置或剪贴板后重试。";}}
        void Preview(){Run(()=>ChatImagePreview.OpenGenerated(host,pet.ImageStore,asset));}
        internal static void SaveOriginal(Window host,GeneratedImageStore store,GeneratedImage image)
        {
            string extension=image.MediaType=="image/jpeg"?".jpg":image.MediaType=="image/webp"?".webp":image.MediaType=="image/gif"?".gif":".png";
            var picker=new Microsoft.Win32.SaveFileDialog{Title="保存原图",FileName="DeskPet-"+image.CreatedUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss")+extension,Filter="原始图片 (*"+extension+")|*"+extension,AddExtension=true,DefaultExt=extension};
            if(picker.ShowDialog(host)==true)File.WriteAllBytes(picker.FileName,store.Read(image.Id));
        }
        internal static void CopyOriginal(GeneratedImageStore store,GeneratedImage image)
        {
            var source=new BitmapImage();using(var stream=new MemoryStream(store.Read(image.Id))){source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.StreamSource=stream;source.EndInit();source.Freeze();}Clipboard.SetImage(source);
        }
    }
    internal sealed partial class ChatMessageView
    {
        readonly StackPanel generatedImagesHost=new StackPanel{Name="GeneratedImages"};string displayedImageIds;
        void RefreshGeneratedImages(ChatMessage message)
        {
            string ids=String.Join("|",message.ImageGenerationTaskIds??new System.Collections.Generic.List<string>())+String.Join("|",(message.GeneratedImages??new System.Collections.Generic.List<GeneratedImage>()).ConvertAll(i=>i.Id));if(ids==displayedImageIds)return;displayedImageIds=ids;generatedImagesHost.Children.Clear();
            foreach(var taskId in message.ImageGenerationTaskIds??new System.Collections.Generic.List<string>()) { var task=pet.ImageTasks.Get(taskId);if(task!=null&&!(message.GeneratedImages??new System.Collections.Generic.List<GeneratedImage>()).Exists(i=>i.ImageJobId==taskId||i.Id==task.ResultImageId))generatedImagesHost.Children.Add(ImageGenerationPendingCard.Create(pet,host,taskId,selected=>{var chat=host as ChatWindow;if(chat!=null)chat.ShowImagesPage(selected);else pet.OpenImageWorkspace(selected);})); }
            foreach(var image in message.GeneratedImages??new System.Collections.Generic.List<GeneratedImage>())generatedImagesHost.Children.Add(new GeneratedImageCard(pet,host,image,selected=>{var chat=host as ChatWindow;if(chat!=null)chat.ShowImagesPage(selected);else pet.OpenImageWorkspace(selected);}));
        }
    }
}
