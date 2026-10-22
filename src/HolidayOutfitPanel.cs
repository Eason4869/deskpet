using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    public sealed class HolidayOutfitPanel:StackPanel,IDisposable
    {
        readonly HolidayController controller;
        readonly Func<SkinDefinition> currentSkin;
        readonly Dictionary<string,BitmapSource> thumbnails=new Dictionary<string,BitmapSource>();
        SkinDefinition thumbnailSkin;
        readonly CheckBox enabled;
        readonly ComboBox outfits;
        readonly TextBlock status;
        bool refreshing,disposed;
        sealed class Choice
        {public string Id {get;set;} public bool Available {get;set;} public string Text {get;set;} public BitmapSource Thumbnail {get;set;} public override string ToString(){return Text;}}
        public HolidayOutfitPanel(HolidayController holidays,Func<SkinDefinition> character=null)
        {
            if(holidays==null)throw new ArgumentNullException("holidays");controller=holidays;currentSkin=character;Name="HolidayOutfitPanel";
            outfits=new ComboBox{Name="HolidayOutfitPicker",MinHeight=36,Padding=new Thickness(8,4,8,4),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Top,ItemTemplate=ThumbnailTemplate()};
            System.Windows.Automation.AutomationProperties.SetName(outfits,"衣柜");outfits.ToolTip="更换当前伙伴已解锁的服饰";
            outfits.SelectionChanged+=delegate {if(refreshing)return;var selected=outfits.SelectedItem as Choice;if(selected==null)return;if(!selected.Available){RefreshState();return;}controller.SelectOwnedOutfit(selected.Id);};Children.Add(outfits);
            // Keep the host's exact themed item style, including its disabled state.
            outfits.ItemContainerGenerator.StatusChanged+=delegate {foreach(Choice choice in outfits.Items){var container=outfits.ItemContainerGenerator.ContainerFromItem(choice) as ComboBoxItem;if(container!=null)container.IsEnabled=choice.Available;}};
            enabled=Ui.Toggle("启用特殊服饰",true,value=>{if(!refreshing)controller.SetEnabled(value);});enabled.Name="HolidayOutfitEnabled";enabled.FontSize=11;enabled.Foreground=Ui.Muted;enabled.Margin=new Thickness(0,3,0,0);Children.Add(enabled);
            status=Ui.Text("",11,Ui.Muted);status.TextWrapping=TextWrapping.Wrap;Children.Add(status);
            controller.Changed+=Changed;RefreshState();
        }
        void Changed(object sender,EventArgs args)
        {if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshState));return;}RefreshState();}
        public void RefreshState()
        {
            if(disposed)return;refreshing=true;
            try {
                Visibility=controller.WardrobeVisible?Visibility.Visible:Visibility.Collapsed;
                enabled.IsChecked=controller.Enabled;
                var skin=currentSkin==null?null:currentSkin();if(!Object.ReferenceEquals(skin,thumbnailSkin)){thumbnailSkin=skin;thumbnails.Clear();}
                enabled.IsEnabled=skin==null || !String.IsNullOrEmpty(skin.BuiltinCharacterId);
                var owned=controller.OwnedOutfits;
                outfits.Items.Clear();
                var original=OriginalThumbnail();outfits.Items.Add(new Choice{Id=null,Available=true,Text="默认",Thumbnail=original});
                foreach(var outfit in owned)outfits.Items.Add(new Choice{Id=outfit.OutfitId,Available=outfit.Ready,Text=outfit.Name+(outfit.Ready?"":"（暂不可用）"),Thumbnail=OutfitThumbnail(outfit)??original});
                var effective=controller.EffectiveOutfit;string selectedId=effective==null?null:effective.OutfitId;
                outfits.SelectedItem=outfits.Items.Cast<Choice>().FirstOrDefault(choice=>choice.Id==selectedId);
                outfits.IsEnabled=owned.Count==0 || controller.Enabled;
                status.Text=!String.IsNullOrEmpty(controller.PersistenceWarning)?"收藏尚未保存，将在之后重试。":effective!=null && !owned.Any(o=>o.OutfitId==effective.OutfitId)?"当前体验尚未解锁":"";
                status.Visibility=status.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
                if(!controller.Enabled)outfits.SelectedItem=outfits.Items.Cast<Choice>().FirstOrDefault(choice=>choice.Id==controller.PreferredOutfitId);
            }finally{refreshing=false;}
        }
        BitmapSource OriginalThumbnail()
        {if(thumbnailSkin==null)return null;BitmapSource image;if(thumbnails.TryGetValue("default",out image))return image;try{image=SkinCatalog.GetAvatar(thumbnailSkin);thumbnails["default"]=image;return image;}catch(Exception error){if(!ThumbnailError(error))throw;return null;}}
        BitmapSource OutfitThumbnail(OutfitDefinition outfit)
        {
            if(String.IsNullOrEmpty(outfit.Preview))return null;string key=outfit.OutfitId+":"+outfit.Version+":"+outfit.SourceKey;BitmapSource cached;if(thumbnails.TryGetValue(key,out cached))return cached;
            try {using(var input=outfit.OpenPreview()) {if(input==null)return null;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=56;image.StreamSource=input;image.EndInit();image.Freeze();if(thumbnails.Count>=12)thumbnails.Clear();thumbnails[key]=image;return image;}}
            catch(Exception error){if(!ThumbnailError(error))throw;return null;}
        }
        static bool ThumbnailError(Exception error){return error is IOException || error is FileFormatException || error is InvalidDataException || error is InvalidOperationException || error is NotSupportedException || error is ArgumentException;}
        static DataTemplate ThumbnailTemplate()
        {
            var row=new FrameworkElementFactory(typeof(DockPanel));row.SetValue(DockPanel.LastChildFillProperty,true);
            var image=new FrameworkElementFactory(typeof(Image));image.SetValue(FrameworkElement.WidthProperty,28.0);image.SetValue(FrameworkElement.HeightProperty,28.0);image.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,9,0));image.SetValue(Image.StretchProperty,Stretch.Uniform);image.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Thumbnail"));row.AppendChild(image);
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Text"));row.AppendChild(text);return new DataTemplate{VisualTree=row};
        }
        public void Dispose(){if(disposed)return;disposed=true;controller.Changed-=Changed;}
    }
}
