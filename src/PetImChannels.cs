using System.IO;
using Dafeiyu.ImChannels;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        ImChannelManager imChannels;
        public ImChannelManager ImChannels {get{return imChannels??(imChannels=new ImChannelManager(Store.DirectoryPath,()=>Settings.Copy(),DataMaintenance));}}
        async void RestoreImChannels(){if(File.Exists(Path.Combine(Store.DirectoryPath,"im-channels.json")))await ImChannels.RestoreAsync();}
    }
}
