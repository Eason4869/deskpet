using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu
{

    // Hosted independently of the relationship-ledger result. Only this page owns decoded cards.
    internal sealed class HolidayKeepsakeAlbumView : StackPanel, IDisposable
    {
        readonly HolidayKeepsakeRuntime runtime;
        readonly string runtimeDataDirectory;
        readonly Action<string> openFolder;
        readonly HolidayKeepsakeAlbumModel model;
        readonly Func<HolidayCatalog> catalog;
        readonly Func<Task> retryEncounter;
        readonly Func<bool> sameOwner;
        WrapPanel cards;readonly List<WrapPanel> cardAreas=new List<WrapPanel>();
        readonly HashSet<string> expandedCards=new HashSet<string>();
        readonly Dictionary<string,HolidayCardCapture> captures=new Dictionary<string,HolidayCardCapture>();
        string identity,character,companionName;string selectedHoliday;
        int page,revision; bool disposed;
        HolidayKeepsakePage loaded;
        HolidayKeepsakeReadStatus readStatus;
        TextBlock folderStatus;
        public Task Pending {get;private set;}
        internal int SavedCount {get{return loaded==null?0:loaded.Album.Records.Count;}}
        internal int DecodedCount {get{return captures.Count;}}
        internal HolidayKeepsakeAlbumView(HolidayKeepsakeRuntime runtime,Func<HolidayCatalog> catalog,Func<Task> retryEncounter,Func<bool> sameOwner,string dataDirectory,Action<string> openFolder=null)
        {runtimeDataDirectory=dataDirectory;this.openFolder=openFolder??(path=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true}));this.runtime=runtime;model=new HolidayKeepsakeAlbumModel(runtime);this.catalog=catalog;this.retryEncounter=retryEncounter;this.sameOwner=sameOwner;Pending=Task.FromResult(true);Name="HolidayKeepsakeAlbum";runtime.Changed+=Changed;SizeChanged+=delegate{ResizeCards();};}
        async void Changed(string id){if(!disposed&&id==identity)await RefreshAsync();}
        internal Task SetIdentityAsync(string id,string verified,string name)
        {
            if(identity!=id){revision++;page=0;loaded=null;ClearCaptures();expandedCards.Clear();identity=id;}
            character=verified;companionName=name;return RefreshAsync();
        }
        internal Task RefreshAsync(){return Pending=LoadAsync();}
        async Task LoadAsync()
        {
            if(disposed||identity==null)return;int version=++revision;string id=identity;
            var result=await model.LoadAsync(id,page);
            if(!Current(version,id))return;
            readStatus=result.Album.Status;
            if(readStatus==HolidayKeepsakeReadStatus.Available)loaded=result;
            else if(loaded!=null)result=loaded;
            page=result.Page;
            var definitions=HolidayKeepsakeRegistry.BuiltIn.Definitions.Where(x=>x.EnabledForAcquisition).ToArray();
            if(!definitions.Any(x=>x.HolidayId==selectedHoliday)){var available=definitions.FirstOrDefault(d=>catalog().Outfits.Any(o=>o.Ready&&o.CharacterId==character&&o.HolidayId==d.HolidayId));selectedHoliday=available!=null?available.HolidayId:definitions.Length==0?null:definitions[0].HolidayId;}
            var now=RelationshipTime.Current();int year=now.UtcNow.Add(now.LocalOffset).Year;
            HolidayKeepsakePreviewResult preview=null;
            if(readStatus==HolidayKeepsakeReadStatus.Available&&selectedHoliday!=null&&!result.Album.Records.Any(x=>x.HolidayId==selectedHoliday&&x.OccurrenceYear==year))
                preview=await runtime.PreviewAsync(id,character,companionName,selectedHoliday,year,catalog());
            if(!Current(version,id))return;
            Render(result,preview,year,version,id,definitions);
        }
        bool Current(int version,string id){return !disposed&&version==revision&&id==identity&&sameOwner();}
        void Render(HolidayKeepsakePage result,HolidayKeepsakePreviewResult preview,int year,int version,string id,HolidayKeepsakeDefinition[] definitions)
        {
            var focused=System.Windows.Input.Keyboard.FocusedElement as FrameworkElement;var focus=HomeVisual.CaptureFocus(this,focused);
            foreach(var old in captures.Values){if(old.Expanded)expandedCards.Add(old.Data.Identity);else expandedCards.Remove(old.Data.Identity);}
            var wanted=new HashSet<string>(result.Scenes.Where(x=>x.Status==HolidayKeepsakeReadStatus.Available&&x.Record!=null).Select(x=>HolidayKeepsakeCardData.Owned(x.Record,HolidayKeepsakeRegistry.BuiltIn).CaptureKey(ThemeManager.IsDark)));
            if(preview!=null&&preview.Availability==HolidayKeepsakeAvailability.Available)wanted.Add(HolidayKeepsakeCardData.Preview(id,companionName,year,preview,HolidayKeepsakeRegistry.BuiltIn).CaptureKey(ThemeManager.IsDark));
            var keep=new HashSet<string>();Children.Clear();cardAreas.Clear();
            foreach(var key in captures.Keys.Where(x=>!wanted.Contains(x)).ToArray()){captures[key].Dispose();captures.Remove(key);}
            var title=Text(()=>(TextResources.Get("holidayKeepsake.albumTitle")),18);Children.Add(title);
            if(readStatus==HolidayKeepsakeReadStatus.Available)Children.Add(Text(()=>(TextResources.Format("holidayKeepsake.savedCount",result.Album.Records.Count)),13));
            folderStatus=null;
            if(readStatus!=HolidayKeepsakeReadStatus.Available){Children.Add(Text(()=>(TextResources.Get("holidayKeepsake.readError")),13));Children.Add(Button(()=>(TextResources.Get("relationship.home.openDataFolder")),OpenDataFolder));folderStatus=Text("",13);folderStatus.Name="HolidayFolderStatus";Children.Add(folderStatus);}
            else if(result.Album.Records.Count==0)Children.Add(Text(()=>(TextResources.Get("holidayKeepsake.empty")),13));
            cards=new WrapPanel();cardAreas.Add(cards);Children.Add(cards);
            foreach(var item in result.Scenes){
                if(item.Record==null)continue;
                var data=HolidayKeepsakeCardData.Owned(item.Record,HolidayKeepsakeRegistry.BuiltIn);
                if(item.Status!=HolidayKeepsakeReadStatus.Available){var details=Text(data.Title+" · "+data.YearText+" · "+data.Name+" · "+data.DateText,16);details.Name="HolidayCardErrorMetadata";details.Tag=data.Identity;Children.Add(details);Children.Add(Text(()=>(TextResources.Get("holidayKeepsake.sceneError")),13));var unavailableSave=Button(()=>(TextResources.Get("relationship.card.save")),delegate{});unavailableSave.Name="SaveHolidayCard";unavailableSave.IsEnabled=false;Children.Add(unavailableSave);continue;}
                AddCard(data,item.CopyPngBytes(),keep,version,id);
            }
            if(result.PageCount>1){
                var pager=new StackPanel{Orientation=Orientation.Horizontal};
                var back=Button(()=>(TextResources.Get("holidayKeepsake.previous")),async delegate{page--;await RefreshAsync();});back.IsEnabled=page>0;back.Name="HolidayPreviousPage";
                var next=Button(()=>(TextResources.Get("holidayKeepsake.next")),async delegate{page++;await RefreshAsync();});next.IsEnabled=page+1<result.PageCount;next.Name="HolidayNextPage";
                pager.Children.Add(back);pager.Children.Add(Text(()=>(TextResources.Format("holidayKeepsake.pageNumber",page+1,result.PageCount)),13));pager.Children.Add(next);Children.Add(pager);
            }
            if(definitions.Length>1){
                var items=definitions.Select(x=>new ComboBoxItem{Tag=x.HolidayId,Content=Text(()=>HolidayKeepsakeCardData.Owned(new HolidayKeepsakeRecord{HolidayId=x.HolidayId},HolidayKeepsakeRegistry.BuiltIn).Title,13)}).ToArray();
                var choice=new ComboBox{Name="HolidayPreviewChoice",ItemsSource=items,SelectedItem=items.FirstOrDefault(x=>(string)x.Tag==selectedHoliday)};
                choice.SelectionChanged+=async delegate{if(choice.SelectedItem is ComboBoxItem){selectedHoliday=(string)((ComboBoxItem)choice.SelectedItem).Tag;await RefreshAsync();}};
                Children.Add(choice);
            }
            if(preview!=null){
                Children.Add(Text(()=>(TextResources.Get("holidayKeepsake.locked")),13));
                cards=new WrapPanel();cardAreas.Add(cards);Children.Add(cards);
                if(preview.Availability==HolidayKeepsakeAvailability.Available)AddCard(HolidayKeepsakeCardData.Preview(id,companionName,year,preview,HolidayKeepsakeRegistry.BuiltIn),preview.CopyPngBytes(),keep,version,id);
                else Children.Add(Text(()=>(TextResources.Get("holidayKeepsake.unavailable")),13));
            }
            var retry=Button(()=>(TextResources.Get("holidayKeepsake.retry")),async delegate{await retryEncounter();if(!disposed)await RefreshAsync();});retry.Name="HolidayRetry";Children.Add(retry);
            foreach(var key in captures.Keys.Where(x=>!keep.Contains(x)).ToArray()){captures[key].Dispose();captures.Remove(key);}
            if(focus!=null){var replacement=HomeVisual.ResolveFocus(this,focus);if(replacement!=null)replacement.Focus();}
        }
        void AddCard(HolidayKeepsakeCardData data,byte[] scene,HashSet<string> keep,int version,string id)
        {
            string key=data.CaptureKey(ThemeManager.IsDark);keep.Add(key);HolidayCardCapture capture;
            if(!captures.TryGetValue(key,out capture)){capture=HolidayCardCapture.Create(data,scene,ThemeManager.IsDark);capture.Expanded=expandedCards.Contains(data.Identity);captures.Add(key,capture);}
            var card=new HolidayCardPreview(capture,delegate{return Current(version,id)&&captures.ContainsKey(key)&&IsVisible;},null,ResizeCards);cards.Children.Add(card);ResizeCards();
        }
        void ResizeCards(){if(cards==null)return;double available=ActualWidth;if(available<=0)available=520;double width=available>=820?(available-22)/2:available;foreach(var area in cardAreas)foreach(var card in area.Children.OfType<HolidayCardPreview>())card.Width=Math.Max(120,(card.Capture.Expanded?available:width)-18);}
        void OpenDataFolder(){try{openFolder(runtimeDataDirectory);if(folderStatus!=null)folderStatus.Text="";}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is System.ComponentModel.Win32Exception||error is InvalidOperationException))throw;if(folderStatus!=null)LocalizedUi.Set(folderStatus,"Text",()=>(TextResources.Get("relationship.home.folderFailed")));}}
        void ClearCaptures(){foreach(var capture in captures.Values)capture.Dispose();captures.Clear();Children.Clear();}
        public void Dispose(){if(disposed)return;disposed=true;revision++;runtime.Changed-=Changed;ClearCaptures();loaded=null;expandedCards.Clear();cardAreas.Clear();}
        static TextBlock Text(string value,double size){return Ui.Text(value,size,AgentUi.Ink);}
        static Button Button(string label,Action action){var button=Ui.Button(label);button.Click+=delegate{action();};return button;}
