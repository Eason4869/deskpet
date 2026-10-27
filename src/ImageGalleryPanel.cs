using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
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
    internal sealed class ImageGalleryTile:Border,IDisposable
    {
        static readonly SemaphoreSlim decoderGate=new SemaphoreSlim(3);
        internal readonly GeneratedImage Asset;readonly PetWindow pet;readonly Action<GeneratedImage> regenerate;readonly Image image;bool disposed;Button selectOverlay,favoriteOverlay;bool selectionActive,selectedAsset;
        internal ImageGalleryTile(PetWindow owner,GeneratedImage asset,string mode,Action<GeneratedImage> again,Action<string> choose,Action<string> favorite,bool selected,bool selecting)
        {
            pet=owner;Asset=asset.Copy();regenerate=again;Name="ImageGalleryTile";Tag=Asset.Id;CornerRadius=new CornerRadius(14);Background=ThemeManager.Brush("#EDF0F4","#292D34");ClipToBounds=true;Focusable=true;Cursor=System.Windows.Input.Cursors.Hand;ToolTip=Asset.Prompt;LocalizedUi.Automation(this,()=>(TextResources.Format("imageGalleryPanel.imageGalleryTile.automationAutomationPropertiesSetName",Asset.Prompt)));
            image=new Image{Name="GalleryThumbnail",Stretch=mode=="grid"?Stretch.UniformToFill:Stretch.Uniform};var surface=new Grid();surface.Children.Add(image);Child=surface;
            var select=Ui.Button("√");select.Name="GalleryTileSelect";LocalizedUi.Set(select,"ToolTip",()=>(TextResources.Get("imageGalleryPanel.imageGalleryTile.selectToolTip")));select.HorizontalAlignment=HorizontalAlignment.Left;select.VerticalAlignment=VerticalAlignment.Top;select.Width=select.Height=32;select.MinHeight=0;select.Padding=new Thickness(0);select.HorizontalContentAlignment=HorizontalAlignment.Center;select.VerticalContentAlignment=VerticalAlignment.Center;select.Margin=new Thickness(8);select.Click+=delegate(object sender,RoutedEventArgs e){choose(Asset.Id);e.Handled=true;};surface.Children.Add(select);
            var star=Ui.Button("");star.Name="GalleryTileFavorite";LocalizedUi.Set(star,"ToolTip",()=>(Asset.IsFavorite?TextResources.Get("imageGalleryPanel.imageGalleryTile.toolTipAssetIsFavorite"):TextResources.Get("imageGalleryPanel.imageGalleryTile.toolTipAssetIsFavorite.alternativeA")));star.HorizontalAlignment=HorizontalAlignment.Right;star.VerticalAlignment=VerticalAlignment.Top;star.Width=star.Height=32;star.MinHeight=0;star.Padding=new Thickness(0);star.HorizontalContentAlignment=HorizontalAlignment.Center;star.VerticalContentAlignment=VerticalAlignment.Center;star.Margin=new Thickness(8);star.Click+=delegate(object sender,RoutedEventArgs e){favorite(Asset.Id);e.Handled=true;};surface.Children.Add(star);
            selectOverlay=select;favoriteOverlay=star;star.Foreground=Asset.IsFavorite?ThemeManager.Brush("#D64550","#FF828B"):Ui.Muted;star.Content=new System.Windows.Shapes.Path{Name="GalleryFavoriteHeart",Data=Geometry.Parse(ImageGalleryLayout.FavoriteHeartPath),Width=16,Height=16,Stretch=Stretch.Uniform,Stroke=star.Foreground,StrokeThickness=1.5,Fill=Asset.IsFavorite?star.Foreground:Brushes.Transparent,IsHitTestVisible=false};LocalizedUi.Automation(star,()=>(Asset.IsFavorite?TextResources.Get("imageGalleryPanel.imageGalleryTile.automationAutomationPropertiesSetName.alternativeA"):TextResources.Get("imageGalleryPanel.imageGalleryTile.automationAutomationPropertiesSetName.alternativeB")));UpdateSelection(selected,selecting);
            Action show=delegate{select.Opacity=star.Opacity=1;};Action hide=delegate{select.Opacity=selectionActive||selectedAsset?1:0;star.Opacity=selectionActive||selectedAsset?1:0;};hide();MouseEnter+=delegate{show();};MouseLeave+=delegate{if(!IsKeyboardFocusWithin)hide();};GotKeyboardFocus+=delegate{show();};LostKeyboardFocus+=delegate{if(!IsMouseOver)hide();};
            LoadAsync();
            SizeChanged+=delegate{Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight)),14,14);};MouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){var source=e.OriginalSource as DependencyObject;while(source!=null&&source!=this){if(source is Button)return;source=VisualTreeHelper.GetParent(source);}OpenDetails();e.Handled=true;};KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.OriginalSource!=this)return;if(e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Space){OpenDetails();e.Handled=true;}};
            var menu=ChatUi.Menu();var detail=LocalizedUi.With(new MenuItem{Header=TextResources.Get("imageGalleryPanel.imageGalleryTile.header")},"Header",()=>(TextResources.Get("imageGalleryPanel.imageGalleryTile.header")));detail.Click+=delegate{OpenDetails();};menu.Items.Add(detail);var save=LocalizedUi.With(new MenuItem{Header=TextResources.Get("imageGalleryPanel.imageGalleryTile.header.alternativeA")},"Header",()=>(TextResources.Get("imageGalleryPanel.imageGalleryTile.header.alternativeA")));save.Click+=delegate{try{GeneratedImageCard.SaveOriginal(Window.GetWindow(this),pet.ImageStore,Asset);}catch(Exception){OpenDetails();}};menu.Items.Add(save);var copy=LocalizedUi.With(new MenuItem{Header=TextResources.Get("imageGalleryPanel.imageGalleryTile.header.alternativeB")},"Header",()=>(TextResources.Get("imageGalleryPanel.imageGalleryTile.header.alternativeB")));copy.Click+=delegate{try{GeneratedImageCard.CopyOriginal(pet.ImageStore,Asset);}catch(Exception){OpenDetails();}};menu.Items.Add(copy);ContextMenu=menu;
        }
        internal void UpdateSelection(bool selected,bool selecting){selectedAsset=selected;selectionActive=selecting;selectOverlay.Content="√";selectOverlay.Background=selected?Ui.Blue:ThemeManager.Surface;selectOverlay.Foreground=selected?System.Windows.Media.Brushes.White:Ui.Muted;LocalizedUi.Automation(selectOverlay,()=>(selected?TextResources.Get("imageGalleryPanel.updateSelection.automationAutomationPropertiesSetName"):TextResources.Get("imageGalleryPanel.updateSelection.automationAutomationPropertiesSetName.alternativeA")));LocalizedUi.Set(selectOverlay,"ToolTip",()=>(selected?TextResources.Get("imageGalleryPanel.updateSelection.selectOverlayToolTipSelected"):TextResources.Get("imageGalleryPanel.updateSelection.selectOverlayToolTipSelected.alternativeA")));selectOverlay.Opacity=favoriteOverlay.Opacity=IsMouseOver||IsKeyboardFocusWithin||selected||selecting?1:0;}
        async void LoadAsync(){bool entered=false;try{await decoderGate.WaitAsync();entered=true;if(disposed)return;var bytes=await Task.Run(()=>{using(pet.ImageUsage.Acquire(Asset.Id,"gallery-thumbnail"))return pet.ImageStore.Read(Asset.Id);});if(disposed)return;var bitmap=await Task.Run(()=>{int width,height;bool limited;return ChatImagePreview.DecodeGeneratedImage(bytes,600,out width,out height,out limited);});if(!disposed)image.Source=bitmap;}
            catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is ArgumentException||error is FormatException||error is NotSupportedException||error is InvalidOperationException))throw;if(!disposed)LocalizedUi.Set(image,"ToolTip",()=>(TextResources.Get("imageGalleryPanel.loadAsync.imageToolTip")));}
            finally{if(entered)decoderGate.Release();}}
        public void Dispose(){disposed=true;image.Source=null;}
        internal void OpenDetails(){ChatImagePreview.OpenGenerated(Window.GetWindow(this),pet.ImageStore,Asset,true,regenerate);}
    }
}
