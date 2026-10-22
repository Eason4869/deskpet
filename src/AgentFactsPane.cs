using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    // Project-scoped, explicitly saved memory. This is a normal workspace tab.
    internal sealed class AgentFactsPane:ScrollViewer
    {
        readonly AgentTaskMemory store;readonly string taskId;
        readonly StackPanel facts=new StackPanel();readonly TextBox input=Ui.Field("",true);
        readonly TextBlock notice=Ui.Text("",11,Ui.Muted),counter=Ui.Text("0 / 2000",11,Ui.Muted),editorTitle=Ui.Text("新增事实",13);
        readonly Button save=Ui.Button("保存事实",true),remove=Ui.Button("删除选中事实");
        readonly Border empty;AgentProjectFact selected;bool readable;
        internal AgentFactsPane(AgentTaskState task)
        {
            if(task==null)throw new ArgumentNullException("task");store=new AgentTaskMemory(task.Workspace);taskId=task.Id;Name="AgentFactsPane";
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto;HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;Background=ThemeManager.Surface;
            var body=new StackPanel{Margin=new Thickness(22,18,22,22)};Content=body;
            var title=Ui.Text("项目事实",20);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            var path=Ui.Text(task.Workspace,11,Ui.Muted);path.Margin=new Thickness(0,2,0,10);body.Children.Add(path);
            var help=Ui.Text("保存已核对的项目约定，供这个项目的后续任务参考。仅手动保存，可随时编辑或删除；请勿填写密钥。",12,Ui.Muted);help.Margin=new Thickness(0,0,0,16);body.Children.Add(help);
            var emptyBody=new StackPanel{Margin=new Thickness(16)};emptyBody.Children.Add(Ui.Text("还没有保存项目事实",14));emptyBody.Children.Add(Ui.Text("例如：源码位于 src，使用 Build.ps1 构建。\n在下方填写一条确认过的信息即可开始。",12,Ui.Muted));
            empty=new Border{Name="AgentFactsEmpty",Child=emptyBody,CornerRadius=new CornerRadius(12),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Background=Ui.Brush("#FAFBFD"),Margin=new Thickness(0,0,0,16)};body.Children.Add(empty);
            var list=Ui.Scroll(facts);list.Name="AgentProjectFacts";list.MaxHeight=230;body.Children.Add(list);
            var heading=new DockPanel{Margin=new Thickness(0,12,0,6)};counter.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(counter,Dock.Right);heading.Children.Add(counter);heading.Children.Add(editorTitle);body.Children.Add(heading);
            input.Name="AgentProjectFactText";input.MaxLength=2000;input.MinHeight=112;input.MaxHeight=190;input.VerticalContentAlignment=VerticalAlignment.Top;input.ContextMenu=ChatUi.EditorMenu(input,false);input.TextChanged+=delegate{RefreshActions();};body.Children.Add(input);
            var actions=new WrapPanel{Margin=new Thickness(0,12,0,8)};var add=Ui.Button("新增事实");add.Name="AddAgentProjectFact";add.Click+=delegate{Select(null);};
            save.Name="SaveAgentProjectFact";save.Click+=delegate{Save();};actions.Children.Add(save);actions.Children.Add(add);
            remove.Name="DeleteAgentProjectFact";remove.Click+=delegate{Delete();};actions.Children.Add(remove);body.Children.Add(actions);notice.Name="AgentFactsNotice";body.Children.Add(notice);Reload();
        }
        void Reload()
        {
            try
            {
                var items=store.List();readable=true;facts.Children.Clear();empty.Visibility=items.Count==0?Visibility.Visible:Visibility.Collapsed;
                foreach(var fact in items)
                {
                    var row=Ui.Button("");row.Name="AgentProjectFactRow";row.Tag=fact.Id;row.HorizontalContentAlignment=HorizontalAlignment.Stretch;row.Padding=new Thickness(12,9,12,9);row.Margin=new Thickness(0,0,0,7);
                    row.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#357FF5'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#357FF5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
                    var text=Ui.Text(fact.Text,12);text.MaxHeight=80;row.Content=text;row.Background=selected!=null&&selected.Id==fact.Id?ThemeManager.Brush("#EDF4FF","#293B57"):ThemeManager.Surface;row.Click+=delegate{Select(fact);};facts.Children.Add(row);
                }
            }
            catch(Exception error){if(!StorageError(error))throw;readable=false;notice.Text="项目事实暂时无法读取，原数据已保留。请检查项目目录。";}
            RefreshActions();
        }
        void Select(AgentProjectFact fact){selected=fact==null?null:fact.Copy();input.Text=selected==null?"":selected.Text;notice.Text=selected==null?"": "上次保存于 "+selected.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");Reload();input.Focus();}
        void RefreshActions(){counter.Text=input.Text.Length+" / 2000";editorTitle.Text=selected==null?"新增事实":"编辑事实";save.IsEnabled=readable&&!String.IsNullOrWhiteSpace(input.Text);remove.IsEnabled=readable&&selected!=null;}
        void Save()
        {
            try
            {
                var fact=new AgentProjectFact{Id=selected==null?Guid.NewGuid().ToString("N"):selected.Id,Text=input.Text.Trim(),TaskId=selected==null?taskId:selected.TaskId,SourceFile=selected==null?"":selected.SourceFile};store.Save(fact);selected=fact.Copy();input.Text=fact.Text;Reload();notice.Text="已保存到当前项目，将在后续任务中引用。";
            }
            catch(Exception error){if(!StorageError(error))throw;notice.Text="未保存，请核对正文长度和项目目录权限。";}
        }
        void Delete()
        {
            if(selected==null)return;
            try{store.Delete(selected.Id);selected=null;input.Clear();Reload();notice.Text="已删除，后续任务不再引用。";}
            catch(Exception error){if(!StorageError(error))throw;notice.Text="删除未完成，原数据已保留。";}
        }
        static bool StorageError(Exception error){return error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is CryptographicException;}
    }
}