static TextBlock Text(System.Func<string> value,double size){return LocalizedUi.With(Text(value(),size),"Text",value);}
        static Button Button(System.Func<string> label,Action action){return LocalizedUi.With(Button(label(),action),"Content",label);}    }

    internal sealed class HolidayCardCapture:IDisposable
    {
        internal HolidayKeepsakeCardData Data;internal byte[] Png;internal BitmapSource Image;internal bool Expanded;
        internal static HolidayCardCapture Create(HolidayKeepsakeCardData data,byte[] scene,bool dark)
        {
            var image=Decode(scene);
            byte[] png=RelationshipCardRenderer.RenderHoliday(data,image,dark);
            return new HolidayCardCapture{Data=data,Png=png,Image=Decode(png)};
        }
        internal static BitmapSource Decode(byte[] png){using(var stream=new MemoryStream(png,false)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=800;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}}
        public void Dispose(){Image=null;Png=null;}
    }
    internal sealed class HolidayCardPreview:StackPanel
    {
        internal Task PendingSave=Task.FromResult(true);
        internal readonly HolidayCardCapture Capture;
        internal HolidayCardPreview(HolidayCardCapture capture,Func<bool> current,Func<Task<string>> chooseDestination=null,Action resized=null)
        {
            Capture=capture;
            Name="HolidayCardPreview";Margin=new Thickness(0,12,18,20);Tag=capture.Data.Identity;
            System.Windows.Automation.AutomationProperties.SetAutomationId(this,"HolidayKeepsake:"+capture.Data.Identity);System.Windows.Automation.AutomationProperties.SetName(this,capture.Data.Name+" "+capture.Data.Title+" "+capture.Data.YearText+" "+capture.Data.DateText);
            var image=new Image{Name="HolidayCardImage",Source=capture.Image,Stretch=Stretch.Uniform,MaxWidth=520,HorizontalAlignment=HorizontalAlignment.Left};
            var expanded=new Image{Source=capture.Image,Stretch=Stretch.Uniform,MaxWidth=800};
            var disclosure=new Expander{Name="HolidayCardExpanded",Header=capture.Data.Title+" · "+capture.Data.YearText,Content=expanded,IsExpanded=capture.Expanded,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            disclosure.Expanded+=delegate{capture.Expanded=true;if(resized!=null)resized();};disclosure.Collapsed+=delegate{capture.Expanded=false;if(resized!=null)resized();};
            Children.Add(image);Children.Add(disclosure);
            if(capture.Data.Record==null)return;
            var status=Ui.Text("",12,AgentUi.Muted);status.Name="HolidayCardStatus";
            var save=LocalizedUi.Button(()=>(TextResources.Get("relationship.card.save")));save.Name="SaveHolidayCard";save.HorizontalAlignment=HorizontalAlignment.Left;
            save.Click+=delegate{PendingSave=SaveAsync(capture,current,chooseDestination,save,status);};
            Children.Add(save);Children.Add(status);
        }
        async Task SaveAsync(HolidayCardCapture capture,Func<bool> current,Func<Task<string>> chooseDestination,Button save,TextBlock status)
        {
            if(!current()||!save.IsEnabled)return;save.IsEnabled=false;status.Text="";
            try{
                string path=await (chooseDestination==null?PickAsync(capture.Data.FileName):chooseDestination());
                if(!current())return;
                if(String.IsNullOrEmpty(path)){LocalizedUi.Set(status,"Text",()=>(TextResources.Get("relationship.home.saveCancelled")));return;}
                var result=await RelationshipCardFile.SaveAsync(path,capture.Png);
                if(current())LocalizedUi.Set(status,"Text",()=>((result==RelationshipCardSaveResult.Saved?TextResources.Get("relationship.card.saved"):TextResources.Get("relationship.card.failed"))));
            }catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException||error is OperationCanceledException||error is System.Security.SecurityException))throw;if(current())LocalizedUi.Set(status,"Text",()=>(TextResources.Get("relationship.card.failed")));}
            finally{if(current())save.IsEnabled=true;}
        }
        Task<string> PickAsync(string fileName)
        {
            var picker=LocalizedUi.With(new Microsoft.Win32.SaveFileDialog{Title=TextResources.Get("relationship.card.save"),Filter=TextResources.Get("relationship.card.pngFilter")+"|*.png",DefaultExt=".png",AddExtension=true,OverwritePrompt=true,FileName=fileName},"Title",()=>(TextResources.Get("relationship.card.save")));
            return Task.FromResult(picker.ShowDialog(Window.GetWindow(this))==true?picker.FileName:null);
        }
    }
}
