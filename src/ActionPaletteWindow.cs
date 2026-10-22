using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed class ActionPaletteWindow : Window
    {
        readonly PetWindow pet;
        readonly Canvas actions;
        readonly TextBlock selection;
        const double Diameter=432,Center=216,OuterRadius=210,InnerRadius=90;
        static ControlTemplate sectorTemplate;
        readonly Button interactionsTab,basicTab;
        readonly TextBlock hint;
        readonly List<Button> actionButtons=new List<Button>();
        readonly DispatcherTimer availability=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(200) };
        bool basic,closed;
        Point invocationPoint;

        public ActionPaletteWindow(PetWindow companion)
        {
            pet=companion;
            Ui.Window(this,"和"+pet.Skin.Name+"互动",360,394);
            Owner=pet;ShowInTaskbar=false;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;
            MinWidth=0;MinHeight=0;Topmost=pet.Topmost;WindowStartupLocation=WindowStartupLocation.Manual;Focusable=true;
            var layout=new Grid{Width=448,Height=490};layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(440)});layout.RowDefinitions.Add(new RowDefinition());
            actions=new Canvas{Width=Diameter,Height=Diameter,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};layout.Children.Add(actions);
            var center=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10)};
            var title=Ui.Text(pet.Skin.Name,15);title.FontWeight=FontWeights.SemiBold;title.HorizontalAlignment=HorizontalAlignment.Center;center.Children.Add(title);
            var tabs=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,8)};
            interactionsTab=Ui.Button("技能");interactionsTab.Name="PaletteInteractionsTab";
            basicTab=Ui.Button("动作");basicTab.Name="PaletteBasicTab";
            foreach(var tab in new[]{interactionsTab,basicTab}){tab.Width=58;tab.MinHeight=30;tab.Padding=new Thickness(8,3,8,3);tab.Margin=new Thickness(2,0,2,0);tab.FontSize=11;tabs.Children.Add(tab);}
            interactionsTab.Click+=delegate {SelectSection(false);};basicTab.Click+=delegate {SelectSection(true);};center.Children.Add(tabs);
            selection=Ui.Text("选择一个技能",12,Ui.Muted);selection.Name="PaletteSelection";selection.TextAlignment=TextAlignment.Center;center.Children.Add(selection);var dismiss=UiIcons.Button("close","收起动作轮盘");dismiss.Name="CloseActionWheel";dismiss.Width=28;dismiss.Height=28;dismiss.MinHeight=28;dismiss.Padding=new Thickness(5);dismiss.Margin=new Thickness(0,4,0,0);dismiss.HorizontalAlignment=HorizontalAlignment.Center;dismiss.Click+=delegate{Close();};center.Children.Add(dismiss);
            var hub=new Border{Width=174,Height=174,CornerRadius=new CornerRadius(87),Background=Glass(true),BorderBrush=Ui.Brush("#EFFFFFFF"),BorderThickness=new Thickness(1.5),Child=center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};layout.Children.Add(hub);
            hint=Ui.Text("点选播放 · 滚轮切换分类 · Esc 收起",11,Ui.Muted);hint.TextAlignment=TextAlignment.Center;hint.VerticalAlignment=VerticalAlignment.Center;
            var help=new Border{Child=hint,Background=Glass(true),BorderBrush=Ui.Brush("#BFFFFFFF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(12,4,12,4),Margin=new Thickness(18,4,18,4)};Grid.SetRow(help,1);layout.Children.Add(help);
            Content=new Viewbox{Child=layout,Stretch=Stretch.Uniform};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.Key==Key.Escape){e.Handled=true;Close();return;}
                if(e.Key==Key.Left || e.Key==Key.Right || e.Key==Key.Up || e.Key==Key.Down)
                {
                    int index=actionButtons.FindIndex(button=>button.IsKeyboardFocused),step=e.Key==Key.Left || e.Key==Key.Up?-1:1;
                    if(actionButtons.Count>0)actionButtons[index<0?(step>0?0:actionButtons.Count-1):(index+step+actionButtons.Count)%actionButtons.Count].Focus();e.Handled=true;
                }
            };
            PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){e.Handled=true;SelectSection(e.Delta<0);};
            PreviewMouseDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Middle){e.Handled=true;Close();}};
            Deactivated+=delegate {if(IsVisible && !closed)Close();};
            Closed+=delegate {closed=true;availability.Stop();};
            availability.Tick+=delegate {RefreshAvailability();};
            SelectSection(false);
        }

        public void ShowForPet()
        {
            if(closed || pet.IsUpdating)return;
            if(!IsVisible)invocationPoint=DesktopInterop.Cursor();
            if(!IsVisible){Opacity=0;new WindowInteropHelper(this).EnsureHandle();UpdateLayout();PlaceNearPet();Show();}
            UpdateLayout();PlaceNearPet();Opacity=1;Activate();
            Keyboard.Focus(this);
            availability.Start();
        }

        void SelectSection(bool showBasic)
        {
            basic=showBasic;actions.Children.Clear();actionButtons.Clear();
            SetTab(interactionsTab,!basic);SetTab(basicTab,basic);
            if(basic)
            {
                for(int index=0;index<AnimationPlayer.States.Length;index++)
                    AddAction(AnimationPlayer.Labels[index],AnimationPlayer.States[index],false);
            }
            else
            {
                AddAction("喂零食","feeding",true);
                AddAction(pet.IsSleeping?"叫醒":"小憩","sleeping",true);AddAction(pet.Skin.SpecialAction,"special",true);
                if(AdditionalInteractions.Supports(pet.Skin))AddAction(AdditionalInteractions.SpecialName(pet.Skin),"special2",true);
            }
            selection.Text=basic?"选择一个动作":"选择一个技能";hint.Text="点选播放 · 滚轮切换分类 · Esc 收起";
            RefreshAvailability();
            if(IsVisible){UpdateLayout();PlaceNearPet();}
        }

        static void SetTab(Button button,bool selected)
        {
            button.Background=selected?Ui.Blue:Ui.Brush("#60FFFFFF");button.Foreground=selected?Brushes.White:Ui.Ink;
            button.BorderBrush=selected?Ui.Blue:Ui.Line;button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetHelpText(button,selected?"当前动作分类":"切换动作分类");
        }

        void AddAction(string label,string action,bool interaction)
        {
            int count=basic?AnimationPlayer.States.Length:AdditionalInteractions.Supports(pet.Skin)?4:3,index=actionButtons.Count;
            double angle=-90+index*360.0/count,radians=angle*Math.PI/180;
            var tile=new StackPanel {Width=76,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};
            var image=new Image {Name="PalettePreview_"+action.Replace('-','_'),Source=pet.GetActionPreview(action,interaction),Width=basic?48:64,Height=basic?52:70,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,3)};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);tile.Children.Add(image);
            var caption=Ui.Text(label,13.5);caption.HorizontalAlignment=HorizontalAlignment.Center;tile.Children.Add(caption);
            var content=new Canvas{Width=Diameter,Height=Diameter,IsHitTestVisible=false};content.Children.Add(tile);
            Canvas.SetLeft(tile,Center+153*Math.Cos(radians)-38);Canvas.SetTop(tile,Center+153*Math.Sin(radians)-(basic?39:48));
            if(sectorTemplate==null)sectorTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Grid><Path x:Name='sector' Data='{TemplateBinding Tag}' Fill='{TemplateBinding Background}' Stroke='{TemplateBinding BorderBrush}' StrokeThickness='1' StrokeLineJoin='Round'/><ContentPresenter/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='sector' Property='Fill' Value='#EDF3FF'/><Setter TargetName='sector' Property='Stroke' Value='#456DE6'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='sector' Property='Fill' Value='#DCE5FF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            var button=new Button{Template=sectorTemplate,Width=Diameter,Height=Diameter,Background=Glass(false),BorderBrush=Ui.Brush("#DBFFFFFF"),Cursor=Cursors.Hand,Content=content,Tag=Sector(angle,360.0/count)};
            button.Name=(interaction?"PaletteInteraction_":"PaletteAction_")+action.Replace('-','_');button.ToolTip=Description(action);
            System.Windows.Automation.AutomationProperties.SetName(button,label);
            button.MouseEnter+=delegate{selection.Text=label;hint.Text=Description(action);};button.GotKeyboardFocus+=delegate{selection.Text=label;hint.Text=Description(action);};
            button.Click+=delegate
            {
                if(pet.IsUpdating){RefreshAvailability();return;}
                if(interaction)pet.PlayInteraction(action=="sleeping" && pet.IsSleeping?"wake":action);
                else pet.PlayAction(action,action=="jumping"?.7:2);
                pet.NotifyGuideInteraction("wheel");Close();
            };
            actionButtons.Add(button);actions.Children.Add(button);
        }

        static Point Polar(double radius,double degrees)
        {double angle=degrees*Math.PI/180;return new Point(Center+radius*Math.Cos(angle),Center+radius*Math.Sin(angle));}
        static Geometry Sector(double centerAngle,double sweep)
        {
            double start=centerAngle-sweep/2,end=centerAngle+sweep/2;
            // Offset both radial edges by the same physical distance. A fixed
            // angular gap widens towards the rim and makes the wheel uneven.
            const double halfGap=2;
            double innerInset=Math.Asin(halfGap/InnerRadius)*180/Math.PI,outerInset=Math.Asin(halfGap/OuterRadius)*180/Math.PI;
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                context.BeginFigure(Polar(InnerRadius,start+innerInset),true,true);
                context.LineTo(Polar(OuterRadius,start+outerInset),true,false);
                context.ArcTo(Polar(OuterRadius,end-outerInset),new Size(OuterRadius,OuterRadius),0,sweep>180,SweepDirection.Clockwise,true,false);
                context.LineTo(Polar(InnerRadius,end-innerInset),true,false);
                context.ArcTo(Polar(InnerRadius,start+innerInset),new Size(InnerRadius,InnerRadius),0,sweep>180,SweepDirection.Counterclockwise,true,false);
            }
            geometry.Freeze();return geometry;
        }

        string Description(string action)
        {
            switch(action)
            {
                case "petting":return "轻轻摸摸头，看看伙伴的反应。";
                case "poked":return "戳一戳伙伴，逗它回应你。";
                case "feeding":return "请伙伴吃份零食，吃完会开心一下。";
                case "happy":return "让伙伴做个开心的小表情。";
                case "sleeping":return pet.IsSleeping?"叫醒正在小憩的伙伴。":"让伙伴安静地小憩，再点一次叫醒。";
                case "special":return pet.Skin.SpecialAction+"：看看伙伴的反应。";
                case "special2":return AdditionalInteractions.SpecialName(pet.Skin)+"：看看伙伴的新花样。";
                case "idle":return "回到平静的待机姿态。";
                case "running-right":return "向右奔跑一小段距离；锁定位置时原地演示。";
                case "running-left":return "向左奔跑一小段距离；锁定位置时原地演示。";
                case "waving":return "挥挥手，和你打个招呼。";
                case "jumping":return "轻快地跳一下。";
                case "failed":return "露出委屈的小表情。";
                case "waiting":return "安静地等待一会儿。";
                case "running":return "认真想一想。";
                default:return "认真检查一下成果。";
            }
        }

        void RefreshAvailability()
        {
            bool enabled=!pet.IsUpdating;
            foreach(var button in actionButtons)button.IsEnabled=enabled;
            if(!enabled)hint.Text="当前对话或更新结束后，就能继续互动。";
        }

        void PlaceNearPet()
        {
            var mine=new WindowInteropHelper(this).Handle;var owner=new WindowInteropHelper(pet).Handle;
            if(mine!=IntPtr.Zero && owner!=IntPtr.Zero)
            {
                Rect bounds=DesktopInterop.Bounds(mine),area=DesktopInterop.WorkArea(invocationPoint);
                DesktopInterop.Move(mine,PetMath.ClampPosition(invocationPoint.X-bounds.Width/2,invocationPoint.Y-bounds.Height*220/490,bounds.Width,bounds.Height,area));
            }
        }
        static Brush Glass(bool hub)
        {
            var glass=new LinearGradientBrush();glass.StartPoint=new Point(.15,0);glass.EndPoint=new Point(.85,1);
            glass.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hub?"#ECFFFFFF":"#D8FFFFFF"),0));
            glass.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hub?"#CDEEF5FF":"#B0E9F2FF"),.48));
            glass.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hub?"#DEF9FCFF":"#C4FAFDFF"),1));
            glass.Freeze();return glass;
        }
    }
}
