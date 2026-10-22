using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class ShortcutHelpDialog : Window
    {
        readonly List<ShellCommand> commands;
        readonly StackPanel groups;
        readonly TextBox search;
        readonly ScrollViewer scroll;
        readonly TextBlock placeholder;
        readonly Brush ink=Ui.Brush("#282A2F"),muted=Ui.Brush("#7A7E86"),line=Ui.Brush("#E4E7EC");

        internal ShortcutHelpDialog(Window owner,IEnumerable<ShellCommand> source)
        {
            if(owner==null)throw new ArgumentNullException("owner");
            if(source==null)throw new ArgumentNullException("source");
            commands=source.Where(c=>c!=null && !String.IsNullOrWhiteSpace(c.Title) && !String.IsNullOrWhiteSpace(c.GestureLabel)).ToList();
            Owner=owner;Ui.Window(this,"键盘快捷键",740,680);
            Width=Math.Min(740,Math.Max(300,SystemParameters.WorkArea.Width-40));
            Height=Math.Min(680,Math.Max(300,SystemParameters.WorkArea.Height-40));
            MinWidth=Math.Min(440,Width);MinHeight=Math.Min(400,Height);
            var root=new Grid{Margin=new Thickness(32,28,32,26)};
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition());
            var title=Ui.Text("键盘快捷键",25,ink);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,34,21);root.Children.Add(title);

            var searchRow=new DockPanel{LastChildFill=true};
            var icon=UiIcons.Image("search",muted);icon.Width=icon.Height=17;icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0,0,10,0);DockPanel.SetDock(icon,Dock.Left);searchRow.Children.Add(icon);
            var inputLayer=new Grid();
            search=new TextBox{Name="ShortcutSearch",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),MinHeight=44,FontSize=13,Foreground=ink,VerticalContentAlignment=VerticalAlignment.Center,TabIndex=0};
            System.Windows.Automation.AutomationProperties.SetName(search,"搜索快捷键、操作名称或分组");
            inputLayer.Children.Add(search);placeholder=Ui.Text("搜索快捷键",13,muted);placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.IsHitTestVisible=false;inputLayer.Children.Add(placeholder);searchRow.Children.Add(inputLayer);
            var searchBox=new Border{Child=searchRow,Background=ThemeManager.Surface,BorderBrush=line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(13,0,13,0),Margin=new Thickness(0,0,0,18)};
            Grid.SetRow(searchBox,1);root.Children.Add(searchBox);
            groups=new StackPanel{Margin=new Thickness(0,0,12,0)};
            scroll=new ScrollViewer{Name="ShortcutScroll",Content=groups,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true,TabIndex=1,Padding=new Thickness(0),CanContentScroll=false};
            System.Windows.Automation.AutomationProperties.SetName(scroll,"快捷键列表，可用方向键或翻页键滚动");
            KeyboardNavigation.SetTabNavigation(scroll,KeyboardNavigationMode.None);Grid.SetRow(scroll,2);root.Children.Add(scroll);

            DialogUi.Apply(this,root);
            // The shared chrome supplies the actual close button; make it the modal cancel action.
            var overlay=(Grid)((Border)Content).Child;
            foreach(var button in overlay.Children.OfType<Button>())if(button.Name=="CloseDialog"){button.IsCancel=true;button.TabIndex=2;}
            KeyboardNavigation.SetTabNavigation(root,KeyboardNavigationMode.Continue);
            search.TextChanged+=delegate{placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;RenderGroups();};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};
            Loaded+=delegate{search.Focus();};RenderGroups();
        }

        void RenderGroups()
        {
            groups.Children.Clear();string query=search.Text.Trim();
            var visible=commands.Where(c=>Matches(c,query)).GroupBy(c=>String.IsNullOrWhiteSpace(c.Group)?"其他":c.Group).ToArray();
            if(visible.Length==0)
            {
                var empty=new StackPanel{Margin=new Thickness(0,68,0,0)};
                var title=Ui.Text("没有匹配的快捷键",15,ink);title.Name="ShortcutEmptyState";title.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(title);
                var hint=Ui.Text("试试操作名称、分组或 Ctrl。",12,muted);hint.HorizontalAlignment=HorizontalAlignment.Center;hint.Margin=new Thickness(0,8,0,0);empty.Children.Add(hint);groups.Children.Add(empty);
            }
            else foreach(var group in visible)
            {
                var heading=Ui.Text(group.Key,12,muted);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(2,groups.Children.Count==0?2:24,0,9);groups.Children.Add(heading);
                foreach(var command in group)
                {
                    var row=new Grid{MinHeight=44,Margin=new Thickness(2,0,2,0)};
                    row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                    var title=Ui.Text(command.Title,13,ink);title.Name="ShortcutCommandTitle";title.TextWrapping=TextWrapping.Wrap;title.VerticalAlignment=VerticalAlignment.Center;title.Margin=new Thickness(0,6,18,6);row.Children.Add(title);
                    var keys=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
                    var labels=command.GestureLabel.Split(new[]{'+'},StringSplitOptions.RemoveEmptyEntries).Select(k=>k.Trim()).ToList();if(command.GestureLabel.EndsWith("+",StringComparison.Ordinal))labels.Add("+");
                    foreach(string label in labels)
                    {
                        var key=Ui.Text(label,11,Ui.Brush("#566171"));key.TextAlignment=TextAlignment.Center;key.MinWidth=13;
                        keys.Children.Add(new Border{Child=key,Padding=new Thickness(8,5,8,5),Margin=new Thickness(4,0,0,0),CornerRadius=new CornerRadius(6),Background=Ui.Brush("#F0F2F5"),BorderBrush=Ui.Brush("#E0E3E8"),BorderThickness=new Thickness(1)});
                    }
                    System.Windows.Automation.AutomationProperties.SetName(keys,command.GestureLabel);Grid.SetColumn(keys,1);row.Children.Add(keys);groups.Children.Add(row);
                }
            }
            scroll.ScrollToTop();
        }

        static bool Matches(ShellCommand command,string query)
        {
            if(query.Length==0)return true;
            string text=command.Title+" "+command.Group+" "+command.GestureLabel;
            return query.Split((char[])null,StringSplitOptions.RemoveEmptyEntries).All(term=>text.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0);
        }
    }
}
