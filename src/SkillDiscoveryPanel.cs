using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class SkillDiscoveryPanel : UserControl, IDisposable
    {
        sealed class Result {internal string SourceId;internal SkillRepositorySnapshot Snapshot;internal RepositorySkill Skill;internal bool FromZip;}
        readonly ManagedSkillLibrary library;
        readonly SkillRepositoryCatalog catalog;
        readonly SkillRepositoryClient client;
        readonly StackPanel body=new StackPanel(),sourcesPanel=new StackPanel(),results=new StackPanel(),preview=new StackPanel();
        readonly TextBox search=Ui.Field(),repository=Ui.Field(),branch=Ui.Field();
        readonly TextBlock status=Ui.Text("",12,Ui.Muted),count=Ui.Text("",12,Ui.Muted);
        readonly Button refresh,cancel;
        readonly Expander sourceExpander;
        readonly StackPanel zipPicker=new StackPanel{Margin=new Thickness(0,0,0,14)};
        bool archiveMode;
        readonly Dictionary<string,SkillRepositorySnapshot> snapshots=new Dictionary<string,SkillRepositorySnapshot>();
        readonly Dictionary<string,string> errors=new Dictionary<string,string>();
        IList<SkillRepositorySource> sources=new SkillRepositorySource[0];
        SkillArchive archive;Result selected;CancellationTokenSource running;bool disposed;int shown=40;
        internal bool IsBusy {get{return running!=null;}}
        internal SkillDiscoveryPanel(ManagedSkillLibrary library,SkillRepositoryCatalog catalog,SkillRepositoryClient client)
        {
            this.library=library;this.catalog=catalog;this.client=client;
            Name="SkillDiscoveryPanel";Background=ThemeManager.Surface;Foreground=Ui.PageInk;
            var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());Content=layout;
            var top=new StackPanel{Margin=new Thickness(0,0,0,14)};var actions=new WrapPanel();
            refresh=Button(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text"),"RefreshSkillRepositories",delegate{RefreshSources(null);});actions.Children.Add(refresh);
            cancel=Button(TextResources.Get("common.cancel"),"CancelSkillDiscovery",delegate{if(running!=null)running.Cancel();});cancel.Visibility=Visibility.Collapsed;actions.Children.Add(cancel);top.Children.Add(actions);
            top.Children.Add(Ui.Text(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeA"),11,Ui.Muted));
            search.Name="SkillDiscoverySearch";search.ToolTip=TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.searchToolTip");search.Margin=new Thickness(0,10,0,0);search.TextChanged+=delegate{shown=40;RenderResults();};top.Children.Add(search);
            status.Name="SkillDiscoveryStatus";status.Margin=new Thickness(0,8,0,0);top.Children.Add(status);layout.Children.Add(top);
            var sourceBody=new StackPanel();sourceBody.Children.Add(sourcesPanel);
            var entry=new StackPanel{Margin=new Thickness(0,12,0,0)};
            entry.Children.Add(Ui.Text(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeB"),12,Ui.PageInk));repository.Name="SkillRepositoryInput";repository.ToolTip=TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.repositoryToolTip");repository.Margin=new Thickness(0,5,0,8);entry.Children.Add(repository);
            entry.Children.Add(Ui.Text(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeC"),11,Ui.Muted));branch.Name="SkillRepositoryBranch";branch.Margin=new Thickness(0,5,0,8);entry.Children.Add(branch);
            entry.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeD"),"AddSkillRepository",AddRepository));
            var recommended=new WrapPanel{Margin=new Thickness(0,10,0,0)};recommended.Children.Add(Ui.Text(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeE"),11,Ui.Muted));
            foreach(string name in new[]{"anthropics/skills","openai/skills"}){string value=name;recommended.Children.Add(Button(value,"",delegate{repository.Text=value;branch.Text="";repository.Focus();}));}entry.Children.Add(recommended);
            sourceBody.Children.Add(entry);sourceExpander=new Expander{Name="SkillRepositoryManager",Header=TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.header"),Content=sourceBody,IsExpanded=true,Foreground=Ui.PageInk,Margin=new Thickness(0,0,0,15)};
            zipPicker.Children.Add(Ui.Text(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeF"),12,Ui.Muted));zipPicker.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.text.alternativeG"),"ChooseSkillZip",delegate{var dialog=new Microsoft.Win32.OpenFileDialog{Title=TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.title"),Filter=TextResources.Get("skillDiscoveryPanel.skillDiscoveryPanel.filter"),CheckFileExists=true};if(dialog.ShowDialog()==true)OpenZip(dialog.FileName);}));zipPicker.Visibility=Visibility.Collapsed;body.Children.Add(zipPicker);
            body.Children.Add(Card(sourceExpander));count.Name="SkillDiscoveryCount";count.Margin=new Thickness(0,8,0,12);body.Children.Add(count);body.Children.Add(preview);body.Children.Add(results);
            var scroll=Ui.Scroll(body);scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;Grid.SetRow(scroll,1);layout.Children.Add(scroll);
            library.Changed+=LibraryChanged;Reload();sourceExpander.IsExpanded=sources.Count==0||errors.Count>0;
        }
        internal void SetMode(bool zip)
        {
            if(IsBusy)return;archiveMode=zip;selected=null;if(!zip&&archive!=null){archive.Dispose();archive=null;}
            sourceExpander.Visibility=zip?Visibility.Collapsed:Visibility.Visible;zipPicker.Visibility=zip?Visibility.Visible:Visibility.Collapsed;refresh.Visibility=zip?Visibility.Collapsed:Visibility.Visible;search.Text="";RenderResults();RenderPreview();SetStatus("");
        }
        internal void CancelWork(){if(running!=null)running.Cancel();}
        static Button Button(string text,string name,Action action)
        {
            var button=Ui.Button(text);button.Name=name;button.Margin=new Thickness(0,0,8,6);button.HorizontalAlignment=HorizontalAlignment.Left;button.FontSize=12;
            System.Windows.Automation.AutomationProperties.SetName(button,text);button.Click+=delegate{action();};return button;
        }
        static Border Card(UIElement child){return new Border{Child=child,Background=ThemeManager.CardSurface,BorderBrush=ThemeManager.CardBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(14),Margin=new Thickness(0,0,0,10)};}
        void SetStatus(string message){if(!disposed)status.Text=message;}
        static bool Expected(Exception e){return e is IOException||e is UnauthorizedAccessException||e is InvalidOperationException||e is ArgumentException||e is NotSupportedException||e is System.Security.SecurityException;}
        void Local(Action action){try{action();}catch(Exception e){if(!Expected(e))throw;SetStatus(e.Message);}}
        void Reload()
        {
            Local(delegate{sources=catalog.Sources;snapshots.Clear();
                foreach(var source in sources){try{var snapshot=catalog.ReadSnapshot(source.Id);if(snapshot!=null)snapshots[source.Id]=snapshot;}catch(Exception e){if(!Expected(e))throw;errors[source.Id]=TextResources.Format("skillDiscoveryPanel.reload.idEMessage",e.Message);}}
                RenderSources();RenderResults();RenderPreview();});
        }
        void RenderSources()
        {
            sourcesPanel.Children.Clear();sourceExpander.Header=TextResources.Format("skillDiscoveryPanel.renderSources.headerSourcesCount",sources.Count);
            if(sources.Count==0)sourcesPanel.Children.Add(Ui.Text(TextResources.Get("skillDiscoveryPanel.renderSources.text"),12,Ui.Muted));
            foreach(var source in sources){var captured=source;SkillRepositorySnapshot snapshot;snapshots.TryGetValue(source.Id,out snapshot);
                var block=new StackPanel{Margin=new Thickness(0,7,0,7)};var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());
                var toggle=Ui.Toggle("",source.Enabled,delegate(bool enabled){Local(delegate{catalog.SetEnabled(captured.Id,enabled);if(!enabled&&selected!=null&&selected.SourceId==captured.Id)selected=null;Reload();});});toggle.Name="SkillRepositoryEnabled";toggle.Tag=source.Id;toggle.Margin=new Thickness(0,0,10,0);toggle.ToolTip=TextResources.Format("skillDiscoveryPanel.renderSources.toolTipSourceRepository",source.Repository);System.Windows.Automation.AutomationProperties.SetName(toggle,TextResources.Format("skillDiscoveryPanel.renderSources.automationAutomationPropertiesSetName",source.Repository));row.Children.Add(toggle);
                var label=Ui.Text(source.Repository,13,Ui.PageInk);label.FontWeight=FontWeights.SemiBold;Grid.SetColumn(label,1);row.Children.Add(label);block.Children.Add(row);
                string details=snapshot==null?TextResources.Get("skillDiscoveryPanel.renderSources.detailsSnapshotNull.alternativeA")+(String.IsNullOrEmpty(source.Branch)?TextResources.Get("skillDiscoveryPanel.renderSources.detailsSnapshotNull.alternativeB"):source.Branch):TextResources.Format("skillDiscoveryPanel.renderSources.detailsSnapshotNull",snapshot.Branch,snapshot.Skills.Count);
                DateTime scanned;if(snapshot!=null&&DateTime.TryParse(snapshot.ScannedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out scanned))details+=TextResources.Format("skillDiscoveryPanel.renderSources.detailsScannedToLocalTime",scanned.ToLocalTime().ToString("MM-dd HH:mm"));
                if(snapshot!=null&&snapshot.InvalidCount>0)details+=TextResources.Format("skillDiscoveryPanel.renderSources.detailsSnapshotInvalidCount",snapshot.InvalidCount);block.Children.Add(Ui.Text(details,11,Ui.Muted));
                string error;if(errors.TryGetValue(source.Id,out error))block.Children.Add(Ui.Text(error,11,Ui.Brush("#91682E")));
                var options=new WrapPanel{Margin=new Thickness(0,7,0,0)};var edit=Ui.Field(source.Branch??"");edit.Name="EditSkillRepositoryBranch";edit.Tag=source.Id;edit.Width=140;edit.Margin=new Thickness(0,0,8,6);edit.ToolTip=TextResources.Get("skillDiscoveryPanel.renderSources.editToolTip");System.Windows.Automation.AutomationProperties.SetName(edit,TextResources.Format("skillDiscoveryPanel.renderSources.automationAutomationPropertiesSetName.alternativeA",source.Repository));options.Children.Add(edit);
                options.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.renderSources.text.alternativeA"),"SaveSkillRepositoryBranch",delegate{Local(delegate{catalog.SetBranch(captured.Id,edit.Text);if(selected!=null&&selected.SourceId==captured.Id)selected=null;errors.Remove(captured.Id);Reload();SetStatus(TextResources.Get("skillDiscoveryPanel.renderSources.setStatus"));});}));
                options.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.renderSources.text.alternativeB"),"RefreshSkillRepository",delegate{RefreshSources(captured.Id);}));
                options.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.renderSources.text.alternativeC"),"RemoveSkillRepository",delegate{Local(delegate{catalog.Remove(captured.Id);errors.Remove(captured.Id);if(selected!=null&&selected.SourceId==captured.Id)selected=null;Reload();SetStatus(TextResources.Get("skillDiscoveryPanel.renderSources.setStatus.alternativeA"));});}));block.Children.Add(options);sourcesPanel.Children.Add(block);
            }
        }
        void AddRepository(){if(IsBusy)return;Local(delegate{var source=catalog.Add(repository.Text,branch.Text);repository.Text="";branch.Text="";Reload();RefreshSources(source.Id);});}
        void RefreshSources(string id)
        {
            if(IsBusy)return;var targets=sources.Where(s=>id==null?s.Enabled:s.Id==id).ToArray();if(targets.Length==0){SetStatus(TextResources.Get("skillDiscoveryPanel.refreshSources.setStatus"));return;}
            Run(async delegate(CancellationToken token,IProgress<string> progress){int failed=0;string lastError="";
                foreach(var source in targets){token.ThrowIfCancellationRequested();
                    try{var snapshot=await client.ScanAsync(source,token,progress);token.ThrowIfCancellationRequested();catalog.SaveSnapshot(source.Id,snapshot);errors.Remove(source.Id);}
                    catch(Exception e){if(!Expected(e))throw;errors[source.Id]=e.Message;lastError=e.Message;failed++;}}
                Reload();sourceExpander.IsExpanded=failed!=0;return failed==0?TextResources.Get("skillDiscoveryPanel.refreshSources.run.alternativeA"):TextResources.Format("skillDiscoveryPanel.refreshSources.run",failed,lastError);
            });
        }
        IEnumerable<Result> Available()
        {
            if(archiveMode){if(archive!=null)foreach(var skill in archive.Skills)yield return new Result{FromZip=true,Skill=skill};yield break;}
            foreach(var source in sources.Where(s=>s.Enabled)){SkillRepositorySnapshot snapshot;if(!snapshots.TryGetValue(source.Id,out snapshot))continue;foreach(var skill in snapshot.Skills)yield return new Result{SourceId=source.Id,Snapshot=snapshot,Skill=skill};}
        }
        string Origin(Result result){return result.FromZip?TextResources.Get("skillDiscoveryPanel.origin.text"):result.Snapshot.Repository;}
        void RenderResults()
        {
            results.Children.Clear();string query=search.Text.Trim();var matches=Available().Where(r=>new[]{r.Skill.Name,r.Skill.Description,Origin(r)}.Any(value=>value.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();count.Text=(archiveMode?TextResources.Get("skillDiscoveryPanel.renderResults.count"):TextResources.Get("skillDiscoveryPanel.renderResults.count.alternativeA"))+" · "+matches.Length+TextResources.Get("skillDiscoveryPanel.renderResults.count.alternativeB");
            var installed=library.List().ToDictionary(s=>s.Name,StringComparer.Ordinal);
            foreach(var result in matches.Take(shown)){var captured=result;var content=new StackPanel();var title=Ui.Text(result.Skill.Name,15,Ui.PageInk);title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);content.Children.Add(Ui.Text(Origin(result)+" · "+(result.Skill.Path.Length==0?TextResources.Get("skillDiscoveryPanel.renderResults.text"):result.Skill.Path),11,Ui.Muted));var description=Ui.Text(result.Skill.Description,12,Ui.Muted);description.MaxHeight=65;description.Margin=new Thickness(0,5,0,9);content.Children.Add(description);
                var actions=new WrapPanel();actions.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeA"),"PreviewDiscoveredSkill",delegate{selected=captured;RenderPreview();preview.BringIntoView();}));ManagedSkillInfo existing;if(installed.TryGetValue(result.Skill.Name,out existing))actions.Children.Add(Ui.Text(SameOrigin(existing,result)?TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeB"):TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeC"),11,Ui.Muted));content.Children.Add(actions);
                var card=Card(content);card.Name="DiscoveredSkillRow";card.Tag=result.Skill.Name;results.Children.Add(card);}
            if(matches.Length==0)results.Children.Add(Ui.Text(query.Length>0?TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeD"):archiveMode?(archive==null?TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeE"):TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeF")):TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeG"),12,Ui.Muted));
            if(matches.Length>shown)results.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeH"),"MoreDiscoveredSkills",delegate{shown+=40;RenderResults();}));
            if(archiveMode&&archive!=null){var close=Button(TextResources.Get("skillDiscoveryPanel.renderResults.text.alternativeI"),"CloseSkillArchive",delegate{if(IsBusy)return;archive.Dispose();archive=null;selected=null;RenderResults();RenderPreview();SetStatus(TextResources.Get("skillDiscoveryPanel.renderResults.setStatus"));});close.IsEnabled=!IsBusy;results.Children.Add(close);}
        }
        static bool SameOrigin(ManagedSkillInfo installed,Result result){return installed.Origin!=null&&(result.FromZip?installed.Origin.Kind=="zip":installed.Origin.Kind=="github"&&installed.Origin.Repository==result.Snapshot.Repository&&installed.Origin.Path==result.Skill.Path);}
        void RenderPreview()
        {
            preview.Children.Clear();if(selected==null)return;var content=new StackPanel();var title=Ui.Text(selected.Skill.Name,18,Ui.PageInk);title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);
            var origin=Ui.Text(selected.FromZip?TextResources.Format("skillDiscoveryPanel.renderPreview.template",selected.Skill.Path):TextResources.Format("skillDiscoveryPanel.renderPreview.template.alternativeA",selected.Snapshot.Repository,selected.Snapshot.Branch,selected.Skill.Path,selected.Snapshot.Commit),11,Ui.Muted);origin.Name="SkillPreviewOrigin";content.Children.Add(origin);
            var text=Ui.Field(selected.Skill.Content,true);text.Name="SkillPreviewContent";text.IsReadOnly=true;text.Height=230;text.FontFamily=new FontFamily("Consolas, Microsoft YaHei UI");text.FontSize=12;text.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;text.ContextMenu=ChatUi.EditorMenu(text,true);text.Margin=new Thickness(0,10,0,10);content.Children.Add(text);
            var existing=library.List().FirstOrDefault(s=>s.Name==selected.Skill.Name);var actions=new WrapPanel();var install=Button(existing==null?TextResources.Get("skillDiscoveryPanel.renderPreview.text"):SameOrigin(existing,selected)?TextResources.Get("skillDiscoveryPanel.renderPreview.text.alternativeA"):TextResources.Get("skillDiscoveryPanel.renderPreview.text.alternativeB"),"InstallDiscoveredSkill",Install);install.IsEnabled=existing==null&&!IsBusy;install.Background=ThemeManager.ActionSurface;install.Foreground=Brushes.White;actions.Children.Add(install);actions.Children.Add(Button(TextResources.Get("skillDiscoveryPanel.renderPreview.text.alternativeC"),"CloseSkillPreview",delegate{selected=null;RenderPreview();}));content.Children.Add(actions);
            content.Children.Add(Ui.Text(existing==null?TextResources.Get("skillDiscoveryPanel.renderPreview.text.alternativeD"):SameOrigin(existing,selected)?TextResources.Get("skillDiscoveryPanel.renderPreview.text.alternativeE"):TextResources.Get("skillDiscoveryPanel.renderPreview.text.alternativeF"),11,Ui.Muted));preview.Children.Add(Card(content));
        }
        void Install()
        {
            if(IsBusy||selected==null)return;var result=selected;
            Run(async delegate(CancellationToken token,IProgress<string> progress){ManagedSkillInfo item;
                if(result.FromZip)item=await Task.Run(delegate{token.ThrowIfCancellationRequested();return archive.Install(result.Skill,library);},token);
                else item=await client.InstallAsync(result.Snapshot,result.Skill,library,token,progress);
                RenderResults();RenderPreview();return TextResources.Format("skillDiscoveryPanel.install.run",item.Name);});
        }
        internal void OpenZip(string path)
        {
            if(IsBusy){SetStatus(TextResources.Get("skillDiscoveryPanel.openZip.setStatus"));return;}
            SetMode(true);
            Run(async delegate(CancellationToken token,IProgress<string> progress){var opened=await Task.Run(delegate{return new SkillArchive(path);},token);
                if(token.IsCancellationRequested||disposed){opened.Dispose();token.ThrowIfCancellationRequested();return "";}
                if(archive!=null)archive.Dispose();archive=opened;selected=null;search.Text="";RenderResults();RenderPreview();return TextResources.Get("skillDiscoveryPanel.openZip.run.alternativeA")+(opened.InvalidCount>0?TextResources.Format("skillDiscoveryPanel.openZip.run",opened.InvalidCount):"");});
        }
        async void Run(Func<CancellationToken,IProgress<string>,Task<string>> operation)
        {
            if(IsBusy||disposed)return;var cts=new CancellationTokenSource();running=cts;BusyChanged();SetStatus(TextResources.Get("skillDiscoveryPanel.run.setStatus"));
            var progress=new Progress<string>(value=>{Dispatcher.BeginInvoke(new Action(delegate{if(!disposed&&ReferenceEquals(running,cts))SetStatus(value);}));});
            string message="";
            try{message=await operation(cts.Token,progress);}
            catch(OperationCanceledException){message=TextResources.Get("skillDiscoveryPanel.run.message");}
            catch(Exception e){if(!disposed&&!Expected(e))throw;message=e.Message;}
            finally{running=null;cts.Dispose();if(!disposed){Reload();BusyChanged();SetStatus(message);}else if(archive!=null)archive.Dispose();}
        }
        void BusyChanged(){sourceExpander.IsEnabled=!IsBusy;refresh.IsEnabled=!IsBusy;cancel.Visibility=IsBusy?Visibility.Visible:Visibility.Collapsed;RenderResults();RenderPreview();}
        void LibraryChanged(){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(LibraryChanged));return;}RenderResults();RenderPreview();}
        public void Dispose(){if(disposed)return;disposed=true;if(running!=null)running.Cancel();library.Changed-=LibraryChanged;if(running==null&&archive!=null)archive.Dispose();client.Dispose();}
    }
}
