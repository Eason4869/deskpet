using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    // Keep the MCP editor alive across tabs so that local drafts survive navigation.
    internal sealed class PluginsPage : UserControl, IDisposable
    {
        readonly PetWindow pet;
        readonly ManagedSkillLibrary library;
        readonly McpSettingsPanel mcp;
        readonly SkillDiscoveryPanel discovery;
        readonly Grid layout=new Grid(),header=new Grid(),content=new Grid();
        readonly WrapPanel actions=new WrapPanel();
        readonly StackPanel skills=new StackPanel();
        readonly WrapPanel rows=new WrapPanel();
        readonly Grid searchSurface=new Grid();
        readonly TextBox search=Ui.Field();
        readonly TextBlock status=Ui.Text("",12,Ui.Muted),warning=Ui.Text("",12,Ui.Brush("#91682E"));
        readonly Button skillsTab,mcpTab,add;
        readonly ScrollViewer skillScroll;
        readonly TextBlock footer=Ui.Text("",11,Ui.Muted);
        static readonly ControlTemplate pillTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='pill' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='18'><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='pill' Property='Opacity' Value='.75'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        IList<ManagedSkillInfo> installed=new ManagedSkillInfo[0];
        string detailId,removeId;
        bool onMcp,disposed;

        internal PluginsPage(PetWindow pet,SkillRepositoryClient repositoryClient=null)
        {
            if(pet==null)throw new ArgumentNullException("pet");
            this.pet=pet;library=new ManagedSkillLibrary(Path.Combine(pet.Store.DirectoryPath,"skills"));
            Name="PluginsPage";Background=ThemeManager.Surface;Foreground=Ui.PageInk;
            layout.MaxWidth=Ui.PageContentWidth;layout.HorizontalAlignment=HorizontalAlignment.Stretch;layout.Margin=new Thickness(30,40,30,24);
            for(int i=0;i<4;i++)layout.RowDefinitions.Add(new RowDefinition{Height=i==2?new GridLength(1,GridUnitType.Star):GridLength.Auto});
            Content=layout;
            header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            header.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});header.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var titles=new StackPanel{Margin=new Thickness(0,0,18,0)};
            var heading=Ui.PageHeading("插件");titles.Children.Add(heading);
            titles.Children.Add(Ui.Text("为伙伴添加技能与外部工具。",12,Ui.Muted));header.Children.Add(titles);
            add=ActionButton("添加技能","ImportManagedSkill",ChooseImport);add.Background=ThemeManager.ActionSurface;add.Foreground=Brushes.White;add.BorderBrush=add.Background;
            searchSurface.Width=260;searchSurface.Margin=new Thickness(0,0,12,5);searchSurface.VerticalAlignment=VerticalAlignment.Center;actions.Children.Add(searchSurface);actions.Children.Add(add);Grid.SetColumn(actions,1);actions.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(actions);layout.Children.Add(header);
            var controls=new StackPanel{Margin=new Thickness(0,24,0,17)};
            var tabs=new WrapPanel{Margin=new Thickness(0,0,0,16)};
            skillsTab=Ui.Button("技能  0");skillsTab.Name="PluginsSkillsTab";skillsTab.Click+=delegate{SelectSkills();};
            mcpTab=Ui.Button("MCP  0");mcpTab.Name="PluginsMcpTab";mcpTab.Click+=delegate{SelectMcp();};
            skillsTab.Template=pillTemplate;mcpTab.Template=pillTemplate;skillsTab.Padding=mcpTab.Padding=new Thickness(17,5,17,5);
            tabs.Children.Add(skillsTab);tabs.Children.Add(mcpTab);controls.Children.Add(tabs);
            search.Name="PluginsSearch";search.ToolTip="按技能名称或描述搜索";search.Height=search.MinHeight=36;search.FontSize=13;search.Padding=new Thickness(12,0,12,0);search.Foreground=Ui.PageInk;
            search.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Border Background='{TemplateBinding Background}' BorderBrush='#E0E3E8' BorderThickness='1' CornerRadius='18'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></Border></ControlTemplate>");
            System.Windows.Automation.AutomationProperties.SetName(search,"搜索技能名称或描述");
            searchSurface.Children.Add(search);
            var placeholder=new TextBlock{Name="PluginsSearchPlaceholder",Text="搜索技能名称或描述",FontSize=13,Foreground=Ui.Brush("#9299A3"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(26,0,13,0),IsHitTestVisible=false};searchSurface.Children.Add(placeholder);
            search.TextChanged+=delegate{placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;RenderRows();};
            Grid.SetRow(controls,1);layout.Children.Add(controls);
            var installedTitle=Ui.Text("已安装",15,Foreground);installedTitle.FontWeight=FontWeights.SemiBold;installedTitle.Margin=new Thickness(8,0,0,12);skills.Children.Add(installedTitle);skills.Children.Add(new Border{Height=1,Background=Ui.Brush("#ECEEF1"),Margin=new Thickness(0,0,0,14)});rows.SizeChanged+=delegate{LayoutSkillRows();};
            warning.Name="ManagedSkillsWarning";warning.Margin=new Thickness(0,0,0,14);warning.Visibility=Visibility.Collapsed;skills.Children.Add(warning);skills.Children.Add(rows);
            skillScroll=Ui.Scroll(skills);skillScroll.Padding=new Thickness(0,0,3,0);content.Children.Add(skillScroll);
            mcp=new McpSettingsPanel(pet){Visibility=Visibility.Collapsed,Background=Brushes.Transparent};mcp.UseHeaderAddButton();mcp.Saved+=McpSaved;mcp.EditorVisibilityChanged+=McpEditorVisibilityChanged;content.Children.Add(mcp);Grid.SetRow(content,2);layout.Children.Add(content);
            discovery=new SkillDiscoveryPanel(library,new SkillRepositoryCatalog(Path.Combine(pet.Store.DirectoryPath,"skill-discovery")),repositoryClient??new SkillRepositoryClient()){Visibility=Visibility.Collapsed};discovery.Back+=SelectSkills;content.Children.Add(discovery);
            var bottom=new StackPanel{Margin=new Thickness(0,15,0,0)};status.Name="PluginsStatus";status.Visibility=Visibility.Collapsed;status.Margin=new Thickness(0,0,0,7);bottom.Children.Add(status);footer.Name="PluginsScope";bottom.Children.Add(footer);Grid.SetRow(bottom,3);layout.Children.Add(bottom);
            library.Changed+=LibraryChanged;pet.McpConnections.Changed+=McpChanged;
            SizeChanged+=delegate{Adapt();};IsVisibleChanged+=delegate{if(IsVisible)Refresh();};
            Refresh();SelectSkills();
        }

        internal bool HasUnsavedEdits {get{return mcp.HasUnsavedChanges;}}
        internal void OpenMcpEditor(){SelectMcp();mcp.OpenEditor();}
        internal void SelectMcp(){discovery.Visibility=Visibility.Collapsed;onMcp=true;add.Visibility=mcp.EditorVisible?Visibility.Collapsed:Visibility.Visible;skillScroll.Visibility=Visibility.Collapsed;mcp.Visibility=Visibility.Visible;search.Visibility=Visibility.Collapsed;((FrameworkElement)search.Parent).Visibility=Visibility.Collapsed;add.Name="AddMcpServer";add.Content="添加服务器";System.Windows.Automation.AutomationProperties.SetName(add,"添加服务器");StyleTabs();footer.Text="MCP 配置保存后需手动连接。技能与 MCP 均在本机管理。";}
        internal void SelectSkills(){discovery.Visibility=Visibility.Collapsed;onMcp=false;add.Visibility=Visibility.Visible;skillScroll.Visibility=Visibility.Visible;mcp.Visibility=Visibility.Collapsed;search.Visibility=Visibility.Visible;((FrameworkElement)search.Parent).Visibility=Visibility.Visible;add.Name="ImportManagedSkill";add.Content="添加技能 ▾";System.Windows.Automation.AutomationProperties.SetName(add,"添加技能");StyleTabs();footer.Text="从仓库、ZIP 或本机目录添加技能，启用变更从下一次工作请求生效。";}
        void StyleTabs(){foreach(var button in new[]{skillsTab,mcpTab}){bool selected=button==(onMcp?mcpTab:skillsTab);button.Background=selected?ThemeManager.SelectedSurface:Brushes.Transparent;button.BorderBrush=selected?ThemeManager.SelectedBorder:Brushes.Transparent;button.Foreground=selected?Ui.Brush("#282A2F"):Ui.Muted;button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"已选中":"");}}
        void Adapt(){layout.Margin=ActualWidth<480?new Thickness(16,20,16,20):new Thickness(30,40,30,24);double available=Math.Max(0,Math.Min(Ui.PageContentWidth,ActualWidth-layout.Margin.Left-layout.Margin.Right));bool narrow=available<680;Grid.SetRow(actions,narrow?1:0);Grid.SetColumn(actions,narrow?0:1);Grid.SetColumnSpan(actions,narrow?2:1);actions.HorizontalAlignment=narrow?HorizontalAlignment.Left:HorizontalAlignment.Right;actions.Width=narrow?available:Double.NaN;actions.Margin=narrow?new Thickness(0,14,0,0):new Thickness(0);searchSurface.Width=narrow?Math.Max(100,available-110):260;LayoutSkillRows();}
        void LayoutSkillRows(){double width=rows.ActualWidth;if(width<=0)return;bool paired=width>=700;int index=0;foreach(FrameworkElement child in rows.Children){bool tile=child.Name=="ManagedSkillRow";child.Width=tile&&paired?Math.Floor((width-16)/2):width;child.Margin=tile?new Thickness(0,0,paired&&index++%2==0?16:0,12):new Thickness(0);}}
        static Button ActionButton(string title,string name,Action action){var button=Ui.Button(title);button.Name=name;button.FontSize=12;button.Foreground=Ui.PageInk;button.Margin=new Thickness(0,0,8,5);button.Padding=new Thickness(12,5,12,5);button.Click+=delegate{action();};System.Windows.Automation.AutomationProperties.SetName(button,title);return button;}
        void ChooseImport()
        {
            if(onMcp){mcp.BeginAdd();return;}
            var menu=ChatUi.Menu();menu.Name="AddSkillMenu";
            foreach(var option in new[]{new {Title="在发现里浏览",Action=(Action)OpenDiscovery},new {Title="从 ZIP 安装",Action=(Action)ChooseZip},new {Title="导入本机已有",Action=(Action)ChooseDirectory}}){var action=option.Action;var item=new MenuItem{Header=option.Title};item.Click+=delegate{action();};menu.Items.Add(item);}
            add.ContextMenu=menu;menu.PlacementTarget=add;menu.IsOpen=true;
        }
        void ChooseDirectory(){using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description="选择包含 SKILL.md 的技能文件夹。将复制到本机技能库。",ShowNewFolderButton=false}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK)ImportDirectory(dialog.SelectedPath);}}
        void ChooseZip(){var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择技能 ZIP",Filter="技能压缩包 (*.zip)|*.zip",CheckFileExists=true};if(dialog.ShowDialog()==true){OpenDiscovery();discovery.OpenZip(dialog.FileName);}}
        internal void OpenDiscovery(){onMcp=false;add.Visibility=Visibility.Visible;add.Name="ImportManagedSkill";add.Content="添加技能 ▾";skillScroll.Visibility=Visibility.Collapsed;mcp.Visibility=Visibility.Collapsed;searchSurface.Visibility=Visibility.Collapsed;discovery.Visibility=Visibility.Visible;StyleTabs();footer.Text="搜索所选公开仓库，预览后安装到本机。";}
        internal bool ImportDirectory(string directory){try{var item=library.Import(directory);detailId=null;removeId=null;Refresh();Notify("已添加 "+item.Name+"，从下一次工作请求起可用。");return true;}catch(Exception e){if(!Expected(e))throw;Notify("未能添加技能。"+e.Message);return false;}}
        static bool Expected(Exception e){return e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException;}
        void Notify(string text){status.Text=text;status.Visibility=Visibility.Visible;}
        void Refresh(){if(disposed)return;try{installed=library.List();warning.Text=library.Warning;warning.Visibility=String.IsNullOrEmpty(warning.Text)?Visibility.Collapsed:Visibility.Visible;skillsTab.Content="技能  "+installed.Count;McpCount();RenderRows();}catch(Exception e){if(!Expected(e))throw;installed=new ManagedSkillInfo[0];warning.Text="技能库暂不可用。"+e.Message;warning.Visibility=Visibility.Visible;skillsTab.Content="技能  0";RenderRows();}}
        void McpCount(){mcpTab.Content="MCP  "+(pet.Settings.McpServers??new List<McpServerConfiguration>()).Count(c=>c!=null);}
        void RenderRows(){rows.Children.Clear();string query=search.Text.Trim();var shown=installed.Where(s=>query.Length==0||(s.Name??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||(s.Description??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            if(shown.Length==0){var empty=new StackPanel{Margin=new Thickness(22,39,22,43),HorizontalAlignment=HorizontalAlignment.Stretch};var icon=ShellIcons.Image(installed.Count==0?"mcp":"file",Ui.Brush("#9DA8B8"),34);icon.Margin=new Thickness(0,0,0,15);empty.Children.Add(icon);var title=Ui.Text(installed.Count==0?"还没有技能":"没有找到匹配的技能",16,Foreground);title.Name=installed.Count==0?"ManagedSkillsEmpty":"ManagedSkillsNoResults";title.TextAlignment=TextAlignment.Center;empty.Children.Add(title);var hint=Ui.Text(installed.Count==0?"添加一个含 SKILL.md 的文件夹，\n让伙伴在工作时按需使用其中的说明。":"试试其他名称或描述关键词。",12,Ui.Muted);hint.TextAlignment=TextAlignment.Center;hint.Margin=new Thickness(0,7,0,0);empty.Children.Add(hint);rows.Children.Add(Surface(empty));LayoutSkillRows();return;}
            foreach(var item in shown)rows.Children.Add(SkillRow(item));LayoutSkillRows();
        }
        Border SkillRow(ManagedSkillInfo item){var body=new StackPanel();var row=new Grid{MinHeight=72};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(45)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});
            var icon=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(10),Background=Ui.Brush("#F3F4F5"),Child=ShellIcons.Image("book",Ui.Brush("#72777F"),20),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};row.Children.Add(icon);
            var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,5,12,5),MinWidth=0};var title=Ui.Text(item.Name,14,Foreground);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=item.Name;copy.Children.Add(title);var description=Ui.Text(item.Description,12,Ui.Muted);description.TextWrapping=TextWrapping.NoWrap;description.MaxHeight=23;description.TextTrimming=TextTrimming.CharacterEllipsis;description.ToolTip=item.Description;copy.Children.Add(description);Grid.SetColumn(copy,1);row.Children.Add(copy);
            bool changing=false;CheckBox toggle=null;toggle=Ui.Toggle("",item.Enabled,delegate(bool enabled){if(changing)return;try{library.SetEnabled(item.Id,enabled);Notify(item.Name+(enabled?" 已启用":" 已停用")+"，从下一次工作请求生效。");}catch(Exception e){if(!Expected(e))throw;changing=true;toggle.IsChecked=item.Enabled;changing=false;Notify("未能保存启用状态。"+e.Message);}});toggle.Name="ManagedSkillEnabled";toggle.Tag=item.Id;toggle.Margin=new Thickness(0);toggle.VerticalAlignment=VerticalAlignment.Center;toggle.ToolTip="启用或停用 "+item.Name;System.Windows.Automation.AutomationProperties.SetName(toggle,"启用技能 "+item.Name);Grid.SetColumn(toggle,2);row.Children.Add(toggle);
            var details=Ui.Button("");details.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse("M0,0 L4,4 L0,8"),Stroke=Ui.Brush("#939BA6"),StrokeThickness=1.5,Width=5,Height=9,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(detailId==item.Id?90:0)};details.Name="ManagedSkillDetails";details.Tag=item.Id;details.ToolTip="查看技能详情";details.Width=29;details.MinHeight=30;details.Padding=new Thickness(0);details.Margin=new Thickness(0);details.Background=Brushes.Transparent;details.BorderBrush=Brushes.Transparent;details.Click+=delegate{detailId=detailId==item.Id?null:item.Id;removeId=null;RenderRows();};System.Windows.Automation.AutomationProperties.SetName(details,"查看 "+item.Name+" 详情");Grid.SetColumn(details,3);row.Children.Add(details);body.Children.Add(row);
            if(detailId==item.Id)body.Children.Add(Details(item));var card=new Border{Name="ManagedSkillRow",Tag=item.Id,Child=body,Background=detailId==item.Id?Ui.Brush("#F8F9FB"):Brushes.Transparent,CornerRadius=new CornerRadius(10),Padding=new Thickness(8,7,8,7)};card.MouseEnter+=delegate{card.Background=Ui.Brush("#F4F5F7");};card.MouseLeave+=delegate{card.Background=detailId==item.Id?Ui.Brush("#F8F9FB"):Brushes.Transparent;};return card;
        }
        UIElement Details(ManagedSkillInfo item){var detail=new StackPanel{Margin=new Thickness(0,9,0,9)};detail.Children.Add(new Border{Height=1,Background=Ui.Brush("#ECEEF1"),Margin=new Thickness(0,0,0,15)});detail.Children.Add(Ui.Text("技能目录",11,Ui.Muted));var path=Ui.Text(item.Directory,11,Ui.Muted);path.Name="ManagedSkillDirectory";path.Margin=new Thickness(0,3,0,13);detail.Children.Add(path);
            if(item.Origin!=null){var origin=Ui.Text((item.Origin.Kind=="github"?"GitHub · ":"ZIP · ")+item.Origin.Repository+"\n"+item.Origin.Path+"\n"+(item.Origin.Branch??"")+" · "+item.Origin.Commit,11,Ui.Muted);origin.Name="ManagedSkillOrigin";origin.Margin=new Thickness(0,0,0,12);detail.Children.Add(origin);}
            try{var source=library.Read(item.Id);var text=Ui.Field(source.Content,true);text.Name="ManagedSkillContent";text.IsReadOnly=true;text.MaxHeight=280;text.MinHeight=95;text.FontSize=12;text.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;text.ContextMenu=ChatUi.EditorMenu(text,true);detail.Children.Add(text);}catch(Exception e){if(!Expected(e))throw;detail.Children.Add(Ui.Text("无法读取 SKILL.md。"+e.Message,12,Ui.Muted));}
            var remove=Ui.Button(removeId==item.Id?"确认移除技能":"移除技能");remove.Name="RemoveManagedSkill";remove.Tag=item.Id;remove.HorizontalAlignment=HorizontalAlignment.Left;remove.Margin=new Thickness(0,12,0,0);remove.Foreground=Ui.Brush("#A65050");remove.Click+=delegate{if(removeId!=item.Id){removeId=item.Id;RenderRows();return;}try{library.Remove(item.Id);detailId=null;removeId=null;Refresh();Notify("已移除 "+item.Name+"，原始文件夹保留。");}catch(Exception e){if(!Expected(e))throw;Notify("未能移除技能。"+e.Message);}};detail.Children.Add(remove);
            if(removeId==item.Id){var note=Ui.Text("仅移除本机管理的技能，原始文件夹会保留。",11,Ui.Muted);note.Name="ManagedSkillRemoveConfirmation";note.Margin=new Thickness(0,7,0,0);detail.Children.Add(note);var cancel=Ui.Button("取消");cancel.Name="CancelRemoveManagedSkill";cancel.HorizontalAlignment=HorizontalAlignment.Left;cancel.Margin=new Thickness(0,7,0,0);cancel.Click+=delegate{removeId=null;RenderRows();};detail.Children.Add(cancel);}return detail;
        }
        static Border Surface(UIElement child){return new Border{Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E6E9EE"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Margin=new Thickness(0,0,0,10),Padding=new Thickness(16),Child=child};}
        void LibraryChanged(){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(LibraryChanged));return;}Refresh();}
        void McpEditorVisibilityChanged(){if(onMcp)add.Visibility=mcp.EditorVisible?Visibility.Collapsed:Visibility.Visible;}
        void McpSaved(){McpCount();}
        void McpChanged(){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(McpChanged));return;}McpCount();}
        public void Dispose(){if(disposed)return;disposed=true;library.Changed-=LibraryChanged;pet.McpConnections.Changed-=McpChanged;mcp.Saved-=McpSaved;mcp.EditorVisibilityChanged-=McpEditorVisibilityChanged;discovery.Back-=SelectSkills;discovery.Dispose();mcp.Dispose();}
    }
}
