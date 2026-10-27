using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Dafeiyu
{
    // A private draft per provider keeps unfinished edits out of the running conversation.
    public sealed partial class TextProviderEditor : Grid
    {
        static readonly Style itemStyle=(Style)ThemeManager.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='surface' Background='Transparent' CornerRadius='7' Padding='{TemplateBinding Padding}' BorderThickness='1' BorderBrush='Transparent'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='surface' Property='Background' Value='#EAEAEA'/><Setter TargetName='surface' Property='BorderBrush' Value='Transparent'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#F1F1F1'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        readonly string kind,prefix;
        internal static Style ProviderItemStyle {get{return itemStyle;}}
        readonly List<TextProviderSettings> drafts=new List<TextProviderSettings>();
        readonly ListBox providers,models;
        readonly ContentControl connectionHost=new ContentControl();
        readonly TextBox providerName,newModel,modelDisplayName,search;
        TextBox contextCapacity;TextBlock contextDetected;
        readonly TextBlock modelCount,emptyModels;
        readonly Button refreshModels;
        readonly ProgressBar loading;
        readonly ListBox discovered;
        readonly StackPanel discoveryPanel;
        readonly HashSet<string> emptyInitialIds=new HashSet<string>();
        readonly HashSet<string> unsavedProviders=new HashSet<string>();
        readonly Dictionary<string,IList<string>> catalogs=new Dictionary<string,IList<string>>();
        readonly Dictionary<string,ConnectionSettings> catalogConnections=new Dictionary<string,ConnectionSettings>();
        public Func<bool> SaveConnection {get;set;}
        public readonly StackPanel Options=new StackPanel();
        public readonly StackPanel Extras=new StackPanel();


        string N(string name){return kind=="text"?name:name.Replace("Text",kind=="asr"?"Asr":kind=="speech"?"Speech":"Live");}
        ConnectionSettings CatalogDraft(){return connection.Draft();}
        readonly DispatcherTimer fetchDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};
        readonly Dictionary<string,ConnectionSettings> attempted=new Dictionary<string,ConnectionSettings>();
        CancellationTokenSource fetching;
        string fetchingProviderId;
        TextBlock connectionEndpoint;
        ServiceProviderSettings baseline;
        readonly TextBlock feedback;
        readonly StackPanel modelForm=new StackPanel{Visibility=Visibility.Collapsed};Window modelDialog;FrameworkElement providerDetail;StackPanel providerDetailHost;TextBlock emptyProviderHint;TextBlock modelDialogNotice;
        readonly Button removeProvider;
        ConnectionSettingsPanel.ConnectionEditor connection;
        TextProviderSettings selected;
        bool updating,automaticName;
        public event Action Changed;
        public string SelectedProviderId {get{return selected.Id;}}
        public TextProviderEditor(PetSettings settings,string serviceKind="text")
        {
            kind=serviceKind;prefix=kind=="text"?"TextService":kind=="asr"?"Transcription":kind=="speech"?"Speech":"LiveService";
            Name=N("GuideTextProviderSection");
            settings=settings.ProviderView(kind);settings.EnsureTextProviders();baseline=settings.ProviderSnapshot("text");foreach(var provider in settings.TextProviders){drafts.Add(provider.Copy());if(kind=="text"&&String.IsNullOrWhiteSpace(provider.Connection.BaseUrl)&&String.IsNullOrWhiteSpace(provider.Connection.ApiKey)&&provider.Models.Count==0)emptyInitialIds.Add(provider.Id);}
            ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(138)});
            ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var rail=new StackPanel();var heading=LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text")),15);heading.FontWeight=FontWeights.SemiBold;rail.Children.Add(heading);
            rail.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeA")),10,Ui.Muted));
            providers=new ListBox{Name=N("TextProviderList"),BorderThickness=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(0,12,0,10),MinHeight=76,MaxHeight=340,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            ScrollViewer.SetHorizontalScrollBarVisibility(providers,ScrollBarVisibility.Disabled);rail.Children.Add(providers);
            var add=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeB")));add.Name=N("AddTextProvider");add.FontSize=11;add.Margin=new Thickness(0,0,0,7);add.Padding=new Thickness(6);add.Click+=delegate {AddProvider();};rail.Children.Add(add);
            removeProvider=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeC")));removeProvider.Name=N("RemoveTextProvider");removeProvider.FontSize=11;removeProvider.Margin=new Thickness(0);removeProvider.Padding=new Thickness(6);removeProvider.HorizontalContentAlignment=HorizontalAlignment.Stretch;removeProvider.Content=LocalizedUi.With(new TextBlock{Text=TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeC"),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center},"Text",()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeC")));removeProvider.Click+=delegate {RemoveProvider();};rail.Children.Add(removeProvider);
            rail.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeD")),10,Ui.Muted));
            var railCard=Glass(rail,10);railCard.Margin=new Thickness(0,0,12,0);railCard.VerticalAlignment=VerticalAlignment.Top;Children.Add(railCard);
            var detail=new StackPanel();providerDetailHost=detail;detail.Children.Add(Options);providerName=Ui.Field();providerName.Name=N("TextProviderName");providerName.MaxLength=80;LocalizedUi.Label(detail,()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeE")),providerName);
            connectionHost.Margin=new Thickness(0,14,0,12);detail.Children.Add(connectionHost);
            var modelHeader=new DockPanel{Margin=new Thickness(0,4,0,7)};
            refreshModels=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeF")));refreshModels.Name=N("FetchTextModels");refreshModels.FontSize=11;refreshModels.MinHeight=30;refreshModels.Padding=new Thickness(10,4,10,4);refreshModels.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(refreshModels,Dock.Right);modelHeader.Children.Add(refreshModels);var customModel=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeG")));customModel.Name="CustomTextModel";customModel.MinHeight=30;customModel.Padding=new Thickness(8,4,8,4);customModel.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(customModel,Dock.Right);modelHeader.Children.Add(customModel);customModel.Click+=delegate{OpenModelEditor(null);};
            modelCount=Ui.Text("",11,Ui.Muted);modelCount.Name=N("TextModelCount");modelCount.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(modelCount,Dock.Right);modelHeader.Children.Add(modelCount);
            var modelHeading=LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeH")),16);modelHeading.FontWeight=FontWeights.SemiBold;modelHeading.VerticalAlignment=VerticalAlignment.Center;modelHeader.Children.Add(modelHeading);detail.Children.Add(modelHeader);
            detail.Children.Add(LocalizedUi.Text(()=>(kind=="text"?TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeI"):kind=="live"?TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeJ"):TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeK")+(kind=="asr"?TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeL"):TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeM"))+TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeN")),11,Ui.Muted));
            search=Ui.Field();search.Name=N("TextModelSearch");search.MaxLength=256;LocalizedUi.Set(search,"ToolTip",()=>(TextResources.Get("textProviderEditor.textProviderEditor.searchToolTip")));LocalizedUi.Label(detail,()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeO")),search);
            loading=new ProgressBar{Name=N("TextModelsLoading"),Height=2,IsIndeterminate=true,Foreground=Ui.Blue,Visibility=Visibility.Collapsed,Margin=new Thickness(0,6,0,0)};detail.Children.Add(loading);
            models=new ListBox{Name=N("TextProviderModels"),MinHeight=42,MaxHeight=224,Background=ThemeManager.Surface,BorderThickness=new Thickness(0),Margin=new Thickness(0,8,0,8),SelectedValuePath="Tag",HorizontalContentAlignment=HorizontalAlignment.Stretch};ScrollViewer.SetHorizontalScrollBarVisibility(models,ScrollBarVisibility.Disabled);detail.Children.Add(models);
            emptyModels=Ui.Text("",12,Ui.Muted);emptyModels.Name=N("TextModelsEmpty");emptyModels.Margin=new Thickness(8,18,8,20);detail.Children.Add(emptyModels);
            discoveryPanel=new StackPanel{Margin=new Thickness(0,8,0,14),Visibility=Visibility.Collapsed};
            var discoveryTitle=LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeP")),13);discoveryTitle.FontWeight=FontWeights.SemiBold;discoveryPanel.Children.Add(discoveryTitle);
            discoveryPanel.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeQ")),11,Ui.Muted));
            discovered=new ListBox{Name=N("DiscoveredTextModels"),MaxHeight=190,HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(1),BorderBrush=Ui.Line,Background=ThemeManager.Surface,Margin=new Thickness(0,7,0,7)};ScrollViewer.SetHorizontalScrollBarVisibility(discovered,ScrollBarVisibility.Disabled);discoveryPanel.Children.Add(discovered);
            detail.Children.Add(discoveryPanel);
            LocalizedUi.Label(modelForm,()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeR")),newModel=Ui.Field());newModel.Name=N("TextProviderNewModel");newModel.MaxLength=256;LocalizedUi.Set(newModel,"ToolTip",()=>(TextResources.Get("textProviderEditor.textProviderEditor.newModelToolTip")));
            var modelRow=new Grid();modelRow.ColumnDefinitions.Add(new ColumnDefinition());modelRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            modelDisplayName=Ui.Field();modelDisplayName.Name=N("TextModelDisplayName");modelDisplayName.MaxLength=80;LocalizedUi.Set(modelDisplayName,"ToolTip",()=>(TextResources.Get("textProviderEditor.textProviderEditor.modelDisplayNameToolTip")));modelRow.Children.Add(modelDisplayName);
            var addModel=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeS")));addModel.Name=N("AddTextModel");addModel.Margin=new Thickness(6,0,0,0);addModel.Padding=new Thickness(10,4,10,4);addModel.Click+=delegate {try{ValidateContextCapacity();if(!AddModel()){if(modelDialogNotice!=null)modelDialogNotice.Text=feedback.Text;return;}if(modelDialog!=null){modelDialog.DialogResult=true;modelDialog.Close();}}catch(Exception e){feedback.Text=e.Message;if(modelDialogNotice!=null)modelDialogNotice.Text=e.Message;}};Grid.SetColumn(addModel,1);modelRow.Children.Add(addModel);LocalizedUi.Label(modelForm,()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeT")),modelRow);
            if(kind=="text"){
                contextCapacity=Ui.Field();contextCapacity.Name="TextModelContextCapacity";contextCapacity.MaxLength=7;LocalizedUi.Set(contextCapacity,"ToolTip",()=>(TextResources.Get("textProviderEditor.textProviderEditor.contextCapacityToolTip")));LocalizedUi.Label(modelForm,()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeU")),contextCapacity);
                contextDetected=Ui.Text("",11,Ui.Muted);contextDetected.Name="DetectedModelContextCapacity";modelForm.Children.Add(contextDetected);
                AddCapabilityFields();
                contextCapacity.TextChanged+=delegate{if(updating||selected==null)return;string id=newModel.Text.Trim();int value;if(contextCapacity.Text.Trim().Length==0)selected.ModelContextOverrides.Remove(id);else if(Int32.TryParse(contextCapacity.Text,out value)&&value>=1024&&value<=ModelContextCapacity.Maximum)selected.ModelContextOverrides[id]=value;RefreshContextDescription();RaiseChanged();};
            }
            var removeModel=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeV")));removeModel.Name=N("RemoveTextModel");removeModel.HorizontalAlignment=HorizontalAlignment.Left;removeModel.FontSize=11;removeModel.MinHeight=28;removeModel.Margin=new Thickness(0,6,0,0);removeModel.Click+=delegate {RemoveModel();};detail.Children.Add(removeModel);detail.Children.Add(modelForm);
            feedback=Ui.Text("",11,Ui.Blue);feedback.Name=N("TextProviderNotice");detail.Children.Add(feedback);detail.Children.Add(Extras);
            if(kind!="text"){detail.Children.Clear();detail.Children.Add(Options);LocalizedUi.Label(detail,()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeE")),providerName);detail.Children.Add(connectionHost);detail.Children.Add(Extras);}
            var detailCard=Glass(detail,14);providerDetail=detailCard;emptyProviderHint=LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.textProviderEditor.text.alternativeW")),13,Ui.Muted);emptyProviderHint.Margin=new Thickness(20,30,12,20);Grid.SetColumn(emptyProviderHint,1);Children.Add(emptyProviderHint);Grid.SetColumn(detailCard,1);Children.Add(detailCard);
            providers.SelectionChanged+=delegate {if(updating)return;var item=providers.SelectedItem as ListBoxItem;if(item==null)return;Capture();Show((TextProviderSettings)item.Tag);RaiseChanged();};
            models.SelectionChanged+=delegate {if(updating || connection==null)return;var item=models.SelectedItem as ListBoxItem;string value=item==null?null:item.Tag as string;if(value!=null){connection.SetModel(value);selected.Connection.Model=value;updating=true;newModel.Text=value;modelDisplayName.Text=selected.ModelDisplayNames.ContainsKey(value)?selected.ModelDisplayNames[value]:"";RefreshContextField();updating=false;RefreshModelLabels();RaiseChanged();}};
            newModel.TextChanged+=delegate {
                if(updating || selected==null || connection==null)return;
                updating=true;connection.SetModel(newModel.Text);string id=newModel.Text.Trim();selected.Connection.Model=id;modelDisplayName.Text=selected.ModelDisplayNames.ContainsKey(id)?selected.ModelDisplayNames[id]:"";RefreshContextField();updating=false;RefreshModelLabels();RaiseChanged();
            };
            modelDisplayName.TextChanged+=delegate {if(updating || selected==null)return;string id=newModel.Text.Trim();if(id.Length>0){SetDisplayName(id);RefreshModelLabels();RaiseChanged();}};
            providerName.TextChanged+=delegate {if(updating || selected==null)return;automaticName=false;selected.Name=providerName.Text;RefreshProviders();RaiseChanged();};
            search.TextChanged+=delegate{if(!updating && selected!=null)RefreshModels();};
            refreshModels.Click+=async delegate{await FetchModelsAsync(true);};
            fetchDelay.Tick+=async delegate{fetchDelay.Stop();await FetchModelsAsync();};
            Loaded+=delegate{ScheduleFetch();};Unloaded+=delegate{CancelFetch();};
            IsVisibleChanged+=delegate{if(IsVisible)ScheduleFetch();else CancelFetch();};
            selected=drafts.Find(p=>p.Id==settings.ActiveTextProviderId) ?? drafts[0];RefreshProviders();Show(selected);
        }
        internal static Border Glass(UIElement child,int padding)
        {
            var background=new LinearGradientBrush{StartPoint=new Point(.5,0),EndPoint=new Point(.5,1)};
            background.GradientStops.Add(ThemedStop("#FAFFFFFF",0));background.GradientStops.Add(ThemedStop("#EDF4FAFF",1));
            return new Border{Child=child,Padding=new Thickness(padding),CornerRadius=new CornerRadius(14),BorderBrush=Ui.Brush("#DCE7F8"),BorderThickness=new Thickness(1),Background=background};
        }
        static GradientStop ThemedStop(string color,double offset)
        {
            // Keep the original translucent gradient linked to the live theme brush.
            var stop=new GradientStop{Offset=offset};
            System.Windows.Data.BindingOperations.SetBinding(stop,GradientStop.ColorProperty,new System.Windows.Data.Binding("Color"){Source=Ui.Brush(color)});return stop;
        }
        void RaiseChanged(){if(Changed!=null)Changed();}
        void Capture(bool includeCurrentModel=true)
        {
            if(selected==null || connection==null)return;
            selected.Name=providerName.Text.Trim();selected.Connection=connection.Draft();selected.Connection.ContextWindowTokens=selected.ContextCapacity(selected.Connection.Model);
            selected.ApplyModelCapabilities(selected.Connection);
            if(includeCurrentModel && kind!="text")selected.Models.Clear();
            if(includeCurrentModel && selected.Connection.Model.Length>0 && !selected.Models.Contains(selected.Connection.Model))selected.Models.Add(selected.Connection.Model);
        }
        void Show(TextProviderSettings provider)
        {
            CancelFetch();
            updating=true;selected=provider;providerName.Text=provider.Name;automaticName=ProviderIdentity.AutomaticName(provider.Name,provider);
            connection=new ConnectionSettingsPanel.ConnectionEditor(TextResources.Get("textProviderEditor.show.connectionConnectionSettingsPanelConnectionEditor"),prefix,kind,provider.Connection);
            connection.FactorySelected+=delegate(string brand){selected.BrandId=brand;};
            var saveAndFetch=LocalizedUi.Button(()=>(TextResources.Get("textProviderEditor.show.text")),true);saveAndFetch.Name=N("SaveAndFetchTextModels");saveAndFetch.HorizontalAlignment=HorizontalAlignment.Left;saveAndFetch.Margin=new Thickness(0,12,0,0);
            saveAndFetch.Click+=async delegate{if(SaveConnection!=null && SaveConnection()){unsavedProviders.Remove(selected.Id);await FetchModelsAsync(true);}};if(kind=="text")connection.Children.Add(saveAndFetch);
            connection.Changed+=delegate {
                if(updating)return;string id=connection.Preview().Model;
                if(newModel.Text.Trim()!=id){updating=true;newModel.Text=id;modelDisplayName.Text=selected.ModelDisplayNames.ContainsKey(id)?selected.ModelDisplayNames[id]:"";updating=false;}
                selected.Connection.Model=id;RefreshModelLabels();connectionEndpoint.Text=EndpointLabel(connection.Draft());if(automaticName){updating=true;string name=ProviderIdentity.NameForBrand(selected.BrandId);selected.Name=name.Length>0?name:ProviderIdentity.Name(connection.Draft());providerName.Text=selected.Name;updating=false;}RefreshProviders();RaiseChanged();ScheduleFetch();
            };var summary=new StackPanel();var caption=LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.show.text.alternativeA")),12);caption.FontWeight=FontWeights.SemiBold;summary.Children.Add(caption);connectionEndpoint=Ui.Text(EndpointLabel(provider.Connection),10,Ui.Muted);connectionEndpoint.TextTrimming=TextTrimming.CharacterEllipsis;connectionEndpoint.TextWrapping=TextWrapping.NoWrap;summary.Children.Add(connectionEndpoint);
            connectionHost.Content=new Expander{Name=N("TextConnectionDetails"),Header=summary,Content=connection,IsExpanded=kind!="text" || unsavedProviders.Contains(provider.Id) || !ValidCatalog(provider.Connection),Padding=new Thickness(0,4,0,4)};newModel.Text=provider.Connection.Model;modelDisplayName.Text=provider.ModelDisplayNames.ContainsKey(provider.Connection.Model)?provider.ModelDisplayNames[provider.Connection.Model]:"";feedback.Text="";
            search.Text="";RefreshModels();RefreshProviders();removeProvider.IsEnabled=drafts.Count>1;RefreshContextField();updating=false;ScheduleFetch();
        }
        void RefreshProviders()
        {
            bool previous=updating;updating=true;providers.Items.Clear();
            foreach(var provider in drafts){if(IsEmptyInitial(provider))continue;var name=Ui.Text(String.IsNullOrWhiteSpace(provider.Name)?TextResources.Get("textProviderEditor.refreshProviders.text"):ProviderIdentity.DisplayName(provider),11);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.VerticalAlignment=VerticalAlignment.Center;var row=new DockPanel();var logo=ProviderLogo.Create(provider==selected && connection!=null?connection.Draft().BaseUrl:provider.Connection.BaseUrl,provider.Name,22,provider.BrandId);logo.Margin=new Thickness(0,0,7,0);DockPanel.SetDock(logo,Dock.Left);row.Children.Add(logo);row.Children.Add(name);var item=new ListBoxItem{Style=itemStyle,Tag=provider,Content=row,ToolTip=ProviderIdentity.DisplayName(provider),Padding=new Thickness(6,9,6,9),Margin=new Thickness(0,0,0,4)};providers.Items.Add(item);if(provider==selected)item.IsSelected=true;}
            updating=previous;
        }
        void RefreshModels()
        {
            UpdateEmptyProviders();
            bool previous=updating;updating=true;models.Items.Clear();string query=search.Text.Trim();
            foreach(string id in selected.Models)
            {
                if(query.Length>0 && id.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0 && selected.DisplayName(id).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                models.Items.Add(new ListBoxItem{Style=itemStyle,Tag=id,Content=ModelContent(id),ToolTip=id,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,3)});
            }
            models.SelectedValue=selected.Connection.Model;LocalizedUi.Set(modelCount,"Text",()=>(query.Length==0?TextResources.Format("textProviderEditor.refreshModels.selectedModelsCount",selected.Models.Count):models.Items.Count+" / "+selected.Models.Count));
            LocalizedUi.Set(emptyModels,"Text",()=>(query.Length>0?TextResources.Get("textProviderEditor.refreshModels.emptyModelsQueryLength"):TextResources.Get("textProviderEditor.refreshModels.emptyModelsQueryLength.alternativeA")));
            emptyModels.Visibility=models.Items.Count==0?Visibility.Visible:Visibility.Collapsed;models.Visibility=models.Items.Count==0?Visibility.Collapsed:Visibility.Visible;updating=previous;
            RefreshDiscovery();
        }
        void RefreshDiscovery()
        {
            IList<string> found;discovered.Items.Clear();string query=search.Text.Trim();
            if(catalogs.TryGetValue(selected.Id,out found))foreach(string id in found)
            {
                if(selected.Models.Contains(id) || (query.Length>0 && id.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0))continue;
                discovered.Items.Add(new ListBoxItem{Content=DiscoveryModelRow(id),Tag=id,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(3)});
            }
            discoveryPanel.Visibility=discovered.Items.Count>0?Visibility.Visible:Visibility.Collapsed;
        }
        UIElement ModelContent(string id)
        {
            var row=new DockPanel();var edit=UiIcons.Button("settings",()=>(TextResources.Get("textProviderEditor.modelContent.uiIcons")));edit.Name="EditConfiguredModel";edit.Width=edit.Height=26;edit.MinHeight=26;edit.Padding=new Thickness(4);edit.Margin=new Thickness(6,0,0,0);edit.Click+=delegate{OpenModelEditor(id);};DockPanel.SetDock(edit,Dock.Right);row.Children.Add(edit);
            if(id==selected.Connection.Model){var badge=new Border{Child=LocalizedUi.Text(()=>(TextResources.Get("textProviderEditor.modelContent.text")),10,Ui.Blue),Background=Ui.Pale,CornerRadius=new CornerRadius(5),Padding=new Thickness(6,3,6,3),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(badge,Dock.Right);row.Children.Add(badge);}
            var labels=new StackPanel();var title=Ui.Text(selected.DisplayName(id),12);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(title);
            if(selected.DisplayName(id)!=id){var identifier=Ui.Text(id,11,Ui.Muted);identifier.Margin=new Thickness(0,3,0,0);identifier.TextWrapping=TextWrapping.NoWrap;identifier.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(identifier);}
            int capacity=selected.ContextCapacity(id);labels.Children.Add(LocalizedUi.Text(()=>(capacity>0?TextResources.Format("textProviderEditor.modelContent.template",capacity.ToString("N0")):TextResources.Get("textProviderEditor.modelContent.text.alternativeA")),10,Ui.Muted));row.Children.Add(labels);return row;
        }
        void RefreshModelLabels(){foreach(ListBoxItem item in models.Items)item.Content=ModelContent((string)item.Tag);}
        static bool SameCatalog(ConnectionSettings a,ConnectionSettings b)
        {return a.BaseUrl==b.BaseUrl && a.Protocol==b.Protocol && a.ApiKey==b.ApiKey && a.UseProxy==b.UseProxy && (!a.UseProxy || a.ProxyAddress==b.ProxyAddress);}
        static string EndpointLabel(ConnectionSettings settings){Uri address;return Uri.TryCreate(settings.BaseUrl,UriKind.Absolute,out address)?address.Authority:TextResources.Get("textProviderEditor.endpointLabel.text");}
        bool ValidCatalog(ConnectionSettings draft)
        {
            try{var probe=draft.Copy();probe.Model="catalog";ApiClient.ValidateConnection(probe,"models");if(probe.UseProxy)ProxyConfiguration.Endpoint(probe.ProxyAddress);return true;}
            catch(ApiException){return false;}catch(InvalidOperationException){return false;}
        }
        void CancelFetch()
        {fetchDelay.Stop();var current=fetching;string providerId=fetchingProviderId;fetching=null;fetchingProviderId=null;if(current!=null){attempted.Remove(providerId);current.Cancel();LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.cancelFetch.feedback")));}refreshModels.IsEnabled=true;LocalizedUi.Set(refreshModels,"Content",()=>(TextResources.Get("textProviderEditor.cancelFetch.refreshModelsContent")));loading.Visibility=Visibility.Collapsed;}
        void UpdateCatalogUi(Action update)
        {
            if(Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)return;
            try{if(Dispatcher.CheckAccess())update();else Dispatcher.Invoke(update);}
            catch(OperationCanceledException){if(!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)throw;}
        }
        void ScheduleFetch()
        {
            if(kind!="text")return;
            ConnectionSettings cached;
            if(connection!=null && catalogConnections.TryGetValue(selected.Id,out cached) && !SameCatalog(cached,CatalogDraft())){catalogs.Remove(selected.Id);catalogConnections.Remove(selected.Id);RefreshDiscovery();}
            if(connection==null || !IsLoaded || !IsVisible || unsavedProviders.Contains(selected.Id))return;
            var draft=CatalogDraft();ConnectionSettings previous;
            if(attempted.TryGetValue(selected.Id,out previous) && SameCatalog(previous,draft))return;
            CancelFetch();if(!ValidCatalog(draft))return;fetchDelay.Start();
        }
        async Task FetchModelsAsync(bool reveal=false)
        {
            if(kind!="text" || connection==null || !IsLoaded || !IsVisible)return;
            if(unsavedProviders.Contains(selected.Id)){LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.fetchModelsAsync.feedback")));return;}
            var draft=CatalogDraft();if(!ValidCatalog(draft)){LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.fetchModelsAsync.feedback.alternativeA")));return;}
            CancelFetch();Capture(false);var provider=selected;var cancel=new CancellationTokenSource();fetching=cancel;fetchingProviderId=provider.Id;attempted[provider.Id]=draft.Copy();
            refreshModels.IsEnabled=false;LocalizedUi.Set(refreshModels,"Content",()=>(TextResources.Get("textProviderEditor.fetchModelsAsync.refreshModelsContent")));loading.Visibility=Visibility.Visible;feedback.Foreground=Ui.Muted;LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.fetchModelsAsync.feedback.alternativeB")));
            try
            {
                var capacities=new Dictionary<string,int>();var capabilityMetadata=new Dictionary<string,ModelCapabilitySetting>();IList<string> found;using(var api=new ApiClient(TimeSpan.FromSeconds(15)))found=await api.ListModelsAsync(draft,cancel.Token,capacities,capabilityMetadata).ConfigureAwait(false);
                UpdateCatalogUi(delegate {
                if(cancel.IsCancellationRequested || selected!=provider || !SameCatalog(draft,CatalogDraft()) || !IsLoaded || !IsVisible)return;
                foreach(var pair in capacities)provider.ModelContextWindows[pair.Key]=pair.Value;provider.ServerCapabilities=capabilityMetadata;provider.ApplyModelCapabilities(provider.Connection);RefreshContextDescription();RaiseChanged();
                catalogs[provider.Id]=found;catalogConnections[provider.Id]=draft.Copy();RefreshModels();feedback.Foreground=Ui.Muted;LocalizedUi.Set(feedback,"Text",()=>(found.Count==0?TextResources.Get("textProviderEditor.fetchModelsAsync.countFoundCount.alternativeA"):TextResources.Format("textProviderEditor.fetchModelsAsync.countFoundCount",found.Count)));
                var details=connectionHost.Content as Expander;if(details!=null)details.IsExpanded=false;
                if(reveal && discovered.Items.Count>0)Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(delegate{if(IsVisible && selected==provider)discoveryPanel.BringIntoView();}));
                });
            }
            catch(OperationCanceledException){}
            catch(Exception e)
            {
                UpdateCatalogUi(delegate {if(!cancel.IsCancellationRequested && selected==provider && SameCatalog(draft,CatalogDraft())){feedback.Foreground=Ui.Brush("#AC425D");LocalizedUi.Set(feedback,"Text",()=>((e is ApiException?e.Message:TextResources.Get("textProviderEditor.fetchModelsAsync.feedback.alternativeC"))+TextResources.Get("textProviderEditor.fetchModelsAsync.feedback.alternativeD")));}});
            }
            finally{try{UpdateCatalogUi(delegate {if(fetching==cancel){fetching=null;fetchingProviderId=null;refreshModels.IsEnabled=true;LocalizedUi.Set(refreshModels,"Content",()=>(TextResources.Get("textProviderEditor.fetchModelsAsync.refreshModelsContent.alternativeA")));loading.Visibility=Visibility.Collapsed;}});}finally{cancel.Dispose();}}
        }
        void ValidateContextCapacity(){if(kind!="text"||contextCapacity==null||String.IsNullOrWhiteSpace(contextCapacity.Text))return;int value;if(!Int32.TryParse(contextCapacity.Text,out value)||value<1024||value>ModelContextCapacity.Maximum)throw new InvalidOperationException(TextResources.Get("textProviderEditor.validateContextCapacity.text"));}
        void RefreshContextField(){RefreshCapabilityFields();if(contextCapacity==null||selected==null)return;int value;contextCapacity.Text=selected.ModelContextOverrides.TryGetValue(newModel.Text.Trim(),out value)?value.ToString():"";RefreshContextDescription();}
        void RefreshContextDescription(){if(contextDetected==null||selected==null)return;int value;contextDetected.Text=selected.ModelContextWindows.TryGetValue(newModel.Text.Trim(),out value)?TextResources.Format("textProviderEditor.refreshContextDescription.selectedModelContextWindowsTryGetValue",value.ToString("N0")):TextResources.Get("textProviderEditor.refreshContextDescription.selectedModelContextWindowsTryGetValue.alternativeA");}
        void SetDisplayName(string id){string label=modelDisplayName.Text.Trim();if(label.Length==0)selected.ModelDisplayNames.Remove(id);else selected.ModelDisplayNames[id]=label;}
        void AddProvider()
        {
            if(drafts.Count>=32){LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.addProvider.feedback")));return;}
            var picker=new ProviderPickerWindow(Window.GetWindow(this),kind);if(picker.ShowDialog()!=true)return;Capture();var preset=picker.SelectedProvider;
            var draft=new ConnectionSettings{BaseUrl=preset.Url,Protocol=preset.Protocol,Model=kind=="text"?"":preset.Model};
            drafts.RemoveAll(IsEmptyInitial);selected=new TextProviderSettings{BrandId=preset.Url.Length==0?"custom":ProviderIdentity.Brand(preset.Url),Name=preset.Url.Length==0?TextResources.Format("textProviderEditor.addProvider.presetUrlLength",(drafts.Count+1)):ProviderIdentity.Name(draft),Connection=draft};drafts.Add(selected);unsavedProviders.Add(selected.Id);RefreshProviders();Show(selected);RaiseChanged();
        }
        void RemoveProvider()
        {
            if(drafts.Count<=1)return;int index=drafts.IndexOf(selected);drafts.Remove(selected);selected=drafts[Math.Min(index,drafts.Count-1)];RefreshProviders();Show(selected);RaiseChanged();
        }
        bool AddModel()
        {
            string value=newModel.Text.Trim();if(value.Length==0){LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.addModel.feedback")));return false;}Capture();
            if(selected.Models.Count>=TextProviderSettings.MaximumModels && !selected.Models.Contains(value)){LocalizedUi.Set(feedback,"Text",()=>(TextResources.Format("textProviderEditor.addModel.feedbackTextProviderSettingsMaximumModels",TextProviderSettings.MaximumModels)));return false;}
            if(!selected.Models.Contains(value))selected.Models.Add(value);SetDisplayName(value);SaveCapabilityFields(value);selected.Connection.Model=value;selected.ApplyModelCapabilities(selected.Connection);connection.SetModel(value);RefreshModels();LocalizedUi.Set(feedback,"Text",()=>(TextResources.Get("textProviderEditor.addModel.feedback.alternativeA")));RaiseChanged();return true;
        }
        void RemoveModel()
        {
            var item=models.SelectedItem as ListBoxItem;string value=item==null?null:item.Tag as string;if(value==null)return;selected.Models.Remove(value);selected.ModelDisplayNames.Remove(value);selected.ModelContextWindows.Remove(value);selected.ModelContextOverrides.Remove(value);selected.ModelCapabilities.Remove(value);
            if(connection.Draft().Model==value){selected.Connection.Model=selected.Models.Count==0?"":selected.Models[0];connection.SetModel(selected.Connection.Model);}
            updating=true;newModel.Text=selected.Connection.Model;modelDisplayName.Text=selected.ModelDisplayNames.ContainsKey(selected.Connection.Model)?selected.ModelDisplayNames[selected.Connection.Model]:"";updating=false;RefreshModels();RaiseChanged();
        }
        public ConnectionSettings Preview(){return connection.Preview();}
        public void ExpandConnection(){var details=connectionHost.Content as Expander;if(details!=null)details.IsExpanded=true;}
        public ConnectionSettings Read(){ValidateContextCapacity();Capture();var result=connection.Read();result.ContextWindowTokens=selected.ContextCapacity(result.Model);selected.ApplyModelCapabilities(result);return result;}
        public TextProviderSettings ReadConnectionProvider(){Capture(false);if(String.IsNullOrWhiteSpace(selected.Name))throw new InvalidOperationException(TextResources.Get("textProviderEditor.readConnectionProvider.text"));return selected.Copy();}
        public void ConnectionSaved(PetSettings settings){baseline=settings.ProviderSnapshot(kind);}
        public List<TextProviderSettings> ReadProviders()
        {
            ValidateContextCapacity();Capture();var result=new List<TextProviderSettings>();
            foreach(var provider in drafts){
                if(IsEmptyInitial(provider))continue;
                if(String.IsNullOrWhiteSpace(provider.Name))throw new InvalidOperationException(TextResources.Get("textProviderEditor.readProviders.text"));
                if(provider.Models.Count>TextProviderSettings.MaximumModels || provider.Models.Exists(m=>m.Length>256))throw new InvalidOperationException(TextResources.Format("textProviderEditor.readProviders.template",TextProviderSettings.MaximumModels));
                ProxyConfiguration.Endpoint(ProxyConfiguration.Normalize(provider.Connection.ProxyAddress));
                if(provider.Connection.ApiKey.IndexOfAny(new[]{'\r','\n'})>=0)throw new InvalidOperationException(TextResources.Get("textProviderEditor.readProviders.text.alternativeA"));
                if(!String.IsNullOrWhiteSpace(provider.Connection.BaseUrl)){var probe=provider.Connection.Copy();probe.Model="validation";if(kind=="live"){probe.ApiKey="validation";GeminiLiveSocket.Endpoint(probe);}else ApiClient.Endpoint(probe,ApiClient.ChatPath(probe));}
                var copy=provider.Copy();copy.Normalize();result.Add(copy);
            }return result;
        }
        public bool Matches(PetSettings settings)
        {
            return MatchesView(settings.ProviderView(kind));
        }
        bool MatchesView(PetSettings settings)
        {return MatchesProviders(settings.TextProviders,settings.ActiveTextProviderId);}
        bool MatchesProviders(System.Collections.Generic.List<TextProviderSettings> saved,string activeProviderId)
        {
            Capture(false);if(drafts.Count!=saved.Count || SelectedProviderId!=activeProviderId)return false;
            for(int i=0;i<drafts.Count;i++) {var a=drafts[i];var b=saved[i];if(a.Id!=b.Id || a.Name!=b.Name || a.BrandId!=b.BrandId || a.Models.Count!=b.Models.Count || !SameConnection(a.Connection,b.Connection))return false;for(int m=0;m<a.Models.Count;m++)if(a.Models[m]!=b.Models[m] || a.DisplayName(a.Models[m])!=b.DisplayName(b.Models[m]) || !SameCapacities(a,b,a.Models[m]))return false;}
            return true;
        }
        static bool SameCapacities(TextProviderSettings a,TextProviderSettings b,string model){int ad,bd,am,bm;a.ModelContextWindows.TryGetValue(model,out ad);b.ModelContextWindows.TryGetValue(model,out bd);a.ModelContextOverrides.TryGetValue(model,out am);b.ModelContextOverrides.TryGetValue(model,out bm);return ad==bd&&am==bm;}
        public bool RefreshSaved(PetSettings settings)
        {
            // Saving this draft advances the baseline; external changes only replace a clean draft.
            settings=settings.ProviderView(kind);if(MatchesView(settings)){baseline=settings.ProviderSnapshot("text");foreach(var saved in settings.TextProviders)unsavedProviders.Remove(saved.Id);ScheduleFetch();return false;}
            if(!MatchesProviders(baseline.Providers,baseline.ActiveProviderId))return false;
            settings.EnsureTextProviders();drafts.Clear();foreach(var provider in settings.TextProviders)drafts.Add(provider.Copy());
            baseline=settings.ProviderSnapshot("text");selected=drafts.Find(p=>p.Id==settings.ActiveTextProviderId) ?? drafts[0];RefreshProviders();Show(selected);RaiseChanged();return true;
        }
        bool SameConnection(ConnectionSettings a,ConnectionSettings b)
        {
            return a.BaseUrl==b.BaseUrl && a.Model==b.Model && a.ApiKey==b.ApiKey && (kind=="text"?(a.Protocol=="anthropic")== (b.Protocol=="anthropic"):a.Protocol==b.Protocol) && a.UseProxy==b.UseProxy && a.ProxyAddress==b.ProxyAddress && a.StreamReplies==b.StreamReplies;
        }
    }
}
