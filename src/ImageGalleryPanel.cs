using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class ImageGalleryPanel:Panel
    {
        internal string Mode="masonry";
        ImageGalleryLayoutResult Layout(double width){return ImageGalleryLayout.Calculate(Children.OfType<ImageGalleryTile>().Select(t=>t.Asset).ToList(),width,Mode);}
        protected override Size MeasureOverride(Size available)
        {
            double width=Double.IsInfinity(available.Width)?Math.Max(1,ActualWidth):Math.Max(1,available.Width);var layout=Layout(width);for(int i=0;i<Children.Count;i++){var p=layout.Items[i];Children[i].Measure(new Size(p.Width,p.Height));}return new Size(width,layout.Height);
        }
        protected override Size ArrangeOverride(Size final)
        {
            var layout=Layout(final.Width);for(int i=0;i<Children.Count;i++){var p=layout.Items[i];Children[i].Arrange(new Rect(p.X,p.Y,p.Width,p.Height));}return new Size(final.Width,layout.Height);
        }
    }
    internal sealed class ImageGalleryTile:Border
    {
        internal readonly GeneratedImage Asset;readonly PetWindow pet;readonly Action<GeneratedImage> regenerate;readonly Image image;
        internal ImageGalleryTile(PetWindow owner,GeneratedImage asset,string mode,Action<GeneratedImage> again)
        {
            pet=owner;Asset=asset.Copy();regenerate=again;Name="ImageGalleryTile";Tag=Asset.Id;CornerRadius=new CornerRadius(14);Background=ThemeManager.Brush("#EDF0F4","#292D34");ClipToBounds=true;Focusable=true;Cursor=System.Windows.Input.Cursors.Hand;ToolTip=Asset.Prompt;System.Windows.Automation.AutomationProperties.SetName(this,"查看图片详情："+Asset.Prompt);
            image=new Image{Name="GalleryThumbnail",Stretch=mode=="grid"?Stretch.UniformToFill:Stretch.Uniform};Child=image;
            try{int width,height;bool limited;image.Source=ChatImagePreview.DecodeGeneratedImage(pet.ImageStore.Read(Asset.Id),600,out width,out height,out limited);}
            catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is ArgumentException||error is FormatException||error is NotSupportedException))throw;Child=Ui.Text("原图暂不可预览\n点击查看详情",12,Ui.Muted);}
            SizeChanged+=delegate{Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight)),14,14);};MouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){OpenDetails();e.Handled=true;};KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Space){OpenDetails();e.Handled=true;}};
            var menu=ChatUi.Menu();var detail=new MenuItem{Header="查看详情"};detail.Click+=delegate{OpenDetails();};menu.Items.Add(detail);var save=new MenuItem{Header="另存为原图…"};save.Click+=delegate{try{GeneratedImageCard.SaveOriginal(Window.GetWindow(this),pet.ImageStore,Asset);}catch(Exception){OpenDetails();}};menu.Items.Add(save);var copy=new MenuItem{Header="复制图片"};copy.Click+=delegate{try{GeneratedImageCard.CopyOriginal(pet.ImageStore,Asset);}catch(Exception){OpenDetails();}};menu.Items.Add(copy);ContextMenu=menu;
        }
        internal void OpenDetails(){ChatImagePreview.OpenGenerated(Window.GetWindow(this),pet.ImageStore,Asset,true,regenerate);}
    }
}
