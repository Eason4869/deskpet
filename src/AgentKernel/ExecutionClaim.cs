using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace Dafeiyu
{
    public sealed class ExecutionClaimStore
    {
        [DataContract] sealed class Claim{[DataMember] public string TaskId,CallId,State;[DataMember] public DateTime CreatedUtc;}
        readonly object gate=new object();readonly string directory;readonly Dictionary<string,Claim> claims=new Dictionary<string,Claim>();
        public ExecutionClaimStore(string root){directory=Path.GetFullPath(root);Load();}
        public bool TryClaim(string taskId,string callId)
        {
            lock(gate){Claim existing;if(claims.TryGetValue(taskId,out existing) && existing.State=="running")return false;claims[taskId]=new Claim{TaskId=taskId,CallId=callId,State="running",CreatedUtc=DateTime.UtcNow};Save();return true;}
        }
        public bool Complete(string taskId,string callId){lock(gate){Claim claim;if(!claims.TryGetValue(taskId,out claim)||claim.CallId!=callId||claim.State!="running")return false;claim.State="complete";Save();return true;}}
        public bool Fail(string taskId,string callId){lock(gate){Claim claim;if(!claims.TryGetValue(taskId,out claim)||claim.CallId!=callId||claim.State!="running")return false;claim.State="failed";Save();return true;}}
        public IList<string> Interrupted{get{lock(gate)return claims.Values.Where(c=>c.State=="running").Select(c=>c.TaskId).ToArray();}}
        void Load(){string path=Path.Combine(directory,"claims.json");if(!File.Exists(path))return;using(var stream=File.OpenRead(path)){var saved=(List<Claim>)new DataContractJsonSerializer(typeof(List<Claim>)).ReadObject(stream);foreach(var claim in saved??new List<Claim>())if(claim!=null&&!String.IsNullOrEmpty(claim.TaskId))claims[claim.TaskId]=claim;}}
        void Save(){Directory.CreateDirectory(directory);string path=Path.Combine(directory,"claims.json");using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read))new DataContractJsonSerializer(typeof(List<Claim>)).WriteObject(stream,claims.Values.ToList());}
    }
}
