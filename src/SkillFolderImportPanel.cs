using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed class SkillFolderImportPanel : UserControl, IDisposable
    {
        readonly ManagedSkillLibrary library;
        readonly TextBox path=Ui.Field(),preview=Ui.Field("",true);
        readonly TextBlock status=Ui.Text("选择含 SKILL.md 的技能文件夹，先预览再导入。",12,Ui.Muted);
        readonly Button install;
        string selectedPath,selectedBody,selectedHash;bool busy,disposed;
        internal bool IsBusy {get{return busy;}}
        internal event Action Finished;
        internal SkillFolderImportPanel(ManagedSkillLibrary library)
        {
            this.library=library;Name="SkillFolderImportPanel";
            var body=new StackPanel();body.Children.Add(Ui.Text("技能文件夹",12,Ui.Muted));path.Name="SkillFolderPath";path.Margin=new Thickness(0,6,0,12);path.ToolTip="可粘贴本机绝对路径";body.Children.Add(path);
            var actions=new WrapPanel();var choose=Ui.Button("选择文件夹");choose.Name="ChooseLocalSkillFolder";choose.Margin=new Thickness(0,0,8,8);choose.Click+=delegate{using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description="选择包含 SKILL.md 的技能文件夹",ShowNewFolderButton=false}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){path.Text=dialog.SelectedPath;Preview();}}};actions.Children.Add(choose);
            var inspect=Ui.Button("预览技能");inspect.Name="PreviewLocalSkill";inspect.Margin=new Thickness(0,0,0,8);inspect.Click+=delegate{Preview();};actions.Children.Add(inspect);body.Children.Add(actions);
            status.Name="SkillFolderStatus";status.Margin=new Thickness(0,3,0,12);body.Children.Add(status);
            preview.Name="SkillFolderPreview";preview.IsReadOnly=true;preview.Height=250;preview.FontSize=12;preview.ContextMenu=ChatUi.EditorMenu(preview,true);preview.Margin=new Thickness(0,0,0,12);body.Children.Add(preview);
            install=Ui.Button("导入技能");install.Name="ImportLocalSkill";install.HorizontalAlignment=HorizontalAlignment.Left;install.Background=ThemeManager.ActionSurface;install.Foreground=System.Windows.Media.Brushes.White;install.IsEnabled=false;install.Click+=async delegate{await Install();};body.Children.Add(install);
            var note=Ui.Text("复制到本机技能库，原文件夹保留。导入后默认启用，下次工作请求可用。",11,Ui.Muted);note.Margin=new Thickness(0,10,0,0);body.Children.Add(note);
            path.TextChanged+=delegate{selectedPath=null;install.IsEnabled=false;};Content=Ui.Scroll(body);
        }
        static bool Expected(Exception error){return error is IOException||error is UnauthorizedAccessException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException;}
        void Preview()
        {
            if(busy)return;
            try{string folder=Path.GetFullPath(path.Text.Trim());var source=AgentInstructions.Read(Path.Combine(folder,"SKILL.md"));var info=AgentSkills.ParseInfo(source.Content,folder);selectedBody=source.Content;selectedHash=source.Hash;preview.Text=selectedBody;selectedPath=folder;status.Text=info.Name+" · "+info.Description;install.IsEnabled=true;}
            catch(Exception error){if(!Expected(error))throw;status.Text=error.Message;selectedPath=null;install.IsEnabled=false;}
        }
        async Task Install()
        {
            if(busy||selectedPath==null)return;string folder=selectedPath,hash=selectedHash;busy=true;install.IsEnabled=false;path.IsEnabled=false;
            try{var item=await Task.Run(delegate{if(AgentInstructions.Read(Path.Combine(folder,"SKILL.md")).Hash!=hash)throw new InvalidOperationException("技能正文已变化，请重新预览。");return library.Import(folder,null,hash);});if(!disposed)status.Text="已导入 "+item.Name+"，从下一次工作请求起可用。";}
            catch(Exception error){if(!Expected(error))throw;if(!disposed){status.Text=error.Message;install.IsEnabled=true;}}
            finally{busy=false;if(!disposed){path.IsEnabled=true;var finished=Finished;if(finished!=null)finished();}}
        }
        public void Dispose(){disposed=true;}
    }
}
