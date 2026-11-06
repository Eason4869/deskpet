using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Dafeiyu
{
    // Keep the original PasswordBox as the source of truth for existing editors.
    internal sealed class SecretField : Grid
    {
        readonly PasswordBox password;
        readonly TextBox plain;
        readonly Button eye;
        bool revealed,syncing;

        internal SecretField(PasswordBox password)
        {
            this.password=password;
            var padding=password.Padding;password.Padding=new Thickness(padding.Left,padding.Top,padding.Right+36,padding.Bottom);
            plain=Ui.Field();plain.Name=String.IsNullOrEmpty(password.Name)?"":password.Name+"Visible";
            plain.Visibility=Visibility.Collapsed;plain.MinHeight=password.MinHeight;plain.Padding=password.Padding;
            plain.MaxLength=password.MaxLength;plain.Background=password.Background;plain.BorderBrush=password.BorderBrush;
            plain.FontSize=password.FontSize;
            eye=new Button{Width=32,Height=30,Padding=new Thickness(5),Margin=new Thickness(0,0,4,0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
            eye.Name=String.IsNullOrEmpty(password.Name)?"ToggleSecretVisibility":password.Name+"Visibility";
            plain.SetBinding(IsEnabledProperty,new Binding("IsEnabled"){Source=password});
            eye.SetBinding(IsEnabledProperty,new Binding("IsEnabled"){Source=password});
            eye.Click+=delegate{SetRevealed(!revealed,true);};
            Children.Add(password);Children.Add(plain);Children.Add(eye);
            password.PasswordChanged+=delegate{if(!syncing)SetRevealed(false,false);};
            plain.TextChanged+=delegate{if(!revealed||syncing)return;syncing=true;try{password.Password=plain.Text;}finally{syncing=false;}};
            Unloaded+=delegate{SetRevealed(false,false);};
            IsVisibleChanged+=delegate{if(!IsVisible)SetRevealed(false,false);};
            UpdateEye();
        }

        void SetRevealed(bool value,bool focus)
        {
            if(revealed==value)return;
            revealed=value;syncing=true;
            try{plain.Text=value?password.Password:"";plain.Visibility=value?Visibility.Visible:Visibility.Collapsed;password.Visibility=value?Visibility.Collapsed:Visibility.Visible;}
            finally{syncing=false;}
            UpdateEye();
            if(focus){if(value){plain.Focus();plain.CaretIndex=plain.Text.Length;}else password.Focus();}
        }

        void UpdateEye()
        {
            LocalizedUi.Set(eye,"ToolTip",()=>revealed?TextResources.Get("secretField.hide"):TextResources.Get("secretField.show"));
            LocalizedUi.Automation(eye,()=>revealed?TextResources.Get("secretField.hide"):TextResources.Get("secretField.show"));
            string shape="M2,12 C6,4 18,4 22,12 C18,20 6,20 2,12 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12";
            if(revealed)shape+=" M3,3 L21,21";
            eye.Content=new Viewbox{Width=20,Height=20,Child=new Path{Data=Geometry.Parse(shape),Stroke=Ui.Muted,StrokeThickness=1.8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=24,Height=24}};
        }
    }
}
