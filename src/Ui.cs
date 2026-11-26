using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Dafeiyu
{
    public static class Ui
    {
        public static readonly SolidColorBrush Ink = Brush("#203754"), Muted = Brush("#61738B"), Blue = Brush("#456DE6"), Pale = Brush("#EDF3FF"), Line = Brush("#DCE5F4"), Mint = Brush("#E8F5EF");
        public const double PageContentWidth=900;
        public static readonly Brush PageInk=Brush("#1C2024");
        public static TextBlock PageHeading(string title){var heading=Text(title,26,PageInk);heading.FontWeight=FontWeights.Bold;return heading;}
        private static Style buttonStyle;
        private static Style comboStyle,navigationStyle,fieldStyle,toggleStyle,settingsToggleStyle,passwordStyle,tooltipStyle,emptyFocusStyle;
        public static Style NavigationItemStyle {get{return navigationStyle;}}
        private static System.Windows.Media.Imaging.BitmapSource applicationIcon;
        public static System.Windows.Media.Imaging.BitmapSource ApplicationIcon()
        {
            if(applicationIcon==null)
                using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.Icon"))
                {
                    var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    foreach(var frame in decoder.Frames)if(applicationIcon==null || frame.PixelWidth>applicationIcon.PixelWidth)applicationIcon=frame;
                    applicationIcon.Freeze();
                }
            return applicationIcon;
        }
        public static SolidColorBrush FixedBrush(string hex) {var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Freeze();return brush;}
        public static SolidColorBrush Brush(string hex) { return ThemeManager.Brush(hex); }
        public static void Window(Window window, string title, double width, double height)
        {
            ThemeManager.TrackWindow(window);
            window.Resources[SystemColors.ControlTextBrushKey]=Ink;window.Resources[SystemColors.WindowTextBrushKey]=Ink;
            window.Resources[SystemColors.ControlBrushKey]=ThemeManager.Surface;window.Resources[SystemColors.WindowBrushKey]=ThemeManager.Surface;window.Resources[SystemColors.GrayTextBrushKey]=Muted;
            window.Title = title; window.Width = width; window.Height = height;window.Icon=ApplicationIcon();
            window.MinWidth = width - 40; window.MinHeight = Math.Min(430, height);
            window.Height = Math.Min(height,Math.Max(window.MinHeight,SystemParameters.WorkArea.Height-36));
            // The OS owns maximized bounds for the current monitor and DPI.
            window.MaxHeight=Double.PositiveInfinity;window.MaxWidth=Double.PositiveInfinity;
            window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.FontSize = 13;
            window.Foreground = Ink; window.Background = Brush("#F7F9FD"); window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if(emptyFocusStyle==null){emptyFocusStyle=new Style(typeof(Control));emptyFocusStyle.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(Control)){VisualTree=new FrameworkElementFactory(typeof(Border))}));}
            window.Resources[SystemParameters.FocusVisualStyleKey]=emptyFocusStyle;if(Application.Current!=null)Application.Current.Resources[SystemParameters.FocusVisualStyleKey]=emptyFocusStyle;
            SlimScrollBars.Install(window);window.UseLayoutRounding=true;window.SnapsToDevicePixels=true;
            if(tooltipStyle==null)tooltipStyle=(Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><Setter Property='Background' Value='#202329'/><Setter Property='Foreground' Value='White'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='9,6'/><Setter Property='FontSize' Value='12'/><Setter Property='HasDropShadow' Value='True'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>");
            window.Resources[typeof(ToolTip)]=tooltipStyle;if(Application.Current!=null)Application.Current.Resources[typeof(ToolTip)]=tooltipStyle;ToolTipService.SetInitialShowDelay(window,600);ToolTipService.SetShowDuration(window,3500);ToolTipService.SetBetweenShowDelay(window,250);            if(comboStyle==null)
            {
                comboStyle=CreateComboStyle();
                navigationStyle=(Style)ThemeManager.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Foreground' Value='#203754'/><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter Property='Background' Value='#EAEAEA'/><Setter Property='Foreground' Value='#203754'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            }
            if(passwordStyle==null)passwordStyle=(Style)ThemeManager.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='PasswordBox'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Foreground' Value='#203754'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='surface' Property='Background' Value='#F3F3F3'/><Setter TargetName='surface' Property='BorderBrush' Value='#DADDE1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            window.Resources[typeof(TextBox)]=new Style(typeof(TextBox)){Setters={new Setter(Control.BackgroundProperty,ThemeManager.FieldSurface)}};window.Resources[typeof(ComboBox)]=new Style(typeof(ComboBox),comboStyle){Setters={new Setter(Control.BackgroundProperty,ThemeManager.FieldSurface)}};window.Resources[typeof(PasswordBox)]=new Style(typeof(PasswordBox),passwordStyle){Setters={new Setter(Control.BackgroundProperty,ThemeManager.FieldSurface)}};
        }
        private static Style CreateComboStyle()
        {
            ThemeManager.Brush("#DCE5F4","#52637C");
            ThemeManager.Brush("#203754","#FFFFFF");
            return (Style)ThemeManager.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Foreground' Value='#203754'/>
    <Setter Property='BorderBrush' Value='{DynamicResource DeskPet.Color.DCE5F4.#52637C}'/>
    <Setter Property='ItemContainerStyle'>
        <Setter.Value>
            <Style TargetType='ComboBoxItem'>
                <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
                <Setter Property='Foreground' Value='#203754'/>
                <Setter Property='Padding' Value='10,6'/>
                <Setter Property='Template'>
                    <Setter.Value>
                        <ControlTemplate TargetType='ComboBoxItem'>
                            <Border x:Name='item' Background='Transparent' CornerRadius='6' Padding='{TemplateBinding Padding}'>
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width='*'/>
                                        <ColumnDefinition Width='18'/>
                                    </Grid.ColumnDefinitions>
                                    <ContentPresenter VerticalAlignment='Center'/>
                                    <Path x:Name='check' Grid.Column='1' Data='M0,4 L3,7 L9,0' Stroke='#456DE6'
                                          StrokeThickness='1.4' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                                          Visibility='Hidden' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                                </Grid>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property='IsSelected' Value='True'>
                                    <Setter TargetName='item' Property='Background' Value='#EDF3FF'/>
                                    <Setter TargetName='check' Property='Visibility' Value='Visible'/>
                                </Trigger>
                                <Trigger Property='IsHighlighted' Value='True'>
                                    <Setter TargetName='item' Property='Background' Value='#EFEFEF'/>
                                </Trigger>
                                <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </Setter.Value>
    </Setter>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='ComboBox'>
                <Grid>
                    <Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                            BorderThickness='1' CornerRadius='9'/>
                    <ToggleButton Focusable='False' ClickMode='Press'
                                  IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                        <ToggleButton.Template>
                            <ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'/></ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <Path x:Name='chevron' Data='M0,0 L4,4 L8,0' Stroke='#72839B' StrokeThickness='1.4'
                          StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' IsHitTestVisible='False'
                          HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,13,0' RenderTransformOrigin='0.5,0.5'/>
                    <ContentPresenter x:Name='selection' Content='{TemplateBinding SelectionBoxItem}'
                                      ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'
                                      Margin='12,7,32,7' VerticalAlignment='Center' IsHitTestVisible='False'/>
                    <TextBox x:Name='PART_EditableTextBox' Style='{x:Null}' Visibility='Hidden' IsReadOnly='{TemplateBinding IsReadOnly}'
                             Foreground='{TemplateBinding Foreground}' Background='Transparent'
                             CaretBrush='{DynamicResource DeskPet.Color.203754.#FFFFFF}' Margin='12,7,32,7' VerticalContentAlignment='Center'>
                        <TextBox.Template>
                            <ControlTemplate TargetType='TextBox'>
                                <Border Background='{TemplateBinding Background}'>
                                    <ScrollViewer x:Name='PART_ContentHost'/>
                                </Border>
                            </ControlTemplate>
                        </TextBox.Template>
                    </TextBox>
                    <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}'
                           AllowsTransparency='True' Focusable='False' PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}'
                           VerticalOffset='4'>
                        <Border x:Name='menu' Background='White' BorderBrush='{DynamicResource DeskPet.Color.DCE5F4.#52637C}'
                                BorderThickness='1' CornerRadius='9' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                                Padding='4'>
                            <ScrollViewer MaxHeight='{TemplateBinding MaxDropDownHeight}' CanContentScroll='True'
                                          HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
                                <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/>
                            </ScrollViewer>
                        </Border>
                    </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='surface' Property='Background' Value='#F2F7FF'/>
                    </Trigger>
                    <Trigger Property='IsKeyboardFocusWithin' Value='True'>
                        <Setter TargetName='surface' Property='BorderBrush' Value='#456DE6'/>
                    </Trigger>
                    <Trigger Property='IsDropDownOpen' Value='True'>
                        <Setter TargetName='surface' Property='BorderBrush' Value='#456DE6'/>
                        <Setter TargetName='chevron' Property='RenderTransform'>
                            <Setter.Value><ScaleTransform ScaleY='-1'/></Setter.Value>
                        </Setter>
                    </Trigger>
                    <Trigger Property='IsEditable' Value='True'>
                        <Setter TargetName='selection' Property='Visibility' Value='Hidden'/>
                        <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
                    </Trigger>
                    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
    <Setter Property='MaxDropDownHeight' Value='290'/>
