using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class UpdateChannelTests
    {
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Update channel tests passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
        static async Task Run()
        {
            var method=typeof(GitHubUpdates).GetMethod("CheckAsync",new[]{typeof(string),typeof(bool),typeof(string),typeof(bool),typeof(UpdateChannel),typeof(CancellationToken)});
            if(method==null)throw new Exception("missing update channel discovery");
            foreach(var channel in new[]{UpdateChannel.Stable,UpdateChannel.Preview})using(var handler=new Handler())using(var client=new GitHubUpdates(new Uri("https://api.github.com/"),handler)) {
                var release=await (Task<UpdateRelease>)method.Invoke(client,new object[]{ReleaseInfo.RepositoryUrl,false,null,true,channel,CancellationToken.None});
                string expected=channel==UpdateChannel.Stable?"v0.9.4":"v0.9.5-dev.10";
                if(release.VersionTag!=expected || release.AssetName!="DeskPet-Windows-"+expected.Substring(1)+".zip")throw new Exception("wrong channel winner: "+release.VersionTag);
            }
            using(var handler=new Handler{MissingAsset=true})using(var client=new GitHubUpdates(new Uri("https://api.github.com/"),handler)) {
                try{await (Task<UpdateRelease>)method.Invoke(client,new object[]{ReleaseInfo.RepositoryUrl,false,null,true,UpdateChannel.Preview,CancellationToken.None});throw new Exception("incomplete highest release accepted");}catch(ApiException){}
            }
            var cfg=new PetSettings();var property=typeof(PetSettings).GetProperty("UpdateChannel");
            if(property==null || (UpdateChannel)property.GetValue(cfg,null)!=(ReleaseVersion.Parse(ReleaseInfo.DisplayVersion).IsPreview?UpdateChannel.Preview:UpdateChannel.Stable))throw new Exception("missing stable configuration default");
            using(var stream=new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"))){var old=(PetSettings)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream);old.Normalize();if(old.UpdateChannel!=UpdateChannel.Stable)throw new Exception("old settings lost stable default");}
            property.SetValue(cfg,UpdateChannel.Preview,null);if((UpdateChannel)property.GetValue(cfg.Copy(),null)!=UpdateChannel.Preview)throw new Exception("channel preference lost in settings copy");
        }
        sealed class Handler:HttpMessageHandler
        {
            public bool MissingAsset;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                string path=request.RequestUri.AbsolutePath;object result;
                if(path.Contains("/git/ref/"))result=new { @object=new {type="tag",sha=new string('b',40)} };
                else if(path.Contains("/git/tags/"))result=new { @object=new {type="commit",sha=new string('a',40)} };
                else result=new[]{"0.9.4","0.9.5-dev.2","0.9.5-dev.10","0.9.9-dev.1","0.9.8-dev.1"}.Select(v=>new {
                    tag_name="v"+v,html_url=ReleaseInfo.RepositoryUrl+"/releases/tag/v"+v,
                    prerelease=v.Contains("-dev"),draft=v=="0.9.9-dev.1"||v=="0.9.8-dev.1",
                    assets=MissingAsset&&v=="0.9.5-dev.10"?new object[0]:new object[]{new{name="DeskPet-Windows-"+v+".zip",size=4,digest="sha256:"+new string('a',64),browser_download_url=ReleaseInfo.RepositoryUrl+"/releases/download/v"+v+"/DeskPet-Windows-"+v+".zip"}}
                }).ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JavaScriptSerializer().Serialize(result))});
            }
        }
    }
}
