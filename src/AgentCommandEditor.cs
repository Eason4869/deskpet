using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    // A small native editor inside existing work settings; recipes stay in the
    // user's settings, never loaded as authority from an untrusted repository.
    internal sealed class AgentCommandEditor : StackPanel
    {
        readonly ListBox list=new ListBox{MinHeight=60,MaxHeight=140,Margin=new Thickness(0,4,0,8)};
        readonly TextBox id=Ui.Field(),executable=Ui.Field(),arguments=Ui.Field("",true);
        readonly List<AgentCommand> recipes=new List<AgentCommand>();readonly Func<string> workspace;readonly TextBlock notice=Ui.Text("",11,Ui.Muted);
        string draftBaseline="";AgentCommand editing;bool loading;readonly TextBlock bound=Ui.Text("",11,Ui.Muted);
        string DraftText{get{return id.Text+"\0"+executable.Text+"\0"+arguments.Text;}}
        internal bool HasDraft{get{return DraftText!=draftBaseline;}}
        internal bool CommitDraft(){return !HasDraft || SaveRecipe();}
        internal string Json{get{return new JavaScriptSerializer{MaxJsonLength=65536}.Serialize(recipes);}}
        internal AgentCommandEditor(Func<string> project)
        {
            workspace=project;Name="AgentCommandEditor";var create=AgentUi.Nav("＋ 新增命令","NewCommandRecipe");create.Click+=delegate{if(HasDraft){notice.Text="请先保存此命令，或重置草稿后再新增。";return;}loading=true;list.SelectedItem=null;loading=false;editing=null;LoadDraft(null);};Children.Add(create);Children.Add(list);Children.Add(bound);
            id.Name="CommandRecipeId";executable.Name="CommandRecipeExecutable";arguments.Name="CommandRecipeArguments";arguments.MinHeight=65;arguments.MaxHeight=120;
            Children.Add(Ui.Text("命令名称",12,Ui.Muted));Children.Add(id);
            Children.Add(Ui.Text("程序完整路径",12,Ui.Muted));Children.Add(executable);
            var browse=Ui.Button("选择程序…");browse.HorizontalAlignment=HorizontalAlignment.Left;browse.Click+=delegate{var picker=new Microsoft.Win32.OpenFileDialog{Filter="可执行程序 (*.exe)|*.exe",CheckFileExists=true};if(picker.ShowDialog(Window.GetWindow(this))==true)executable.Text=picker.FileName;};Children.Add(browse);
            Children.Add(Ui.Text("参数（每行一个；无需额外加引号）",12,Ui.Muted));Children.Add(arguments);
            var buttons=new WrapPanel{Margin=new Thickness(0,8,0,0)};
            var add=Ui.Button("保存此命令");add.Name="SaveCommandRecipe";add.Click+=delegate{SaveRecipe();};buttons.Children.Add(add);
            var reset=Ui.Button("重置草稿");reset.Name="ResetCommandDraft";reset.Click+=delegate{LoadDraft(editing);};buttons.Children.Add(reset);var remove=Ui.Button("移除此命令");remove.Click+=delegate{var current=list.SelectedItem as AgentCommand;if(current!=null){recipes.Remove(current);editing=null;Refresh();LoadDraft(null);}};buttons.Children.Add(remove);Children.Add(buttons);Children.Add(notice);
            list.SelectionChanged+=delegate{if(loading)return;var current=list.SelectedItem as AgentCommand;if(current==editing)return;if(HasDraft){loading=true;list.SelectedItem=editing;loading=false;notice.Text="请先保存此命令，或重置草稿后切换。";return;}editing=current;LoadDraft(current);};
        }
        internal void Load(string json)
        {
            editing=null;LoadDraft(null);notice.Text="";recipes.Clear();try{recipes.AddRange(new JavaScriptSerializer{MaxJsonLength=65536}.Deserialize<AgentCommand[]>(String.IsNullOrWhiteSpace(json)?"[]":json)??new AgentCommand[0]);}catch(ArgumentException){notice.Text="命令配置无法读取，请重新配置后保存。";}catch(InvalidOperationException){notice.Text="命令配置无法读取，请重新配置后保存。";}Refresh();
        }
        void Refresh(){loading=true;list.ItemsSource=null;list.ItemsSource=recipes;list.DisplayMemberPath="Id";list.SelectedItem=editing;loading=false;list.Visibility=recipes.Count==0?Visibility.Collapsed:Visibility.Visible;}
        void LoadDraft(AgentCommand recipe){id.Text=recipe==null?"":recipe.Id;executable.Text=recipe==null?"":recipe.Executable;arguments.Text=recipe==null?"":String.Join(Environment.NewLine,recipe.Arguments??new string[0]);draftBaseline=DraftText;bound.Text=recipe==null?"新命令将绑定当前项目。":"绑定项目："+recipe.Workspace;bound.ToolTip=bound.Text;}
        bool SaveRecipe()
        {
            string name=id.Text.Trim(),exe=executable.Text.Trim(),root=(workspace()??"").Trim();
            if(name.Length==0 || name.Length>80 || !Path.IsPathRooted(exe) || !File.Exists(exe) || !Path.IsPathRooted(root) || !Directory.Exists(root)){notice.Text="请填写命令名称、已存在的程序完整路径，并选择已存在的项目目录。";return false;}
            var argumentValues=arguments.Text.Split(new[]{"\r\n","\n"},StringSplitOptions.None);
            if(argumentValues.Length>100 || argumentValues.Any(a=>a.Length>8000 || a.IndexOf('\0')>=0)){notice.Text="参数数量或长度超过限制。";return false;}
            var old=editing;if(recipes.Any(r=>r!=old && r.Id==name)){notice.Text="命令名称已存在，请使用不同名称。";return false;}
            if(old==null && recipes.Count>=32){notice.Text="最多配置 32 条命令。";return false;}
            var recipe=new AgentCommand{Id=name,Executable=Path.GetFullPath(exe),Workspace=old==null?Path.GetFullPath(root):old.Workspace,Arguments=arguments.Text.Split(new[]{"\r\n","\n"},StringSplitOptions.None)};
            if(recipe.Arguments.Length==1 && recipe.Arguments[0].Length==0)recipe.Arguments=new string[0];
            if(old!=null)recipes[recipes.IndexOf(old)]=recipe;else recipes.Add(recipe);editing=recipe;Refresh();LoadDraft(recipe);notice.Text="命令已加入草稿；点击保存工作设置后生效。";return true;
        }
    }
}
