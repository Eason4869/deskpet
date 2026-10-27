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
        readonly TextBlock status=LocalizedUi.Text(()=>(TextResources.Get("skillFolderImportPanel.status.text")),12,Ui.Muted);
        readonly Button install;
        string selectedPath,selectedBody,selectedHash;bool busy,disposed;
        internal bool IsBusy {get{return busy;}}
        internal event Action Finished;
        internal SkillFolderImportPanel(ManagedSkillLibrary library)
        {
            this.library=library;Name="SkillFolderImportPanel";
            var body=new StackPanel();body.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.text")),12,Ui.Muted));path.Name="SkillFolderPath";path.Margin=new Thickness(0,6,0,12);LocalizedUi.Set(path,"ToolTip",()=>(TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.pathToolTip")));body.Children.Add(path);
            var actions=new WrapPanel();var choose=LocalizedUi.Button(()=>(TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.text.alternativeA")));choose.Name="ChooseLocalSkillFolder";choose.Margin=new Thickness(0,0,8,8);choose.Click+=delegate{using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.description"),ShowNewFolderButton=false}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){path.Text=dialog.SelectedPath;Preview();}}};actions.Children.Add(choose);
            var inspect=LocalizedUi.Button(()=>(TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.text.alternativeB")));inspect.Name="PreviewLocalSkill";inspect.Margin=new Thickness(0,0,0,8);inspect.Click+=delegate{Preview();};actions.Children.Add(inspect);body.Children.Add(actions);
            status.Name="SkillFolderStatus";status.Margin=new Thickness(0,3,0,12);body.Children.Add(status);
            preview.Name="SkillFolderPreview";preview.IsReadOnly=true;preview.Height=250;preview.FontSize=12;preview.ContextMenu=ChatUi.EditorMenu(preview,true);preview.Margin=new Thickness(0,0,0,12);body.Children.Add(preview);
            install=LocalizedUi.Button(()=>(TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.text.alternativeC")));install.Name="ImportLocalSkill";install.HorizontalAlignment=HorizontalAlignment.Left;install.Background=ThemeManager.ActionSurface;install.Foreground=System.Windows.Media.Brushes.White;install.IsEnabled=false;install.Click+=async delegate{await Install();};body.Children.Add(install);
            var note=LocalizedUi.Text(()=>(TextResources.Get("skillFolderImportPanel.skillFolderImportPanel.text.alternativeD")),11,Ui.Muted);note.Margin=new Thickness(0,10,0,0);body.Children.Add(note);
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
            try{var item=await Task.Run(delegate{if(AgentInstructions.Read(Path.Combine(folder,"SKILL.md")).Hash!=hash)throw new InvalidOperationException(TextResources.Get("skillFolderImportPanel.install.taskRun"));return library.Import(folder,null,hash);});if(!disposed)LocalizedUi.Set(status,"Text",()=>(TextResources.Format("skillFolderImportPanel.install.statusItemName",item.Name)));}
            catch(Exception error){if(!Expected(error))throw;if(!disposed){status.Text=error.Message;install.IsEnabled=true;}}
            finally{busy=false;if(!disposed){path.IsEnabled=true;var finished=Finished;if(finished!=null)finished();}}
        }
        public void Dispose(){disposed=true;}
    }
}
