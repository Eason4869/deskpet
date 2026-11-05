using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dafeiyu
{
    // Destinations contain navigation metadata only; never commands or maintenance services.
    internal sealed class SettingsFeatureDestination
    {
        internal readonly int Category,Section;internal readonly string Anchor,Label;internal readonly string[] Aliases;
        internal SettingsFeatureDestination(int category,int section,string anchor,string label,params string[] aliases)
        {Category=category;Section=section;Anchor=anchor;Label=label;Aliases=new[]{label}.Concat(aliases).ToArray();}
    }
    internal static class SettingsFeatureNavigation
    {
        internal static SettingsFeatureDestination[] Destinations()
        {
            return new[]{
                new SettingsFeatureDestination(1,0,"TextServiceUrl",TextResources.Get("settingsLayout.modelAddress"),"API","base url","服务地址"),
                new SettingsFeatureDestination(1,0,"TextServiceProxyAddress",TextResources.Get("settingsLayout.modelProxy"),"proxy","代理"),
                new SettingsFeatureDestination(1,1,"TranscriptionModel",TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeC")),
                new SettingsFeatureDestination(1,2,"SpeechVoice",TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeD")),
                new SettingsFeatureDestination(1,3,"LiveVoice",TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeE")),
                new SettingsFeatureDestination(4,-1,"CheckUpdates",TextResources.Get("settingsLayout.aboutTitle"),TextResources.Get("aboutPanel.aboutPanel.text.alternativeA")),
                new SettingsFeatureDestination(6,2,"ShowContextUsage",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAL")),
                new SettingsFeatureDestination(6,2,"AgentDetailedProcess",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAF")),
                new SettingsFeatureDestination(3,2,"GeneralArchivedConversations",TextResources.Get("settingsLayout.archives")),
                new SettingsFeatureDestination(3,2,"GeneralClearCache",TextResources.Get("cache.title")),
                new SettingsFeatureDestination(6,0,"DefaultConversationMode",TextResources.Get("settingsLayout.defaultMode")),
                new SettingsFeatureDestination(6,1,"SendShortcut",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAN"),"Enter","Ctrl+Enter"),
                new SettingsFeatureDestination(6,1,"FollowUpBehavior",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAP")),
                new SettingsFeatureDestination(6,1,"ShowPredictedMessages",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAH")),
                new SettingsFeatureDestination(6,1,"PlainTextEditor",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAJ")),
                new SettingsFeatureDestination(6,2,"ReadReplies",TextResources.Get("settingsLayout.readReplies")),
                new SettingsFeatureDestination(2,0,"AgentWorkspace",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeC")),
                new SettingsFeatureDestination(2,0,"AgentApprovalMode",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeE")),
                new SettingsFeatureDestination(2,0,"SandboxReadDirectories",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeI"),"sandbox","沙箱"),
                new SettingsFeatureDestination(2,0,"AgentTerminalPolicy",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeM")),
                new SettingsFeatureDestination(2,0,"AgentBackupFiles",TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeW")),
                new SettingsFeatureDestination(2,1,"AgentSystemPrompt",TextResources.Get("workSettingsPanel.workSettingsPanel.tabsSettingsPageHeader.alternativeB")),
                new SettingsFeatureDestination(7,0,"WebSearchEnabled",TextResources.Get("webSearch.title"),"Tavily","DeepSeek","SerpApi","Baidu","百度","Bocha","Brave","Firecrawl","Exa","AnySearch","Doubao","豆包","web search","联网搜索"),
                new SettingsFeatureDestination(7,2,"ToolsChannelsTab",TextResources.Get("im.title")),
                new SettingsFeatureDestination(3,0,"GeneralLanguage",TextResources.Get("settingsLayout.language"),"language"),
                new SettingsFeatureDestination(3,0,"ThemeModePicker",TextResources.Get("quietPanel.quietPanel.text"),"theme"),
                new SettingsFeatureDestination(3,2,"GeneralHistoryTab",TextResources.Get("dataManagement.title")),
                new SettingsFeatureDestination(3,2,"ClearAllHistory",TextResources.Get("dataManagement.clearHistory"),TextResources.Get("settingsSearch.clearHistoryAlias")),
                new SettingsFeatureDestination(3,2,"RestoreDefaultPreferences",TextResources.Get("dataManagement.restorePreferences"),TextResources.Get("settingsSearch.resetAlias")),
                new SettingsFeatureDestination(0,-1,"CompanionRelationshipSettings",TextResources.Get("relationship.home.settings")),
                new SettingsFeatureDestination(0,-1,"CompanionHomePage",TextResources.Get("relationship.home.title"),TextResources.Get("relationship.home.open"))
            };
        }
        internal static SettingsFeatureDestination Match(SettingsFeatureDestination[] destinations,string query,int category=-1)
        {
            if(String.IsNullOrWhiteSpace(query))return null;query=query.Trim();
            var candidates=destinations.Where(d=>category<0||d.Category==category);
            return candidates.FirstOrDefault(d=>d.Aliases.Any(a=>String.Equals(a,query,StringComparison.CurrentCultureIgnoreCase)))??candidates.FirstOrDefault(d=>d.Aliases.Any(a=>a.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0));
        }
    }
    public sealed partial class SettingsPage
    {
        TextBox featureSearch;SettingsFeatureDestination[] features;bool changingRoute;string featureAnchor;
        public int SelectedSecondarySection{get{return activeCategory==0?companionPage.SelectedSection:activeCategory==1?(aiConfiguration==null?0:aiConfiguration.SelectedSection):activeCategory==3?(quiet==null?0:quiet.SelectedSection):activeCategory==6?(conversationSettings==null?0:conversationSettings.SelectedSection):activeCategory==7?(toolsSettings==null?0:toolsSettings.SelectedSection):-1;}}
        internal string SelectedFeatureAnchor{get{return featureAnchor;}}
        void InitializeFeatureNavigation(TextBox search,TextBlock hint,StackPanel searchHeader)
        {
            featureSearch=search;features=SettingsFeatureNavigation.Destinations();
            var empty=LocalizedUi.Text(()=>(TextResources.Get("settingsSearch.noResults")),12,Ui.Muted);empty.Name="SettingsSearchEmpty";empty.Margin=new Thickness(8,10,8,0);empty.Visibility=Visibility.Collapsed;searchHeader.Children.Add(empty);
            search.TextChanged+=delegate{
                features=SettingsFeatureNavigation.Destinations();string query=search.Text.Trim();hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;int visible=0;
                for(int i=0;i<navigation.Items.Count;i++){
                    var item=(ListBoxItem)navigation.Items[i];var match=SettingsFeatureNavigation.Match(features,query,CategoryAt(i));string title=(string)item.Content;
                    item.Visibility=query.Length==0||title.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0||match!=null?Visibility.Visible:Visibility.Collapsed;
                    item.ToolTip=match==null?title:title+" · "+match.Label;AutomationProperties.SetHelpText(item,match==null?"":match.Label);if(item.Visibility==Visibility.Visible)visible++;
                }
                if(SettingsFeatureNavigation.Match(features,query,4)!=null)visible++;empty.Visibility=visible==0?Visibility.Visible:Visibility.Collapsed;
            };
            search.PreviewKeyDown+=delegate(object sender,KeyEventArgs args){
                if(args.Key==Key.Escape){search.Clear();args.Handled=true;}
                else if(args.Key==Key.Enter){args.Handled=true;var category=navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i=>String.Equals((string)i.Content,search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));var match=SettingsFeatureNavigation.Match(features,search.Text);if(category!=null)SelectNavigation(Int32.Parse(category.Uid,System.Globalization.CultureInfo.InvariantCulture));else if(match!=null)ActivateFeature(match);else{var first=navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Visibility==Visibility.Visible);if(first!=null)SelectNavigation(Int32.Parse(first.Uid,System.Globalization.CultureInfo.InvariantCulture));}}
                else if(args.Key==Key.Down){args.Handled=true;var first=navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Visibility==Visibility.Visible);if(first!=null){navigation.SelectedItem=first;first.Focus();}}
            };
            navigation.PreviewKeyDown+=delegate(object sender,KeyEventArgs args){if(args.Key==Key.Enter){args.Handled=true;if(!ActivateSearchCategory(activeCategory))SelectNavigation(activeCategory);}else if(args.Key==Key.Escape){args.Handled=true;search.Clear();search.Focus();}};
            navigation.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs args){var item=ItemsControl.ContainerFromElement(navigation,args.OriginalSource as DependencyObject) as ListBoxItem;if(item!=null&&ActivateSearchCategory(Int32.Parse(item.Uid,System.Globalization.CultureInfo.InvariantCulture)))args.Handled=true;};
            companionPage.SelectionChanged+=delegate{SecondaryChanged(0);};
        }
        bool ActivateSearchCategory(int index)
        {
            if(changingRoute||index<0||featureSearch==null)return false;
            var item=NavigationItem(index);if(item==null)return false;string title=(string)item.Content;if(String.Equals(title,featureSearch.Text.Trim(),StringComparison.CurrentCultureIgnoreCase))return false;
            var match=SettingsFeatureNavigation.Match(features,featureSearch.Text,index);if(match==null)return false;ActivateFeature(match);return true;
        }
        void ActivateFeature(SettingsFeatureDestination destination)
        {
            if(destination.Anchor=="CompanionHomePage"||destination.Anchor=="CompanionRelationshipSettings"){OpenCompanionFeature(destination.Anchor);return;}
            RestoreNavigation(destination.Category,destination.Section,destination.Anchor);
        }
        void OpenCompanionFeature(string anchor)
        {var host=pet.EnsureAppWindow();host.ShowCompanionPage(anchor);pet.ShowAppWindow(host);}
        void NotifyNavigation(){if(!changingRoute&&NavigationChanged!=null)NavigationChanged(activeCategory);}
        void SecondaryChanged(int category){if(changingRoute||activeCategory!=category)return;featureAnchor=null;NotifyNavigation();}
        internal void RestoreNavigation(int category,int section,string anchor,bool clearSearch=false)
        {
            if(category==5){category=7;section=2;}if(anchor=="ClearCache"){category=3;section=2;anchor="GeneralClearCache";}
            if(category==7&&section==1){if(anchor=="PluginsSkillsTab"){var host=pet.EnsureAppWindow();host.ShowPluginsPage();pet.ShowAppWindow(host);}else pet.ShowMcpPage(anchor=="AddMcpServer");return;}
            changingRoute=true;
            try{
                if(category==2&&!CanChangeWorkSettingsProject(pet.CurrentEditingProject))return;
                EnsureCategory(category,section);
                if(clearSearch&&featureSearch!=null)featureSearch.Clear();
                if(section>=0){if(category==0)companionPage.SelectSection(section);else if(category==1)aiConfiguration.SelectSection(section);else if(category==3)quiet.SelectSection(section);else if(category==6)conversationSettings.SelectSection(section);else if(category==7)toolsSettings.SelectSection(section);else if(category==2)work.SelectSection(section);}
                var item=NavigationItem(category);if(item==null){navigation.SelectedIndex=-1;ActivateCategory(category);}else if(ReferenceEquals(navigation.SelectedItem,item))ActivateCategory(category);else navigation.SelectedItem=item;featureAnchor=anchor;
                if(!String.IsNullOrEmpty(anchor)){if(category==1)connections.ExpandConnectionFor(anchor);UpdateLayout();var target=FindControl(anchor);SettingsSurface.Reveal(target);}
            }finally{changingRoute=false;}
            NotifyNavigation();
        }
    }
}
