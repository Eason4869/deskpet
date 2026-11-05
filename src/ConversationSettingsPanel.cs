using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Dafeiyu
{
    public sealed class ConversationSettingsPanel : UserControl
    {
        readonly PetWindow pet;
        readonly CheckBox predicted,plainText,showContext,readReplies,detailed;
        readonly ComboBox defaultMode,sendShortcut,followUp;
        readonly TextBlock notice;
        readonly SettingsPageHeader header;readonly ScrollViewer[] pages;
        PetSettings baseline;bool refreshing;
        internal int SelectedSection {get{return header.SelectedIndex;}}
        internal event Action SelectionChanged;
        internal void SelectSection(int index){header.Select(index);}
        internal void ShowTarget(string name){SelectSection(name=="DefaultConversationMode"?0:name=="ReadReplies"||name=="ShowContextUsage"||name=="AgentDetailedProcess"?2:1);}
        public bool HasUnsavedChanges
        {
            get{return baseline!=null&&(defaultMode.SelectedIndex!=(baseline.AgentEnabled?1:0)||predicted.IsChecked!=baseline.ShowPredictedMessages||plainText.IsChecked!=baseline.PlainTextEditor||showContext.IsChecked!=baseline.ShowContextUsage||readReplies.IsChecked!=baseline.ReadReplies||detailed.IsChecked!=baseline.AgentDetailedProcess||sendShortcut.SelectedIndex!=(baseline.SendOnEnter?0:1)||followUp.SelectedIndex!=(baseline.FollowUpBehavior=="steer"?1:0));}
        }
        public ConversationSettingsPanel(PetWindow companion)
        {
            if(companion==null)throw new ArgumentNullException("companion");pet=companion;Name="ConversationSettingsPanel";Background=ThemeManager.Surface;
            var shell=new DockPanel{LastChildFill=true};
            var footer=new DockPanel{Margin=new Thickness(24,14,24,16)};
            var actions=new StackPanel{Orientation=Orientation.Horizontal};
            var restore=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.restore"));restore.Name="RestoreConversationSettings";restore.Margin=new Thickness(0,0,10,0);restore.Click+=delegate{baseline=null;RefreshState();};actions.Children.Add(restore);
            var save=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.save"),true);save.Name="SaveConversationSettings";save.Margin=new Thickness(0);save.Click+=async delegate{await SaveAsync();};actions.Children.Add(save);DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
            notice=Ui.Text("",11,Ui.Muted);notice.Name="ConversationSettingsNotice";notice.VerticalAlignment=VerticalAlignment.Center;notice.Margin=new Thickness(0,0,12,0);footer.Children.Add(notice);
            var footerBorder=new Border{Child=footer,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0)};DockPanel.SetDock(footerBorder,Dock.Bottom);shell.Children.Add(footerBorder);
            header=new SettingsPageHeader(()=>TextResources.Get("settingsLayout.conversationTitle"),()=>TextResources.Get("settingsLayout.conversationDescription"),()=>new[]{TextResources.Get("settingsLayout.modeGroup"),TextResources.Get("settingsLayout.inputGroup"),TextResources.Get("settingsLayout.displayGroup")},new[]{"ConversationDefaultsTab","ConversationInputTab","ConversationDisplayTab"}){Margin=new Thickness(24,22,24,18)};DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
            var defaultContent=new StackPanel{Margin=new Thickness(24,0,24,24)};var inputContent=new StackPanel{Margin=new Thickness(24,0,24,24)};var displayContent=new StackPanel{Margin=new Thickness(24,0,24,24)};
            var defaults=new StackPanel();defaultMode=Options("DefaultConversationMode",()=>new[]{TextResources.Get("settingsLayout.chatMode"),TextResources.Get("settingsLayout.workMode")});defaults.Children.Add(Ui.SettingRow(()=>TextResources.Get("settingsLayout.defaultMode"),()=>TextResources.Get("settingsLayout.defaultModeHelp"),defaultMode));AddGroup(defaultContent,()=>TextResources.Get("settingsLayout.modeGroup"),defaults);
            var input=new StackPanel();sendShortcut=Options("SendShortcut",()=>new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeF"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeG")});input.Children.Add(Ui.SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAN"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAO"),sendShortcut));
            followUp=Options("FollowUpBehavior",()=>new[]{TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeH"),TextResources.Get("workSettingsPanel.workSettingsPanel.itemsSource.alternativeI")});input.Children.Add(Ui.SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAP"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAQ"),followUp));
            predicted=Toggle("ShowPredictedMessages");input.Children.Add(Ui.SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAH"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAI"),predicted));plainText=Toggle("PlainTextEditor");input.Children.Add(Ui.SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAJ"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAK"),plainText));AddGroup(inputContent,()=>TextResources.Get("settingsLayout.inputGroup"),input);
            var display=new StackPanel();showContext=Toggle("ShowContextUsage");display.Children.Add(Ui.SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAL"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAM"),showContext));readReplies=Toggle("ReadReplies");display.Children.Add(Ui.SettingRow(()=>TextResources.Get("settingsLayout.readReplies"),()=>TextResources.Get("settingsLayout.readRepliesHelp"),readReplies));detailed=Toggle("AgentDetailedProcess");display.Children.Add(Ui.SettingRow(()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAF"),()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAG"),detailed));AddGroup(displayContent,()=>TextResources.Get("settingsLayout.displayGroup"),display);
            pages=new[]{Ui.Scroll(defaultContent),Ui.Scroll(inputContent),Ui.Scroll(displayContent)};var bodyHost=new Grid();for(int index=0;index<pages.Length;index++){pages[index].Name="ConversationSettingsScroll"+index;pages[index].Visibility=index==0?Visibility.Visible:Visibility.Collapsed;bodyHost.Children.Add(pages[index]);}shell.Children.Add(bodyHost);Content=shell;
            header.SelectionChanged+=index=>{for(int page=0;page<pages.Length;page++)pages[page].Visibility=page==index?Visibility.Visible:Visibility.Collapsed;if(SelectionChanged!=null)SelectionChanged();};
            AddHandler(ToggleButton.CheckedEvent,new RoutedEventHandler(delegate{DraftChanged();}));AddHandler(ToggleButton.UncheckedEvent,new RoutedEventHandler(delegate{DraftChanged();}));AddHandler(Selector.SelectionChangedEvent,new SelectionChangedEventHandler(delegate{DraftChanged();}));RefreshState();
        }
        static CheckBox Toggle(string name){var value=Ui.Toggle("",false,delegate{});value.Name=name;return value;}
        static ComboBox Options(string name,Func<string[]> labels){return LocalizedUi.Options(new ComboBox{Name=name,ItemsSource=labels(),MinHeight=36},labels);}
        static void AddGroup(StackPanel parent,Func<string> title,StackPanel body)
        {
            SettingsSurface.Section(parent,title,body);
        }
        void DraftChanged(){if(!refreshing)LocalizedUi.Set(notice,"Text",()=>(HasUnsavedChanges?TextResources.Get("settingsLayout.unsaved"):TextResources.Get("settingsLayout.saved")));}
        public void RefreshState()
        {
            if(HasUnsavedChanges)return;refreshing=true;
            try{baseline=pet.Settings.Copy();defaultMode.SelectedIndex=baseline.AgentEnabled?1:0;predicted.IsChecked=baseline.ShowPredictedMessages;plainText.IsChecked=baseline.PlainTextEditor;showContext.IsChecked=baseline.ShowContextUsage;readReplies.IsChecked=baseline.ReadReplies;detailed.IsChecked=baseline.AgentDetailedProcess;sendShortcut.SelectedIndex=baseline.SendOnEnter?0:1;followUp.SelectedIndex=baseline.FollowUpBehavior=="steer"?1:0;}
            finally{refreshing=false;}DraftChanged();
        }
        public void RefreshRestoredPreferences()
        {
            refreshing=true;
            try{
                predicted.IsChecked=pet.Settings.ShowPredictedMessages;plainText.IsChecked=pet.Settings.PlainTextEditor;showContext.IsChecked=pet.Settings.ShowContextUsage;sendShortcut.SelectedIndex=pet.Settings.SendOnEnter?0:1;followUp.SelectedIndex=pet.Settings.FollowUpBehavior=="steer"?1:0;
                baseline.ShowPredictedMessages=pet.Settings.ShowPredictedMessages;baseline.PlainTextEditor=pet.Settings.PlainTextEditor;baseline.ShowContextUsage=pet.Settings.ShowContextUsage;baseline.SendOnEnter=pet.Settings.SendOnEnter;baseline.FollowUpBehavior=pet.Settings.FollowUpBehavior;
            }finally{refreshing=false;}DraftChanged();
        }
        public bool Save(){return SaveCoreAsync(change=>Task.FromResult(pet.CommitSettingsNow(change))).GetAwaiter().GetResult();}
        public Task<bool> SaveAsync(){return pet.RunSettingsEditAsync(this,()=>SaveCoreAsync(change=>pet.PersistSettingsAsync(change)));}
        async Task<bool> SaveCoreAsync(Func<Action<PetSettings>,Task<bool>> commit)
        {
            var original=baseline.Copy();var submitted=baseline.Copy();submitted.AgentEnabled=defaultMode.SelectedIndex==1;submitted.ShowPredictedMessages=predicted.IsChecked==true;submitted.PlainTextEditor=plainText.IsChecked==true;submitted.ShowContextUsage=showContext.IsChecked==true;submitted.ReadReplies=readReplies.IsChecked==true;submitted.AgentDetailedProcess=detailed.IsChecked==true;submitted.SendOnEnter=sendShortcut.SelectedIndex==0;submitted.FollowUpBehavior=followUp.SelectedIndex==1?"steer":"queue";
            if(!await commit(target=>{
                if(submitted.AgentEnabled!=original.AgentEnabled)target.AgentEnabled=submitted.AgentEnabled;
                if(submitted.ShowPredictedMessages!=original.ShowPredictedMessages)target.ShowPredictedMessages=submitted.ShowPredictedMessages;
                if(submitted.PlainTextEditor!=original.PlainTextEditor)target.PlainTextEditor=submitted.PlainTextEditor;
                if(submitted.ShowContextUsage!=original.ShowContextUsage)target.ShowContextUsage=submitted.ShowContextUsage;
                if(submitted.ReadReplies!=original.ReadReplies)target.ReadReplies=submitted.ReadReplies;
                if(submitted.AgentDetailedProcess!=original.AgentDetailedProcess)target.AgentDetailedProcess=submitted.AgentDetailedProcess;
                if(submitted.SendOnEnter!=original.SendOnEnter)target.SendOnEnter=submitted.SendOnEnter;
                if(submitted.FollowUpBehavior!=original.FollowUpBehavior)target.FollowUpBehavior=submitted.FollowUpBehavior;
            })){notice.Text=pet.Notice;return false;}
            var saved=pet.Settings.Copy();refreshing=true;
            try{
                if((defaultMode.SelectedIndex==1)==submitted.AgentEnabled)defaultMode.SelectedIndex=saved.AgentEnabled?1:0;
                if(predicted.IsChecked==submitted.ShowPredictedMessages)predicted.IsChecked=saved.ShowPredictedMessages;
                if(plainText.IsChecked==submitted.PlainTextEditor)plainText.IsChecked=saved.PlainTextEditor;
                if(showContext.IsChecked==submitted.ShowContextUsage)showContext.IsChecked=saved.ShowContextUsage;
                if(readReplies.IsChecked==submitted.ReadReplies)readReplies.IsChecked=saved.ReadReplies;
                if(detailed.IsChecked==submitted.AgentDetailedProcess)detailed.IsChecked=saved.AgentDetailedProcess;
                if((sendShortcut.SelectedIndex==0)==submitted.SendOnEnter)sendShortcut.SelectedIndex=saved.SendOnEnter?0:1;
                if((followUp.SelectedIndex==1?"steer":"queue")==submitted.FollowUpBehavior)followUp.SelectedIndex=saved.FollowUpBehavior=="steer"?1:0;
                baseline=saved;
            }finally{refreshing=false;}
            if(submitted.ReadReplies!=original.ReadReplies&&!saved.ReadReplies)pet.StopReplySpeech();pet.RefreshChatAppearance();DraftChanged();return true;
        }
    }
}
