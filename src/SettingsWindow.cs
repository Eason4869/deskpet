using System;
using System.Windows;
using System.Windows.Interop;

namespace Dafeiyu
{
    // Compatibility host for existing automation. Production routes host Page in ChatWindow.
    public sealed class SettingsWindow : Window
    {
        public SettingsPage Page {get;private set;}
        public SettingsWindow(PetWindow companion)
        {
            if(companion==null)throw new ArgumentNullException("companion");
            LocalizedUi.Window(this,()=>(TextResources.Get("settingsWindow.settingsWindow.window")),960,790);
            MinWidth=Math.Min(820,Math.Max(320,SystemParameters.WorkArea.Width-36));
            MinHeight=Math.Min(600,Math.Max(320,SystemParameters.WorkArea.Height-36));
            Background=GlassSurface.Background();Icon=Ui.ApplicationIcon();Page=new SettingsPage(companion);Content=Page;
            Closed+=delegate {Page.Dispose();};
            Loaded+=delegate {if(WindowState==WindowState.Normal)DesktopInterop.PlaceBeside(new WindowInteropHelper(this).Handle,new WindowInteropHelper(companion).Handle);};
        }
        public event Action GuideRequested {add{Page.GuideRequested+=value;}remove{Page.GuideRequested-=value;}}
        public bool HasUnsavedEdits {get{return Page.HasUnsavedEdits;}}
        public bool IsOnAboutPage {get{return Page.IsOnAboutPage;}}
        public int SelectedNavigationIndex {get{return Page.SelectedNavigationIndex;}}
        public event Action<int> NavigationChanged {add{Page.NavigationChanged+=value;}remove{Page.NavigationChanged-=value;}}
        public void SelectNavigation(int index){Page.SelectNavigation(index);}
        public void RefreshTextProviders(){Page.RefreshTextProviders();}
        public void RefreshState(){Page.RefreshState();}
        public void ShowNotice(string text){Page.ShowNotice(text);}
        public void SelectConnections(){Page.SelectConnections();}
        public void SelectPage(int index){Page.SelectPage(index);}
        public void SelectCompanionSection(int index){Page.SelectCompanionSection(index);}
        public void SelectAiSection(int index){Page.SelectAiSection(index);}
        public void SelectGeneralSection(int index){Page.SelectGeneralSection(index);}
        internal void SelectMcpSettings(){Page.SelectMcpSettings();}
        public void SelectWorkSettings(){Page.SelectWorkSettings();}
        public bool ValidateGuideNetwork(out string error){return Page.ValidateGuideNetwork(out error);}
        public bool SaveGuideNetwork(){return Page.SaveGuideNetwork();}
        public bool SaveAiConfiguration(){return Page.SaveAiConfiguration();}
        public bool SaveGuideAiSection(int index){return Page.SaveGuideAiSection(index);}
        internal int GuideSpeechTestRevision {get{return Page.GuideSpeechTestRevision;}}
        internal bool GuideSpeechTestSucceeded {get{return Page.GuideSpeechTestSucceeded;}}
        public bool ValidateGuideAiSection(int index,out string error){return Page.ValidateGuideAiSection(index,out error);}
        public FrameworkElement FindControl(string name){return Page.FindControl(name);}
        public FrameworkElement FocusGuideTarget(string name){return Page.FocusGuideTarget(name);}
        public FrameworkElement ShowGuideTarget(string name){return Page.ShowGuideTarget(name);}
    }
}