</Style>");
        }
        public static Button Button(string text, bool primary = false)
        {
            if (buttonStyle == null)
                buttonStyle = (Style)ThemeManager.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Grid x:Name='root'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9'/><Border x:Name='highlight' Background='Transparent' CornerRadius='9'/><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='highlight' Property='Background' Value='#10000000'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='highlight' Property='Background' Value='#10000000'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='highlight' Property='Background' Value='#20000000'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='root' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            return new Button { Content = text, Style = buttonStyle, MinHeight = 36, Padding = new Thickness(13, 6, 13, 6),
                Background = primary ? Blue : ThemeManager.Surface, Foreground = primary ? Brushes.White : Ink,
                BorderBrush = primary ? Blue : Line, Cursor = System.Windows.Input.Cursors.Hand, FontWeight=primary?FontWeights.SemiBold:FontWeights.Normal, Margin = new Thickness(0, 0, 8, 0) };
        }
        public static TextBlock Text(string text, double size = 13, Brush color = null)
        { return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.65 }; }
        // Inline labels use natural font metrics; paragraph spacing belongs in Text().
        public static TextBlock InlineText(string text, double size = 13, Brush color = null)
        { return new TextBlock { Text=text, FontSize=size, Foreground=color??Ink, TextWrapping=TextWrapping.NoWrap, TextTrimming=TextTrimming.CharacterEllipsis, VerticalAlignment=VerticalAlignment.Center }; }
        internal static FrameworkElement SettingRow(Func<string> title,Func<string> description,FrameworkElement control,bool emphasizeTitle=true,bool alignRight=false,double stackBelow=0)
        {
            bool trailing=alignRight||control is CheckBox||control is Button;
            var row=new Grid{Margin=new Thickness(0,0,0,14)};
            row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=trailing?GridLength.Auto:new GridLength(stackBelow>0?1.5:1,GridUnitType.Star)});
            var labels=new StackPanel{Margin=new Thickness(0,4,20,0)};var caption=LocalizedUi.Text(title,13,Ink);caption.FontWeight=emphasizeTitle?FontWeights.SemiBold:FontWeights.Normal;labels.Children.Add(caption);
            if(description!=null){var help=LocalizedUi.Text(description,11,Muted);help.Margin=new Thickness(0,5,0,0);labels.Children.Add(help);}row.Children.Add(labels);
            if(control is CheckBox&&toggleStyle!=null&&ReferenceEquals(control.Style,toggleStyle))control.Style=ToggleStyle(true);
            LocalizedUi.Automation(control,title);control.HorizontalAlignment=trailing?HorizontalAlignment.Right:HorizontalAlignment.Stretch;control.VerticalAlignment=VerticalAlignment.Center;control.Margin=new Thickness(0);Grid.SetColumn(control,1);row.Children.Add(control);
            if(stackBelow>0)
            {
                row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                row.SizeChanged+=delegate(object sender,SizeChangedEventArgs args)
                {
                    bool stacked=args.NewSize.Width<stackBelow;
                    if(stacked==(Grid.GetRow(control)==1))return;
                    Grid.SetColumnSpan(labels,stacked?2:1);
                    Grid.SetColumn(control,stacked?0:1);
                    Grid.SetColumnSpan(control,stacked?2:1);
                    Grid.SetRow(control,stacked?1:0);
                    control.Margin=new Thickness(0,stacked?8:0,0,0);
                };
            }
            return row;
        }
        public static TextBox Field(string value = "", bool multiline = false)
        {
            if(fieldStyle==null)fieldStyle=(Style)ThemeManager.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='surface' Property='Background' Value='#F3F3F3'/><Setter TargetName='surface' Property='BorderBrush' Value='#DADDE1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
            return new TextBox { Style=fieldStyle,CaretBrush=ThemeManager.Caret,Text = value ?? "", MinHeight = 38, Padding = new Thickness(10, 7, 10, 7), BorderBrush = Line, BorderThickness = new Thickness(1), Background=ThemeManager.FieldSurface,Foreground=Ink,VerticalContentAlignment=multiline?VerticalAlignment.Top:VerticalAlignment.Center,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, AcceptsReturn = multiline, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden }; }
        public static Border Card(UIElement child)
        { return new Border { Child = child, Background=ThemeManager.CardSurface, CornerRadius = new CornerRadius(14), BorderBrush = ThemeManager.CardBorder, BorderThickness = new Thickness(1), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 16) }; }
        public static void Label(Panel panel, string caption, UIElement field)
        {
            var label = Text(caption, 12, Ink); label.Margin = new Thickness(0, 12, 0, 6); panel.Children.Add(label); panel.Children.Add(field);
        }
        public static CheckBox Toggle(string label, bool value, Action<bool> changed){return Toggle(label,value,changed,false);}
        internal static CheckBox Toggle(string label,bool value,Action<bool> changed,bool trailing)
        {
            var toggle = new CheckBox { Style=ToggleStyle(trailing),Background=ThemeManager.ToggleOffSurface,Foreground=Ink,Content = label, IsChecked = value, MinHeight=24,Margin = new Thickness(0, 6, 0, 9), VerticalContentAlignment = VerticalAlignment.Center };
            toggle.Checked += delegate { changed(true); }; toggle.Unchecked += delegate { changed(false); }; return toggle;
        }
        static Style ToggleStyle(bool trailing)
        {
            var cached=trailing?settingsToggleStyle:toggleStyle;
            if(cached!=null)return cached;
            var track=@"<Border x:Name='track' Width='32' Height='20' CornerRadius='10' BorderThickness='1' BorderBrush='Transparent' Background='{TemplateBinding Background}' VerticalAlignment='Center' "+
                (trailing?"Grid.Column='1'":"Margin='0,0,9,0'")+@"><Ellipse x:Name='thumb' Width='12' Height='12' Fill='White' HorizontalAlignment='Left' Margin='3,0,0,0'/></Border>";
            var body=trailing?@"<Grid Background='Transparent'>
  <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
  <ContentPresenter RecognizesAccessKey='True' VerticalAlignment='Center' Margin='0,0,12,0'>
    <ContentPresenter.ContentTemplate><DataTemplate><AccessText Text='{Binding}' TextWrapping='Wrap'/></DataTemplate></ContentPresenter.ContentTemplate>
  </ContentPresenter>"+track+"</Grid>":"<StackPanel Orientation='Horizontal' Background='Transparent'>"+track+"<ContentPresenter RecognizesAccessKey='True' VerticalAlignment='Center'/></StackPanel>";
            var style=(Style)ThemeManager.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
  <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <Border x:Name='highlight' CornerRadius='7' Background='Transparent' Padding='"+(trailing?"0,3,0,3":"3")+@"'>"+body+@"</Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='highlight' Property='Background' Value='#EFEFEF'/></Trigger>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='highlight' Property='Background' Value='#EFEFEF'/></Trigger>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='track' Property='Background' Value='#456DE6'/><Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='thumb' Property='Margin' Value='0,0,3,0'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>");
            if(trailing)settingsToggleStyle=style;else toggleStyle=style;
            return style;
        }
        public static CheckBox ReplySpeechToggle(PetWindow pet){return ReplySpeechToggle(pet,false);}
        internal static CheckBox ReplySpeechToggle(PetWindow pet,bool trailing)
        {
            Func<string> label=()=>TextResources.Get("ui.replySpeechToggle.toggle");
            var toggle=LocalizedUi.With(Toggle(label(),pet.Settings.ReadReplies,pet.SetReplySpeech,trailing),"Content",label);toggle.Name="TtsOutput";
            LocalizedUi.Set(toggle,"ToolTip",()=>(TextResources.Get("ui.replySpeechToggle.toggleToolTip")));
            Action sync=()=>toggle.IsChecked=pet.Settings.ReadReplies;
            toggle.Loaded+=delegate {sync();pet.ReplySpeechChanged+=sync;};
            toggle.Unloaded+=delegate {pet.ReplySpeechChanged-=sync;};return toggle;
        }
        public static ScrollViewer Scroll(UIElement child) { return new ScrollViewer { FocusVisualStyle=null, Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; }
public static TextBlock PageHeading(System.Func<string> title){return LocalizedUi.With(PageHeading(title()),"Text",title);}
        public static Button Button(System.Func<string> text, bool primary = false){return LocalizedUi.With(Button(text(),primary),"Content",text);}
        public static TextBlock Text(System.Func<string> text, double size = 13, Brush color = null){return LocalizedUi.With(Text(text(),size,color),"Text",text);}
        public static TextBlock InlineText(System.Func<string> text, double size = 13, Brush color = null){return LocalizedUi.With(InlineText(text(),size,color),"Text",text);}
        public static CheckBox Toggle(System.Func<string> label, bool value, Action<bool> changed){return LocalizedUi.With(Toggle(label(),value,changed),"Content",label);}    }
}
