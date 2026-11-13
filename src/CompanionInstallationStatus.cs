using System;
namespace Dafeiyu
{
    // Presentation only. Selection and transfer ownership stay in the controller.
    public sealed class CompanionInstallationStatus
    {
        public long Request {get;private set;}
        public string PickerKey {get;private set;}
        public string Origin {get;private set;}
        public string Stage {get;private set;}
        public OfficialCompanionProgress Progress {get;private set;}
        public bool Dismissed {get;private set;}
        bool installing;
        public bool Active {get{return installing&&(Stage=="installing"||Stage=="progress"||Stage=="prepared"||Stage=="failed"||Stage=="reselect-required");}}
        public bool CanCancel {get{return Active&&Stage!="failed"&&Stage!="reselect-required";}}
        public bool CanRetry {get{return Active&&Stage=="failed";}}
        public bool Visible(bool petVisible){return Active&&!Dismissed&&petVisible;}
        public void Dismiss(){Dismissed=true;}
        public void Accept(CompanionSelectionUpdate update)
        {
            if(update.Request<Request)return;
            if(update.Stage=="requested"){
                Request=update.Request;PickerKey=update.PickerKey;Origin=update.Origin;Stage=update.Stage;
                Progress=null;Dismissed=false;installing=false;return;
            }
            if(update.Request!=Request||update.PickerKey!=PickerKey||Origin=="StartupRestore")return;
            Stage=update.Stage;Progress=update.Progress??Progress;
            if(Stage=="installing")installing=true;
        }
        public string Text {
            get {
                if(!Active)return "";
                var id=PickerKey!=null&&PickerKey.StartsWith("official:",StringComparison.Ordinal)?PickerKey.Substring(9):"";
                var profile=OfficialCompanionProfiles.Find(id);var name=profile==null?"":profile.DisplayName;
                if(Stage=="failed")return TextResources.Format("companions.install.failed",name);
                if(Stage=="reselect-required")return TextResources.Format("companions.install.reselect",name);
                string result=TextResources.Format("companions.install.preparing",name);
                if(Progress!=null&&Progress.TotalBytes>0)result+=" · "+Progress.Percent.ToString("0")+"%";
                return result;
            }
        }
    }
}
