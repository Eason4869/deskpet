using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed class AgentFactsWindow:Window
    {
        internal AgentFactsWindow(AgentTaskState task,Window owner)
        {
            Ui.Window(this,"项目事实",640,580);if(owner!=null){Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}Name="AgentFactsWindow";var store=new AgentTaskMemory(task.Workspace);var body=new StackPanel{Margin=new Thickness(24)};body.Children.Add(Ui.Text("仅手动保存到当前项目",20));var help=Ui.Text("这些事实会在当前项目的后续工作请求中引用，可编辑或删除。请只保存已核对的信息，不要填写密钥。不会自动生成事实或跨项目共享。",12,Ui.Muted);help.TextWrapping=TextWrapping.Wrap;body.Children.Add(help);var list=new ListBox{Name="AgentProjectFacts",MaxHeight=180,DisplayMemberPath=""};body.Children.Add(list);var input=Ui.Field("",true);input.Name="AgentProjectFactText";input.MaxLength=2000;input.MinHeight=100;body.Children.Add(input);var notice=Ui.Text("",11,Ui.Muted);notice.TextWrapping=TextWrapping.Wrap;var actions=new WrapPanel();AgentProjectFact selected=null;
            Action refresh=delegate{list.ItemsSource=store.List();};list.SelectionChanged+=delegate{selected=list.SelectedItem as AgentProjectFact;input.Text=selected==null?"":selected.Text;notice.Text=selected==null?"":selected.TaskId+" · "+selected.UpdatedUtc.ToLocalTime().ToString("g");};
            var add=Ui.Button("新增");add.Click+=delegate{list.SelectedItem=null;selected=null;input.Text="";};actions.Children.Add(add);var save=Ui.Button("保存事实",true);save.Name="SaveAgentProjectFact";save.Click+=delegate{try{store.Save(new AgentProjectFact{Id=selected==null?Guid.NewGuid().ToString("N"):selected.Id,Text=input.Text.Trim(),TaskId=task.Id,SourceFile=selected==null?"":selected.SourceFile});refresh();input.Text="";notice.Text="已保存到当前项目；下次任务生效。";}catch(Exception){notice.Text="未保存，请核对正文和数据目录。";}};actions.Children.Add(save);var remove=Ui.Button("删除选中事实");remove.Name="DeleteAgentProjectFact";remove.Click+=delegate{if(selected==null)return;try{store.Delete(selected.Id);refresh();notice.Text="已删除，后续任务不再引用。";}catch(Exception){notice.Text="删除未完成，原数据已保留。";}};actions.Children.Add(remove);body.Children.Add(actions);body.Children.Add(notice);Content=Ui.Scroll(body);refresh();
        }
    }
}
