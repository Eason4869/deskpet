using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    // A shared composer keeps the small bubble and full conversation consistent.
    internal static class ChatUi
    {
        // The prediction ghost reuses the composer placeholder created below,
        // so it needs no extra layout and hides itself whenever the draft is
        // non-empty. Tests and the prediction controller resolve it here.
        public static readonly DependencyProperty ComposerPlaceholderProperty=DependencyProperty.RegisterAttached("ComposerPlaceholder",typeof(TextBlock),typeof(ChatUi),new PropertyMetadata(null));
        public static void SetComposerPlaceholder(MarkdownEditor input,TextBlock value){input.SetValue(ComposerPlaceholderProperty,value);}
        public static TextBlock GetComposerPlaceholder(MarkdownEditor input){return (TextBlock)input.GetValue(ComposerPlaceholderProperty);}
        static ControlTemplate actionTemplate;
        static ControlTemplate menuTemplate;
        static ControlTemplate menuItemTemplate;
        static ControlTemplate processTemplate;
        public static void ProcessExpander(Expander control)
        {
            if(processTemplate==null)processTemplate=(ControlTemplate)ThemeManager.Parse(TextResources.Get("chatUi.processExpander.themeManagerParse"));
            control.Template=processTemplate;control.FocusVisualStyle=null;control.BorderThickness=new Thickness(0);control.Background=Brushes.Transparent;
        }
        public static ContextMenu Menu()
        {
            if(menuTemplate==null)menuTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'><Border Background='White' BorderBrush='#E6E8EC' BorderThickness='1' CornerRadius='11' Padding='5' Margin='4'><Border.Effect><DropShadowEffect BlurRadius='12' ShadowDepth='2' Opacity='0.12'/></Border.Effect><ScrollViewer VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' CanContentScroll='True'><StackPanel IsItemsHost='True'/></ScrollViewer></Border></ControlTemplate>");
            var menu=new ContextMenu{Template=menuTemplate,Background=ThemeManager.Surface,Foreground=Ui.Brush("#3F434A"),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=12,Padding=new Thickness(0)};
            menu.Resources[SystemColors.HighlightBrushKey]=Ui.Brush("#EFEFEF");menu.Resources[SystemColors.HighlightTextBrushKey]=Ui.Ink;
            if(menuItemTemplate==null)menuItemTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'><Grid><Border x:Name='surface' Background='Transparent' CornerRadius='7' Padding='{TemplateBinding Padding}'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='16'/></Grid.ColumnDefinitions><TextBlock x:Name='check' Text='✓' Visibility='Hidden' VerticalAlignment='Center'/><ContentPresenter Grid.Column='1' Content='{TemplateBinding Icon}' Margin='0,0,7,0' VerticalAlignment='Center'/><ContentPresenter Grid.Column='2' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/><TextBlock Grid.Column='3' Text='{TemplateBinding InputGestureText}' Foreground='#81858B' Margin='22,0,4,0' VerticalAlignment='Center'/><TextBlock x:Name='arrow' Grid.Column='4' Text='›' Visibility='Collapsed' VerticalAlignment='Center' HorizontalAlignment='Right'/></Grid></Border><Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-2' IsOpen='{TemplateBinding IsSubmenuOpen}' AllowsTransparency='True' Focusable='False'><Border Background='White' BorderBrush='#E6E8EC' BorderThickness='1' CornerRadius='11' Padding='5' Margin='4'><Border.Effect><DropShadowEffect BlurRadius='12' ShadowDepth='2' Opacity='0.12'/></Border.Effect><ScrollViewer MaxHeight='420' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' CanContentScroll='True'><StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/></ScrollViewer></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='HasItems' Value='True'><Setter TargetName='arrow' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            var items=new Style(typeof(MenuItem));items.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));items.Setters.Add(new Setter(Control.TemplateProperty,menuItemTemplate));items.Setters.Add(new Setter(Control.MinHeightProperty,29.0));items.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,4,8,4)));items.Setters.Add(new Setter(Control.FontSizeProperty,12.0));menu.Resources[typeof(MenuItem)]=items;return menu;
        }
        public static ContextMenu EditorMenu(Control editor,bool readOnly)
        {
            var menu=Menu();
            if(!readOnly)menu.Items.Add(new MenuItem{Header=TextResources.Get("chatUi.editorMenu.header"),InputGestureText="Ctrl+X",Command=System.Windows.Input.ApplicationCommands.Cut,CommandTarget=editor});
            menu.Items.Add(new MenuItem{Header=TextResources.Get("common.copy"),InputGestureText="Ctrl+C",Command=System.Windows.Input.ApplicationCommands.Copy,CommandTarget=editor});
            if(!readOnly)menu.Items.Add(new MenuItem{Header=TextResources.Get("chatUi.editorMenu.header.alternativeA"),InputGestureText="Ctrl+V",Command=System.Windows.Input.ApplicationCommands.Paste,CommandTarget=editor});
            menu.Items.Add(new Separator());menu.Items.Add(new MenuItem{Header=TextResources.Get("chatUi.editorMenu.header.alternativeB"),InputGestureText="Ctrl+A",Command=System.Windows.Input.ApplicationCommands.SelectAll,CommandTarget=editor});return menu;
        }
        public static string SendHint(PetSettings settings){return settings.SendOnEnter?TextResources.Get("chatUi.sendHint.text"):TextResources.Get("chatUi.sendHint.text.alternativeA");}
        public static FrameworkElement Composer(MarkdownEditor input,UIElement leading,UIElement trailing,TextBlock status,bool compact,UIElement attachments=null)
        {
            input.BorderThickness=new Thickness(0);input.Background=Brushes.Transparent;
            // The composer has its own white surface; do not inherit form-field focus fills.
            input.FocusVisualStyle=null;
            input.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RichTextBox'><Border Background='Transparent'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border></ControlTemplate>");
            input.Padding=new Thickness(4,6,4,6);input.FontSize=14;input.VerticalContentAlignment=VerticalAlignment.Top;
            input.MinHeight=48;input.MaxHeight=compact?92:140;
            input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
            System.Windows.Automation.AutomationProperties.SetName(input,TextResources.Get("chatUi.composer.automationAutomationPropertiesSetName"));
            var field=new Grid();int inputRow=0;
            if(attachments!=null){field.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});field.Children.Add(attachments);inputRow=1;}
            field.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(input,inputRow);field.Children.Add(input);
            var placeholder=new TextBlock{Name="ComposerPlaceholder",Text=TextResources.Get("chatUi.composer.text"),FontSize=14,Foreground=Ui.Brush("#B8BCC2"),VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};Grid.SetRow(placeholder,inputRow);field.Children.Add(placeholder);
            SetComposerPlaceholder(input,placeholder);
            Action syncPlaceholder=delegate {
                bool show=input.Text.Length==0;
                placeholder.Visibility=show?Visibility.Visible:Visibility.Collapsed;
                if(!show || !input.IsLoaded)return;
                Rect firstLine=input.GetRectFromCharacterIndex(0);
                if(firstLine.IsEmpty)return;
                Point origin=input.TranslatePoint(firstLine.TopLeft,field);
                var margin=new Thickness(origin.X,origin.Y,0,0);
                if(placeholder.Margin!=margin)placeholder.Margin=margin;
            };
            input.TextChanged+=delegate{syncPlaceholder();};
            input.GotKeyboardFocus+=delegate{syncPlaceholder();};
            input.LostKeyboardFocus+=delegate{syncPlaceholder();};
            input.LayoutUpdated+=delegate{syncPlaceholder();};
            syncPlaceholder();
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.Children.Add(field);
            var toolbar=new Grid{Name="ComposerToolbar",Margin=new Thickness(2,8,2,0),MinWidth=0};toolbar.ColumnDefinitions.Add(new ColumnDefinition{MinWidth=0});toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            if(leading!=null)toolbar.Children.Add(leading);
            if(trailing!=null){Grid.SetColumn(trailing,1);toolbar.Children.Add(trailing);}
            Grid.SetRow(toolbar,1);body.Children.Add(toolbar);
            var surface=new Border{Name="ComposerSurface",Child=body,Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E3E7ED"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(8)};
            input.GotKeyboardFocus+=delegate{surface.BorderBrush=Ui.Brush("#DADDE1");};
            input.LostKeyboardFocus+=delegate{surface.BorderBrush=Ui.Brush("#E3E7ED");};
            var result=new StackPanel();result.Children.Add(surface);
            status.FontSize=11;status.Margin=new Thickness(3,7,3,0);status.MaxHeight=38;
            result.Children.Add(status);
            return result;
        }
        public static Button Action(string icon,string label)
        {
            var button=UiIcons.Button(icon,label);button.Content=UiIcons.Image(icon,icon=="send"?Ui.Blue:Ui.Ink);
            ((FrameworkElement)button.Content).Width=20;((FrameworkElement)button.Content).Height=20;
            if(actionTemplate==null)actionTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='18'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.88'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.88'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.65'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            button.Template=actionTemplate;
            button.Background=Ui.Brush(icon=="send"?"#F0F4FF":"#FFF3F5");
            button.BorderBrush=Ui.Brush(icon=="send"?"#DBE4FC":"#F5DCE1");button.Margin=new Thickness(0);
            return button;
        }
    }
}
