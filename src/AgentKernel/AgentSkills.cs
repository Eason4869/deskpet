using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace Dafeiyu
{
    public sealed class AgentSkillInfo {public string Name,Description,Directory;}
    public sealed class AgentSkills
    {
        readonly Dictionary<string,AgentSkillInfo> skills=new Dictionary<string,AgentSkillInfo>(StringComparer.Ordinal);
        public IList<AgentSkillInfo> Catalog {get{return skills.Values.OrderBy(s=>s.Name).Select(s=>new AgentSkillInfo{Name=s.Name,Description=s.Description,Directory=s.Directory}).ToArray();}}
        public AgentSkills(IEnumerable<string> directories,IEnumerable<AgentSkillInfo> managedSkills=null)
        {
            foreach(string directory in directories??new string[0]){
                if(String.IsNullOrWhiteSpace(directory)||!System.IO.Directory.Exists(directory))continue;AgentInstructions.RejectLinks(directory);
                foreach(string folder in System.IO.Directory.EnumerateDirectories(directory).Take(129)){
                    if(skills.Count>=128)throw new InvalidOperationException(TextResources.Get("agentSkills.agentSkills.text"));AgentInstructions.RequireWithin(directory,folder);string path=Path.Combine(folder,"SKILL.md");if(!File.Exists(path))continue;AgentInstructions.RejectLinks(path);
                    Add(ReadInfo(folder));
                }
            }
            foreach(var item in managedSkills??new AgentSkillInfo[0]){var info=ReadInfo(item.Directory);if(info.Name!=item.Name)throw new InvalidOperationException(TextResources.Get("agentSkills.agentSkills.text.alternativeA"));Add(info);}
        }
        void Add(AgentSkillInfo info){if(skills.Count>=128)throw new InvalidOperationException(TextResources.Get("agentSkills.add.text"));if(skills.ContainsKey(info.Name))throw new InvalidOperationException(TextResources.Format("agentSkills.add.template",info.Name));skills.Add(info.Name,info);}
        internal static AgentSkillInfo ReadInfo(string folder)
        {
            folder=Path.GetFullPath(folder);string path=Path.Combine(folder,"SKILL.md");AgentInstructions.RequireWithin(folder,path);string body=AgentInstructions.Read(path).Content;
            return ParseInfo(body,folder);
        }
        internal static AgentSkillInfo ParseInfo(string body,string folder)
        {
            if(body==null||Encoding.UTF8.GetByteCount(body)>65536)throw new InvalidOperationException(TextResources.Get("agentSkills.parseInfo.text"));
            body=body.TrimStart('\ufeff');
            // Read only a small YAML scalar subset; tags, aliases and executable objects are never interpreted.
            var frontMatter=Regex.Match(body,@"\A---[ \t]*\r?\n(?<header>[\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)");
            if(!frontMatter.Success||frontMatter.Length>4096)throw new InvalidOperationException(TextResources.Format("agentSkills.parseInfo.template",folder));
            string header=frontMatter.Groups["header"].Value;
            string name=Scalar(header,"name"),summary=Scalar(header,"description");
            if(!Regex.IsMatch(name,@"\A[a-z0-9][a-z0-9_-]{0,63}\z")||String.IsNullOrWhiteSpace(summary))throw new InvalidOperationException(TextResources.Format("agentSkills.parseInfo.template.alternativeA",folder));
            if(summary.Length>512)summary=summary.Substring(0,512);
            return new AgentSkillInfo{Name=name,Description=summary,Directory=folder};
        }
        static string Scalar(string header,string key)
        {
            var matches=Regex.Matches(header,@"(?m)^"+key+@":([^\r\n]*)\r?$");
            if(matches.Count!=1)throw new InvalidOperationException(TextResources.Format("agentSkills.scalar.template",key));
            var match=matches[0];string value=match.Groups[1].Value.Trim();
            var continuation=Regex.Match(header.Substring(match.Index+match.Length),@"\A(?:\r?\n[ \t]+[^\r\n]*)+").Value;
            if(value=="|"||value=="|-"||value=="|+"||value==">"||value==">-"||value==">+")return String.Join(value[0]=='>'?" ":"\n",continuation.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim())).Trim();
            if(continuation.Length>0)value+=" "+String.Join(" ",continuation.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()));
            if(value.StartsWith("\"")&&value.EndsWith("\"")){
                try{return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(value);}catch(ArgumentException){throw new InvalidOperationException(TextResources.Get("agentSkills.scalar.text"));}}
            if(value.StartsWith("'")&&value.EndsWith("'"))return value.Substring(1,value.Length-2).Replace("''","'");
            if(value.StartsWith("\"")||value.StartsWith("'")||value.StartsWith("!")||value.StartsWith("&")||value.StartsWith("*"))throw new InvalidOperationException(TextResources.Get("agentSkills.scalar.text.alternativeA"));
            int comment=value.IndexOf(" #",StringComparison.Ordinal);return comment<0?value:value.Substring(0,comment).TrimEnd();
        }
        public AgentInstructionSource Load(string name,string resource=null)
        {
            AgentSkillInfo skill;if(!skills.TryGetValue(name??"",out skill))throw new ArgumentException(TextResources.Get("agentSkills.load.text"));string path=Path.Combine(skill.Directory,String.IsNullOrEmpty(resource)?"SKILL.md":resource);AgentInstructions.RequireWithin(skill.Directory,path);return AgentInstructions.Read(path);
        }
    }
    public sealed partial class AgentWorkspace
    {
        AgentSkills skills;bool trustInstructions;string globalInstruction;
        internal string ProjectInstructionText {get;private set;}
        internal void InheritInstructions(AgentWorkspace source)
        {
            trustInstructions=source.trustInstructions;globalInstruction=source.globalInstruction;skills=source.skills;ProjectInstructionText=source.ProjectInstructionText;
            foreach(var definition in source.dynamicTools.Where(t=>t.Name=="read_instructions"||t.Name=="load_skill"))dynamicTools.Add(new AgentToolDefinition{Name=definition.Name,Description=definition.Description,ReadOnly=true,Properties=new Dictionary<string,object>(definition.Properties),Required=(string[])definition.Required.Clone()});BuildSchemas();
        }
        internal void ConfigureInstructions(bool trusted,string globalPath="",string skillDirectory="")
        {
            trustInstructions=trusted;globalInstruction=globalPath;ProjectInstructionText="";dynamicTools.RemoveAll(d=>d.Name=="load_skill"||d.Name=="read_instructions");
            var managed=new AgentSkillInfo[0];if(!String.IsNullOrWhiteSpace(ManagedSkillLibrary.ActiveDirectory))managed=new ManagedSkillLibrary(ManagedSkillLibrary.ActiveDirectory).List().Where(s=>s.Enabled).Select(s=>new AgentSkillInfo{Name=s.Name,Description=s.Description,Directory=s.Directory}).ToArray();
            skills=new AgentSkills(trusted?new[]{Path.Combine(Root,".agents","skills"),skillDirectory}:new string[0],managed);
            if(trusted){ProjectInstructionText=FormatInstructions(AgentInstructions.Load(Root,Root,true,globalPath));
                dynamicTools.Add(new AgentToolDefinition{Name="read_instructions",Description=TextResources.Get("agentSkills.configureInstructions.description"),ReadOnly=true,Properties=new Dictionary<string,object>{{"path",new{type="string"}}},Required=new string[0]});
            }
            if(skills.Catalog.Count>0){dynamicTools.Add(new AgentToolDefinition{Name="load_skill",Description=TextResources.Get("agentSkills.configureInstructions.description.alternativeA"),ReadOnly=true,Properties=new Dictionary<string,object>{{"name",new{type="string"}},{"resource",new{type="string"}}},Required=new[]{"name"}});ProjectInstructionText+=TextResources.Format("agentSkills.configureInstructions.scriptSerializationJavaScriptSerializer",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(skills.Catalog));}
            BuildSchemas();
        }
        string InstructionTool(string name,Dictionary<string,object> args)
        {
            if(name=="load_skill"){if(skills==null)throw new UnauthorizedAccessException(TextResources.Get("agentSkills.instructionTool.text"));return FormatInstructions(new[]{skills.Load(GetString(args,"name",null),GetString(args,"resource",""))});}
            if(!trustInstructions)throw new UnauthorizedAccessException(TextResources.Get("agentSkills.instructionTool.text.alternativeA"));
            return FormatInstructions(AgentInstructions.Load(Root,Resolve(GetString(args,"path",".")),true,globalInstruction));
        }
        static string FormatInstructions(IEnumerable<AgentInstructionSource> sources){var text=new StringBuilder(TextResources.Get("agentSkills.formatInstructions.textStringBuilder"));foreach(var item in sources)text.Append(TextResources.Get("agentSkills.formatInstructions.textAppend")).Append(item.Path).Append("\nSHA256：").Append(item.Hash).Append('\n').AppendLine(item.Content);return text.ToString();}
    }
}
