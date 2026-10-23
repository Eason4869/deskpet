using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class PreviewWorkspace:DockPanel,IDisposable
    {
        sealed class Tab{internal string Key;internal FrameworkElement View;internal Border Header;internal TextBlock Title;internal FrameworkElement Glyph;internal Button Close;internal bool Pinned;}
        readonly DockPanel bar=new DockPanel{Background=Ui.Brush("#F8F9FB"),Margin=new Thickness(0,0,0,4)};
        double toolbarSpace=150;
        readonly List<Tab> tabs=new List<Tab>();readonly StackPanel headers=new StackPanel{Orientation=Orientation.Horizontal};readonly Grid body=new Grid();readonly Action empty,openTerminal;readonly Action<string> warning;bool disposed;Tab lastTool;
        internal int Count{get{return tabs.Count;}}
        internal int ToolCount{get{return tabs.Count(t=>!t.Pinned);}}
        internal bool HasConversation{get{return tabs.Any(t=>t.Pinned);}}
        internal FrameworkElement Selected{get;private set;}
        internal PreviewWorkspace(Action empty,Action openTerminal,Action<string> warning=null)
        {
            this.empty=empty;this.openTerminal=openTerminal;this.warning=warning;Name="PreviewWorkspace";Background=Ui.Brush("#F8F9FB");
            var add=UiIcons.Button("add",TextResources.Get("previewWorkspace.previewWorkspace.uiIcons"));add.Name="NewPreviewTab";add.Width=add.Height=add.MinHeight=36;add.Padding=new Thickness(9);add.ToolTip=TextResources.Get("previewWorkspace.previewWorkspace.addToolTip");add.Margin=new Thickness(3,0,4,4);add.VerticalAlignment=VerticalAlignment.Top;add.Click+=delegate{openTerminal();};
            var rail=new StackPanel{Orientation=Orientation.Horizontal};rail.Children.Add(headers);rail.Children.Add(add);
            bar.Children.Add(new ScrollViewer{Name="PreviewTabScroll",Content=rail,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(1,1,1,1)});DockPanel.SetDock(bar,Dock.Top);Children.Add(bar);Children.Add(body);SizeChanged+=delegate{FitTabWidths();};

        }
        internal void SetToolbarInset(double width){toolbarSpace=Math.Max(0,width);bar.Margin=new Thickness(0,0,toolbarSpace,5);FitTabWidths();}
        void FitTabWidths(){double available=Math.Max(0,ActualWidth-toolbarSpace-48-tabs.Sum(t=>t.Header.Margin.Left+t.Header.Margin.Right));double width=Math.Max(120,Math.Min(230,available/Math.Max(1,tabs.Count)));foreach(var tab in tabs)tab.Header.Width=width;}
        internal void OpenFile(string path)
        {
            string key=Path.GetFullPath(path);var found=tabs.FirstOrDefault(t=>String.Equals(t.Key,key,StringComparison.OrdinalIgnoreCase));if(found!=null){Select(found);return;}
            FilePreviewPane pane=null;pane=new FilePreviewPane(key,()=>Close(pane));Add(key,Path.GetFileName(path),String.Equals(Path.GetExtension(path),".html",StringComparison.OrdinalIgnoreCase)||String.Equals(Path.GetExtension(path),".htm",StringComparison.OrdinalIgnoreCase)?"globe":"file",pane);
        }
        internal void AddTerminal(TerminalPane pane,int number){Add("terminal:"+Guid.NewGuid().ToString("N"),TextResources.Format("previewWorkspace.addTerminal.template",number),"terminal",pane);}
        internal void AddPage(string title,string icon,FrameworkElement view){Add("page:"+Guid.NewGuid().ToString("N"),title,icon,view);}
        internal void Replace(FrameworkElement previous,string title,string icon,FrameworkElement view){var old=tabs.FirstOrDefault(t=>ReferenceEquals(t.View,previous));if(old==null){Release(view);return;}AddPage(title,icon,view);Close(previous);}
        internal void AttachConversation(FrameworkElement view,string title)
        {
            if(view==null)throw new ArgumentNullException("view");if(disposed)throw new ObjectDisposedException("PreviewWorkspace");
            var pinned=tabs.FirstOrDefault(t=>t.Pinned);
            if(pinned!=null&&ReferenceEquals(pinned.View,view)){UpdateConversationTitle(title);return;}
            if(view.Parent!=null)throw new InvalidOperationException(TextResources.Get("previewWorkspace.attachConversation.text"));
            var selected=tabs.FirstOrDefault(t=>ReferenceEquals(t.View,Selected));
            if(pinned==null){pinned=CreateTab("conversation",ConversationTitle(title),"chat",view,true);tabs.Insert(0,pinned);headers.Children.Insert(0,pinned.Header);}
            else{body.Children.Remove(pinned.View);pinned.View.Visibility=Visibility.Visible;pinned.View=view;UpdateConversationTitle(title);}
            body.Children.Add(view);Select(selected??pinned);
        }
        internal FrameworkElement DetachConversation()
        {
            var pinned=tabs.FirstOrDefault(t=>t.Pinned);if(pinned==null)return null;
            bool selected=ReferenceEquals(Selected,pinned.View);tabs.Remove(pinned);headers.Children.Remove(pinned.Header);body.Children.Remove(pinned.View);pinned.View.Visibility=Visibility.Visible;
            if(selected){if(tabs.Count>0)Select(lastTool!=null&&tabs.Contains(lastTool)?lastTool:tabs[0]);else Selected=null;}
            // The caller is moving its existing conversation back into the shell.
            // Removing the pinned tab is neither a close nor an empty-workspace action.
            return pinned.View;
        }
        internal void UpdateConversationTitle(string title)
        {
            var pinned=tabs.FirstOrDefault(t=>t.Pinned);if(pinned==null)return;string text=ConversationTitle(title);pinned.Title.Text=text;pinned.Title.ToolTip=text;pinned.Header.ToolTip=text;
        }
        internal void SelectConversation(){var pinned=tabs.FirstOrDefault(t=>t.Pinned);if(pinned!=null)Select(pinned);}
        static string ConversationTitle(string title){return String.IsNullOrWhiteSpace(title)?TextResources.Get("previewWorkspace.conversationTitle.text"):title;}
        void Add(string key,string title,string icon,FrameworkElement view)
        {
            if(disposed){Release(view);return;}
            var tab=CreateTab(key,title,icon,view,false);headers.Children.Add(tab.Header);tabs.Add(tab);body.Children.Add(view);Select(tab);
        }
        Tab CreateTab(string key,string title,string icon,FrameworkElement view,bool pinned)
        {
            var tab=new Tab{Key=key,View=view,Pinned=pinned};var row=new DockPanel();
            if(!pinned){var close=UiIcons.Button("close",TextResources.Format("previewWorkspace.createTab.uiIcons",title));close.Name="ClosePreviewTab";tab.Close=close;close.Width=close.Height=24;close.MinHeight=24;close.Padding=new Thickness(4);close.ToolTip=TextResources.Format("previewWorkspace.createTab.closeToolTipTitle",title);close.Margin=new Thickness(8,0,0,0);close.VerticalAlignment=VerticalAlignment.Center;close.Click+=delegate{Close(view);};DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);}
            var glyph=UiIcons.Image(icon,AgentUi.Muted);glyph.Width=glyph.Height=16;glyph.VerticalAlignment=VerticalAlignment.Center;glyph.RenderTransform=new TranslateTransform(0,-.5);glyph.Margin=new Thickness(0,0,7,0);DockPanel.SetDock(glyph,Dock.Left);row.Children.Add(glyph);var label=new TextBlock{Text=title,FontSize=13,Foreground=AgentUi.Ink,Height=18,LineHeight=18,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};label.VerticalAlignment=VerticalAlignment.Center;label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=165;row.Children.Add(label);
            tab.Glyph=glyph;tab.Title=label;if(pinned){label.Name="ConversationPreviewTitle";label.ToolTip=title;}
            tab.Header=new Border{Name=pinned?"ConversationPreviewTab":"PreviewTab",Tag=key,Child=row,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),Height=36,Width=230,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(12,4,pinned?12:7,4),Margin=new Thickness(2,0,4,4),Cursor=System.Windows.Input.Cursors.Hand,Focusable=true,ToolTip=pinned||key.StartsWith("terminal:")||key.StartsWith("page:")?title:key};tab.Header.MouseLeftButtonDown+=delegate{Select(tab);};tab.Header.MouseEnter+=delegate{PaintTab(tab);};tab.Header.MouseLeave+=delegate{PaintTab(tab);};tab.Header.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Space){Select(tab);e.Handled=true;}};return tab;
        }
        void Select(Tab selected){if(!selected.Pinned)lastTool=selected;Selected=selected.View;body.Background=selected.Pinned?ThemeManager.Surface:Ui.Brush("#F8F9FB");foreach(var tab in tabs){tab.View.Visibility=ReferenceEquals(tab,selected)?Visibility.Visible:Visibility.Collapsed;PaintTab(tab);}FitTabWidths();selected.Header.BringIntoView();}
        void PaintTab(Tab tab)
        {
            bool active=ReferenceEquals(tab.View,Selected),hover=tab.Header.IsMouseOver;
            tab.Header.Background=active?ThemeManager.Surface:hover?Ui.Brush("#F6F6F7"):Brushes.Transparent;tab.Header.BorderBrush=active?Ui.Brush("#DEDFE2"):Brushes.Transparent;
            tab.Header.Effect=active?new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=3,ShadowDepth=1,Opacity=.06,Color=Colors.Black}:null;
            tab.Title.Foreground=active?Ui.Brush("#25282D"):Ui.Brush("#85898F");tab.Glyph.Opacity=active?1:.65;if(tab.Close!=null)tab.Close.Opacity=active||hover?.85:.55;
        }

        internal void Close(FrameworkElement view)
        {
            var tab=tabs.FirstOrDefault(t=>ReferenceEquals(t.View,view));if(tab==null||tab.Pinned)return;int index=tabs.IndexOf(tab);bool selected=ReferenceEquals(Selected,view);tabs.Remove(tab);headers.Children.Remove(tab.Header);body.Children.Remove(view);Release(view);FitTabWidths();if(tabs.Count==0){Selected=null;empty();}else if(selected)Select(tabs[Math.Min(index,tabs.Count-1)]);
        }
        void Release(FrameworkElement view){var release=view as IDisposable;if(release==null)return;try{release.Dispose();}catch(Exception error){System.Diagnostics.Trace.WriteLine("Preview cleanup: "+error.GetType().Name);if(warning!=null)try{warning(TextResources.Get("previewWorkspace.release.warning"));}catch(Exception notification){System.Diagnostics.Trace.WriteLine("Preview cleanup notification: "+notification.GetType().Name);}}}
        public void Dispose(){if(disposed)return;disposed=true;foreach(var tab in tabs.ToArray()){if(tab.Pinned)tab.View.Visibility=Visibility.Visible;else Release(tab.View);}tabs.Clear();headers.Children.Clear();body.Children.Clear();Selected=null;lastTool=null;}
    }
}
