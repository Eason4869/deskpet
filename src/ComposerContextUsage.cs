using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Dafeiyu
{
    internal sealed partial class ConversationComposer
    {
        Button contextPreview;Path contextArc;ToolTip contextTip;
        FrameworkElement CreateContextControls()
        {
            var panel=new Grid{HorizontalAlignment=HorizontalAlignment.Right,MinWidth=0};panel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});panel.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=0});
            var ring=new Grid{Width=14,Height=14};ring.Children.Add(new Ellipse{Stroke=Ui.Brush("#DADDE0"),StrokeThickness=2});contextArc=new Path{Stroke=Ui.Brush("#747B83"),StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};ring.Children.Add(contextArc);
            contextPreview=Ui.Button("");contextPreview.Name="ContextUsagePreview";contextPreview.Content=ring;contextPreview.Width=28;contextPreview.Height=32;contextPreview.MinWidth=0;contextPreview.MinHeight=0;contextPreview.Padding=new Thickness(0);contextPreview.Margin=new Thickness(0);contextPreview.Background=Brushes.Transparent;contextPreview.BorderBrush=Brushes.Transparent;contextPreview.VerticalAlignment=VerticalAlignment.Center;
            contextTip=new ToolTip{Template=hintTemplate,Foreground=Brushes.White,Placement=System.Windows.Controls.Primitives.PlacementMode.Top,HorizontalContentAlignment=HorizontalAlignment.Center};contextPreview.RenderTransform=new TranslateTransform(0,4);contextPreview.ToolTip=contextTip;ToolTipService.SetInitialShowDelay(contextPreview,200);contextPreview.ToolTipOpening+=delegate{RefreshContextUsage();};contextPreview.Click+=delegate{RefreshContextUsage();contextTip.PlacementTarget=contextPreview;contextTip.IsOpen=!contextTip.IsOpen;};
            host.PreviewMouseDown+=ContextHostMouseDown;host.PreviewKeyDown+=ContextHostKeyDown;
            host.Deactivated+=ContextHostDeactivated;host.IsVisibleChanged+=ContextHostVisibilityChanged;
            Unloaded+=delegate{CloseContextPreview();};
            panel.Children.Add(contextPreview);Grid.SetColumn(modelPicker,1);panel.Children.Add(modelPicker);return panel;
        }
        void CloseContextPreview(){if(contextTip!=null)contextTip.IsOpen=false;}
        void ContextHostMouseDown(object sender,MouseButtonEventArgs args)
        {
            if(contextTip==null||!contextTip.IsOpen)return;
            for(DependencyObject source=args.OriginalSource as DependencyObject;source!=null;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source))
                if(source==contextPreview||source==contextTip)return;
            CloseContextPreview();
        }
        void ContextHostKeyDown(object sender,KeyEventArgs args){if(args.Key==Key.Escape&&contextTip!=null&&contextTip.IsOpen){CloseContextPreview();args.Handled=true;}}
        void ContextHostDeactivated(object sender,EventArgs args){CloseContextPreview();}
        void ContextHostVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){if(!host.IsVisible)CloseContextPreview();}
        void DetachContextPreview()
        {
            CloseContextPreview();host.PreviewMouseDown-=ContextHostMouseDown;host.PreviewKeyDown-=ContextHostKeyDown;
            host.Deactivated-=ContextHostDeactivated;host.IsVisibleChanged-=ContextHostVisibilityChanged;
        }
        static string CompactContextTokens(int tokens){return tokens>=1000000?(tokens/1000000d).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"m":tokens>=1000?(tokens/1000d).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"k":Math.Max(0,tokens).ToString();}
        void RefreshContextUsage()
        {
            if(contextPreview==null)return;contextPreview.Visibility=pet.Settings.ShowContextUsage?Visibility.Visible:Visibility.Collapsed;if(!pet.Settings.ShowContextUsage){contextTip.IsOpen=false;return;}var model=pet.Settings.Text;int capacity=model.ContextWindowTokens;var usage=session.CurrentContextUsage;bool measured=usage!=null&&usage.Model==model.Model&&usage.Endpoint==model.BaseUrl;int tokens;
            if(measured)tokens=usage.Tokens;else{var messages=session.History.ForRequest(pet.Settings.SystemPrompt??"",input.Text??"");tokens=AgentContextBudget.EstimateTokens(messages.Select(m=>(object)new{role=m.Role,content=ChatAttachment.Content(m,model.Protocol=="anthropic")}));}
            double ratio=capacity>0?Math.Min(1,(double)tokens/capacity):0;double angle=Math.Max(.01,Math.Min(.999,ratio))*2*Math.PI;var figure=new PathFigure{StartPoint=new Point(7,1)};figure.Segments.Add(new ArcSegment(new Point(7+6*Math.Sin(angle),7-6*Math.Cos(angle)),new Size(6,6),0,ratio>.5,SweepDirection.Clockwise,true));contextArc.Data=new PathGeometry(new[]{figure});contextArc.Visibility=capacity>0?Visibility.Visible:Visibility.Collapsed;
            int used=(int)Math.Round(Math.Max(0,Math.Min(100,100*ratio)));string usedTokens=CompactContextTokens(tokens),totalTokens=CompactContextTokens(capacity);
            Func<string> text=delegate{
            string summary=TextResources.Get("composerContextUsage.refreshContextUsage.text.alternativeB")+(capacity>0?TextResources.Format("composerContextUsage.refreshContextUsage.text",used,(100-used),usedTokens,totalTokens):TextResources.Format("composerContextUsage.refreshContextUsage.text.alternativeA",usedTokens));
            if(measured&&usage.Status=="compacting")summary+=TextResources.Get("composerContextUsage.refreshContextUsage.text.alternativeC");
                return summary;
            };
            contextTip.Content=LocalizedUi.With(new TextBlock{Text=text(),TextAlignment=TextAlignment.Center,Foreground=Brushes.White,FontSize=12},"Text",text);LocalizedUi.Automation(contextPreview,text);
        }
    }
}
