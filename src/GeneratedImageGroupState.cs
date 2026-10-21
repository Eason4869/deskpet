using System;
using System.Collections.Generic;
namespace Dafeiyu {
public sealed class GeneratedImageGroupState {
readonly List<string> keys=new List<string>();readonly Dictionary<string,GeneratedImage> images=new Dictionary<string,GeneratedImage>();
public IList<string> Keys {get{return keys.AsReadOnly();}} public string SelectedId {get;private set;}
public GeneratedImage SelectedImage {get{return ImageFor(SelectedId);}}
public GeneratedImage ImageFor(string id){GeneratedImage value;return id!=null&&images.TryGetValue(id,out value)?value.Copy():null;}
public bool Select(string id){if(!keys.Contains(id))return false;SelectedId=id;return true;}
public void Update(IEnumerable<string> taskIds,IList<GeneratedImage> assets,Func<string,GeneratedImage> find){
var next=new List<string>();var mapped=new Dictionary<string,GeneratedImage>();
foreach(var id in taskIds??new string[0]){if(String.IsNullOrEmpty(id)||next.Contains(id))continue;next.Add(id);GeneratedImage image=null;foreach(var asset in assets)if(asset.ImageJobId==id){image=asset;break;}if(image==null&&find!=null)image=find(id);if(image!=null)mapped[id]=image.Copy();}
foreach(var asset in assets){bool found=false;foreach(var value in mapped.Values)if(value.Id==asset.Id)found=true;if(found)continue;string key=!String.IsNullOrEmpty(asset.ImageJobId)&&next.Contains(asset.ImageJobId)?asset.ImageJobId:asset.Id;if(!next.Contains(key))next.Add(key);mapped[key]=asset.Copy();}
keys.Clear();keys.AddRange(next);images.Clear();foreach(var pair in mapped)images[pair.Key]=pair.Value;if(!keys.Contains(SelectedId))SelectedId=keys.Count>0?keys[0]:null;
}
}}
