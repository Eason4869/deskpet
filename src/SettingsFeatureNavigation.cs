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
                new SettingsFeatureDestination(4,-1,"ClearCache",TextResources.Get("cache.title")),
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
        public int SelectedSecondarySection{get{return navigation.SelectedIndex==0?companionPage.SelectedSection:navigation.SelectedIndex==1?(aiConfiguration==null?0:aiConfiguration.SelectedSection):navigation.SelectedIndex==3?(quiet==null?0:quiet.SelectedSection):-1;}}
        internal string SelectedFeatureAnchor{get{return featureAnchor;}}
        void InitializeFeatureNavigation(TextBox search,TextBlock hint,StackPanel searchHeader)
        {
            featureSearch=search;features=SettingsFeatureNavigation.Destinations();
            var empty=LocalizedUi.Text(()=>(TextResources.Get("settingsSearch.noResults")),12,Ui.Muted);empty.Name="SettingsSearchEmpty";empty.Margin=new Thickness(8,10,8,0);empty.Visibility=Visibility.Collapsed;searchHeader.Children.Add(empty);
            search.TextChanged+=delegate{
                string query=search.Text.Trim();hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;int visible=0;
                for(int i=0;i<navigation.Items.Count;i++){
                    var item=(ListBoxItem)navigation.Items[i];var match=SettingsFeatureNavigation.Match(features,query,i);string title=(string)item.Content;
                    item.Visibility=query.Length==0||title.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0||match!=null?Visibility.Visible:Visibility.Collapsed;
                    item.ToolTip=match==null?title:title+" · "+match.Label;AutomationProperties.SetHelpText(item,match==null?"":match.Label);if(item.Visibility==Visibility.Visible)visible++;
                }
                empty.Visibility=visible==0?Visibility.Visible:Visibility.Collapsed;
            };
            search.PreviewKeyDown+=delegate(object sender,KeyEventArgs args){
                if(args.Key==Key.Escape){search.Clear();args.Handled=true;}
                else if(args.Key==Key.Enter){args.Handled=true;var category=navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i=>String.Equals((string)i.Content,search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));var match=SettingsFeatureNavigation.Match(features,search.Text);if(category!=null)SelectNavigation(navigation.Items.IndexOf(category));else if(match!=null)ActivateFeature(match);else{var first=navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Visibility==Visibility.Visible);if(first!=null)SelectNavigation(navigation.Items.IndexOf(first));}}
                else if(args.Key==Key.Down){args.Handled=true;var first=navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i=>i.Visibility==Visibility.Visible);if(first!=null){navigation.SelectedItem=first;first.Focus();}}
            };
            navigation.PreviewKeyDown+=delegate(object sender,KeyEventArgs args){if(args.Key==Key.Enter){args.Handled=true;if(!ActivateSearchCategory(navigation.SelectedIndex))SelectNavigation(navigation.SelectedIndex);}else if(args.Key==Key.Escape){args.Handled=true;search.Clear();search.Focus();}};
            navigation.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs args){var item=ItemsControl.ContainerFromElement(navigation,args.OriginalSource as DependencyObject) as ListBoxItem;if(item!=null&&ActivateSearchCategory(navigation.Items.IndexOf(item)))args.Handled=true;};
            companionPage.SelectionChanged+=delegate{SecondaryChanged(0);};
        }
        bool ActivateSearchCategory(int index)
        {
            if(changingRoute||index<0||featureSearch==null)return false;
            string title=(string)((ListBoxItem)navigation.Items[index]).Content;if(String.Equals(title,featureSearch.Text.Trim(),StringComparison.CurrentCultureIgnoreCase))return false;
            var match=SettingsFeatureNavigation.Match(features,featureSearch.Text,index);if(match==null)return false;ActivateFeature(match);return true;
        }
        void ActivateFeature(SettingsFeatureDestination destination)
        {
            if(destination.Anchor=="CompanionHomePage"||destination.Anchor=="CompanionRelationshipSettings"){OpenCompanionFeature(destination.Anchor);return;}
            RestoreNavigation(destination.Category,destination.Section,destination.Anchor);
        }
        void OpenCompanionFeature(string anchor)
        {var host=pet.EnsureAppWindow();host.ShowCompanionPage(anchor);pet.ShowAppWindow(host);}
        void NotifyNavigation(){if(!changingRoute&&NavigationChanged!=null)NavigationChanged(navigation.SelectedIndex);}
        void SecondaryChanged(int category){if(changingRoute||navigation.SelectedIndex!=category)return;featureAnchor=null;NotifyNavigation();}
        internal void RestoreNavigation(int category,int section,string anchor,bool clearSearch=false)
        {
            changingRoute=true;
            try{
                if(category==2&&!CanChangeWorkSettingsProject(pet.CurrentEditingProject))return;
                EnsureCategory(category,section);
                if(clearSearch&&featureSearch!=null)featureSearch.Clear();
                if(section>=0){if(category==0)companionPage.SelectSection(section);else if(category==1)aiConfiguration.SelectSection(section);else if(category==3)quiet.SelectSection(section);}
                if(navigation.SelectedIndex==category)ActivateCategory(category);else navigation.SelectedIndex=category;featureAnchor=anchor;
                if(!String.IsNullOrEmpty(anchor)){UpdateLayout();var target=FindControl(anchor);if(target!=null){target.BringIntoView();if(target.Focusable&&target.IsEnabled)target.Focus();}}
            }finally{changingRoute=false;}
            NotifyNavigation();
        }
    }
}
