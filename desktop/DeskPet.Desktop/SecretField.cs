using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class SecretField : Grid
{
    readonly TextBox input;
    readonly Button eye;
    readonly List<Visual> visibilityHosts=new();
    internal SecretField(TextBox input)
    {
        this.input=input;input.PasswordChar='●';input.RevealPassword=false;input.MinWidth=0;
        // Supplying our own right content avoids a second theme reveal button.
        eye=new Button{Width=32,Height=30,Padding=new Thickness(5),Background=Brushes.Transparent,BorderThickness=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
        eye.Name=string.IsNullOrEmpty(input.Name)?"ToggleSecretVisibility":input.Name+"Visibility";
        input.InnerRightContent=eye;Children.Add(input);
        eye.Click+=(_,_)=>{input.RevealPassword=!input.RevealPassword;UpdateEye();input.Focus();};
        input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty&&!input.IsFocused)Hide();};
        AttachedToVisualTree+=(_,_)=>{foreach(var host in this.GetVisualAncestors()){visibilityHosts.Add(host);host.PropertyChanged+=HostChanged;}};
        DetachedFromVisualTree+=(_,_)=>{Hide();foreach(var host in visibilityHosts)host.PropertyChanged-=HostChanged;visibilityHosts.Clear();};
        PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty&&!IsVisible)Hide();};
        UpdateEye();
    }
    void Hide(){if(!input.RevealPassword)return;input.RevealPassword=false;UpdateEye();}
    void HostChanged(object? sender,AvaloniaPropertyChangedEventArgs e){if(e.Property==IsVisibleProperty&&!IsEffectivelyVisible)Hide();}
    void UpdateEye()
    {
        LocalizedUi.Tooltip(eye,()=>input.RevealPassword?TextResources.Get("secretField.hide"):TextResources.Get("secretField.show"));
        LocalizedUi.Automation(eye,()=>input.RevealPassword?TextResources.Get("secretField.hide"):TextResources.Get("secretField.show"));
        string shape="M2,12 C6,4 18,4 22,12 C18,20 6,20 2,12 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12";
        if(input.RevealPassword)shape+=" M3,3 L21,21";
        eye.Content=new Viewbox{Width=20,Height=20,Child=new Path{Data=Geometry.Parse(shape),Stroke=SettingsVisual.Muted,StrokeThickness=1.8,Width=24,Height=24}};
    }
}
