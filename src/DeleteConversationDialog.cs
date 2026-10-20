using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed class DeleteConversationDialog : Window
    {
        public DeleteConversationDialog(string conversationName, string failure = null,bool companion=false)
        {
            Ui.Window(this, companion?"删除伙伴":"删除聊天", 540, 184); Name = companion?"RemoveCompanionDialog":"DeleteConversationDialog"; SizeToContent = SizeToContent.Height;
            var root = new Grid { Margin = new Thickness(24, 24, 24, 22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = Ui.Text(failure == null ? (companion?"要删除这位伙伴吗？":"要删除聊天吗？") : "删除未完成", 21, Ui.Brush("#222426")); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 32, 9); root.Children.Add(title);
            var description = Ui.Text(failure ?? (companion?"将从伙伴列表移除“"+conversationName+"”。聊天记录、人格和音色配置会保留，可通过“恢复已删除伙伴”或重新导入恢复。":"这将永久删除“" + conversationName + "”的聊天历史和任务缓存。此操作无法撤销。"), 14, Ui.Brush("#929397")); description.Name = "DeleteConversationDescription"; description.TextWrapping = TextWrapping.Wrap; description.Margin = new Thickness(0, 0, 8, 14); Grid.SetRow(description, 1); root.Children.Add(description);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = Ui.Button(failure == null ? "取消" : "关闭"); cancel.Name = "CancelDeleteConversation"; cancel.IsCancel = true; cancel.IsDefault = true; cancel.MinWidth = 76; cancel.Height = 38; cancel.Margin = new Thickness(0, 0, 12, 0); cancel.Background = Ui.Brush("#FFFFFF"); cancel.BorderBrush = Ui.Brush("#E9EAEC"); cancel.Click += delegate { DialogResult = false; }; actions.Children.Add(cancel);
            var delete = Ui.Button("删除"); delete.Name = "ConfirmDeleteConversation"; delete.MinWidth = 76; delete.Height = 38; delete.Margin = new Thickness(0); delete.Background = Ui.Brush("#F9E5E5"); delete.BorderBrush = delete.Background; delete.Foreground = Ui.Brush("#E34242"); delete.Click += delegate { DialogResult = true; }; delete.Visibility = failure == null ? Visibility.Visible : Visibility.Collapsed; actions.Children.Add(delete);
            Grid.SetRow(actions, 2); root.Children.Add(actions); DialogUi.Apply(this, root); Loaded += delegate { cancel.Focus(); };
        }
    }
}
