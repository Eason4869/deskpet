using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Dafeiyu
{
    [DataContract]
    public sealed class AgentToolEvent
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Summary { get; set; }
        [DataMember] public string Status { get; set; }
        [DataMember] public long ElapsedMilliseconds { get; set; }
        public AgentToolEvent Copy() { return (AgentToolEvent)MemberwiseClone(); }
    }

    // Restricted mode grants authority only to Root; full mode follows the user's OS permissions.
    // No shell commands or file deletion are exposed. Explorer opens directories only.
    public sealed partial class AgentWorkspace : IDisposable
    {
        private const int MaxFileBytes = 1024 * 1024;
        private const int MaxResultChars = 24000;
        private const string BackupFolder = ".deskpet-agent-backups";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly string prefix;
        public string Root { get; private set; }
        public bool RestrictToRoot { get; private set; }
        public bool BackupEnabled { get; set; }
        public int ContextTokenLimit {get;set;}
        internal Action<string> DirectoryOpener { get; set; }
        object[] openAITools,anthropicTools;
        public object[] OpenAITools { get{BuildSchemas();return openAITools;} private set{openAITools=value;} }
        public object[] AnthropicTools { get{BuildSchemas();return anthropicTools;} private set{anthropicTools=value;} }
        private bool readOnly,disposed,desktopSession,githubConnected;private GitHubClient github;private AgentPermissionRuntime kernelPermissions=AgentPermissionRuntime.DefaultWorkspacePolicy();private AgentAuditLog kernelAudit;private IAgentApprovalProvider kernelApproval;private ExecutionClaimStore kernelClaims;private string kernelTaskId;private AgentSessionModel kernelSessionModel;private AgentProfile kernelProfile;private AgentModelCapabilities providerCapabilities;private AgentSubagentManager subagents;private AgentSubagentScheduler subagentScheduler;private AgentToolRuntime kernelTools;private readonly List<AgentToolDefinition> dynamicTools=new List<AgentToolDefinition>();private readonly List<IMcpTransport> mcpTransports=new List<IMcpTransport>();
        public bool PlanMode {get{return readOnly;}set{readOnly=value;BuildSchemas();}}
        public bool ReadOnly {get{return PlanMode;}set{PlanMode=value;}}

        internal static AgentWorkspace ForContextBudget(int budget){return new AgentWorkspace(budget);}
        private AgentWorkspace(int budget){Root=AppDomain.CurrentDomain.BaseDirectory;prefix=Root;ContextTokenLimit=budget;}
        public AgentWorkspace(string root) : this(root, true) { }
        public AgentWorkspace(string root, bool restrictToRoot)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("当前文件 Agent 仅支持 Windows；请关闭文件 Agent 后使用普通聊天。");
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new ArgumentException("工作目录必须是明确选择的绝对路径。");
            RestrictToRoot = restrictToRoot;
            BackupEnabled = true; ContextTokenLimit=32768;
            DirectoryOpener = OpenExplorer;
            ValidateSyntax(root, true, !restrictToRoot);
            string full = Path.GetFullPath(root), drive = Path.GetPathRoot(full);
            Root = String.Equals(full.TrimEnd('\\', '/'), drive.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ? drive : full.TrimEnd('\\', '/');
            prefix = Root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string volume = Path.GetPathRoot(Root).TrimEnd('\\', '/');
            if ((restrictToRoot && String.Equals(Root.TrimEnd('\\', '/'), volume, StringComparison.OrdinalIgnoreCase)) || !Directory.Exists(Root))
                throw new ArgumentException("请选择已存在的项目目录，不能选择磁盘根目录。");
            if (restrictToRoot) foreach (string protectedRoot in ProtectedRoots())
            {
                if (String.IsNullOrEmpty(protectedRoot)) continue;
                string p = Path.GetFullPath(protectedRoot).TrimEnd('\\', '/');
                if (IsWithin(Root, p) || String.Equals(Root, p, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("不能将系统目录设为 Agent 工作目录。");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (restrictToRoot && !String.IsNullOrEmpty(home) && (String.Equals(Root, home, StringComparison.OrdinalIgnoreCase) || IsWithin(home, Root)))
                throw new ArgumentException("请选择具体项目目录，不能选择整个用户目录或其上级。");
            ValidateSegments(Root);
            using (LockDirectories(Root, false)) { }
            BuildSchemas();
        }

        internal void ConfigureGitHub(string repository,ConnectionSettings connection){if(github!=null)github.Dispose();github=new GitHubClient(repository,connection);githubConnected=!String.IsNullOrWhiteSpace(repository);BuildSchemas();}
        internal void ConfigureGitHub(GitHubClient client){if(github!=null)github.Dispose();github=client;githubConnected=client!=null&&!String.IsNullOrWhiteSpace(client.Repository);BuildSchemas();}
        internal void AttachKernelSession(AgentSessionModel model){kernelSessionModel=model;}
        internal void ConfigureProvider(string provider,string model){providerCapabilities=new AgentProviderRegistry().Resolve(provider,model);BuildSchemas();}
        internal void ConfigureCapabilities(AgentCapabilitySnapshot snapshot){providerCapabilities=snapshot.ToLegacy();BuildSchemas();}
        internal void ConfigureMcp(IMcpTransport transport){if(transport==null)throw new ArgumentNullException("transport");if(!mcpTransports.Contains(transport))mcpTransports.Add(transport);if(transport is IMcpConnectionState)kernelPermissions.Add(new AgentPermissionRule{Action="mcp",Resource="**",Effect=AgentPermissionEffect.Ask});kernelTools=new AgentToolRuntime(kernelPermissions,kernelAudit,kernelApproval);RegisterMcpTools();BuildSchemas();}
        public AgentModelCapabilities ProviderCapabilities{get{return providerCapabilities;}}public AgentSubagentManager Subagents{get{return subagents;}}public AgentSubagentScheduler SubagentScheduler{get{return subagentScheduler;}}public AgentToolRuntime KernelTools{get{return kernelTools;}}
        internal void ConfigureAgentProfile(AgentProfile profile){kernelProfile=profile;if(profile!=null)kernelPermissions=new AgentProfileRegistry().PermissionRuntime(profile.Id);kernelTools=new AgentToolRuntime(kernelPermissions,kernelAudit,kernelApproval);RegisterMcpTools();BuildSchemas();}
        internal void ConfigureKernel(AgentPermissionRuntime permissions,AgentAuditLog audit){ConfigureKernel(permissions,audit,null,null,null);}
        internal void ConfigureKernel(AgentPermissionRuntime permissions,AgentAuditLog audit,IAgentApprovalProvider approval,ExecutionClaimStore claims,string taskId){ConfigureKernel(permissions,audit,approval,claims,taskId,null);}
        internal void ConfigureKernel(AgentPermissionRuntime permissions,AgentAuditLog audit,IAgentApprovalProvider approval,ExecutionClaimStore claims,string taskId,AgentSessionModel sessionModel){kernelPermissions=permissions??AgentPermissionRuntime.DefaultWorkspacePolicy();kernelAudit=audit;kernelApproval=approval;kernelClaims=claims;kernelTaskId=taskId;kernelSessionModel=sessionModel;kernelTools=new AgentToolRuntime(kernelPermissions,kernelAudit,kernelApproval);if(kernelClaims!=null){subagents=new AgentSubagentManager(kernelClaims);subagentScheduler=new AgentSubagentScheduler(subagents,4);}RegisterMcpTools();BuildSchemas();}
        internal void ConfigureDesktopSession(){desktopSession=true;BuildSchemas();}
        internal int CumulativeTokenLimit {get;set;}
        internal AgentPermissionRuntime FreezePermissions(){return kernelPermissions.Copy();}
        bool DesktopToolAvailable(string name)
        {
            IMcpConnectionState connection;if(mcpAvailability.TryGetValue(name,out connection) && !connection.Connected)return false;
            if(name=="exec_command")return TerminalAvailable;
            if(name=="spawn_subagent" || name=="interrupt_subagent" || name=="get_subagent" || name=="wait_subagents")return childWorkers!=null;
            if(name=="complete_subagent")return false;
            return !name.StartsWith("github_",StringComparison.Ordinal) || (githubConnected && (AgentPermissionRuntime.ActionForTool(name)=="github_read" || kernelApproval!=null));
        }
        public Task<string> ExecuteAsync(string name,string argumentsJson,CancellationToken token){return ExecuteAsync(name,argumentsJson,Guid.NewGuid().ToString("N"),token);}
        readonly AgentExecutionGuard executionGuard=new AgentExecutionGuard();
        public Task<string> ExecuteAsync(string name,string argumentsJson,string callId,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();if(name=="complete_subagent")return Task.FromResult("错误：只有真实 worker 可以记录子任务完成。");if(ChildToolBudget>0 && Interlocked.Increment(ref childToolCalls)>ChildToolBudget)throw new ApiException("只读子任务工具预算已用尽。");Dictionary<string,object> args;
            try{args=new JavaScriptSerializer{MaxJsonLength=MaxFileBytes*2+4096,RecursionLimit=12}.DeserializeObject(argumentsJson??"{}") as Dictionary<string,object>;}catch(ArgumentException){return Task.FromResult("错误：工具参数必须是 JSON 对象。");}
            if(args==null)return Task.FromResult("错误：工具参数必须是 JSON 对象。");
            if(name=="exec_command")NormalizeTerminalArguments(args);
            string resource=name!=null&&name.StartsWith("github_",StringComparison.Ordinal)&&github!=null?github.Repository+" :: "+AgentPermissionRuntime.ResourceForTool(name,args):null;
            var invocation=new AgentToolInvocation(name,args,new AgentToolExecutionContext{SessionId=kernelSessionModel==null?"":kernelSessionModel.SessionId,ProjectId=kernelSessionModel==null?Root:kernelSessionModel.ProjectId,AgentId=kernelProfile==null?"":kernelProfile.Id,TaskId=kernelTaskId,CallId=callId},resource??dynamicTools.Where(t=>t.Name==name).Select(t=>t.ApprovalResource).FirstOrDefault());
            return executionGuard.RunAsync(invocation,()=>ExecuteRecordedAsync(invocation,token));
        }
        async Task<string> ExecuteAuthorizedAsync(AgentToolInvocation invocation,CancellationToken token,Action beforeEffect)
        {
            string name=invocation.ToolName,argumentsJson=invocation.Arguments,callId=invocation.CallId;
            token.ThrowIfCancellationRequested();
            if(providerCapabilities!=null && !providerCapabilities.CanUseTool(name))return "错误：当前模型未启用工具能力。";
            if((name=="spawn_subagent"||name=="get_subagent"||name=="wait_subagents"||name=="interrupt_subagent")&&childWorkers==null)return "错误：独立 worker 尚未连接，不会执行子任务。";
            if(desktopSession && !DesktopToolAvailable(name))return "错误：此工具尚未在桌面端启用或连接，不会执行操作。";
            if(name=="exec_command")return await ExecuteTerminalAsync(invocation,token,beforeEffect).ConfigureAwait(false);
            if(String.IsNullOrWhiteSpace(callId) || callId.Length>256)throw new ArgumentException("工具调用必须具有稳定 CallId。");Dictionary<string,object> args;try{args=new JavaScriptSerializer{MaxJsonLength=MaxFileBytes*2+4096,RecursionLimit=12}.DeserializeObject(argumentsJson??"{}") as Dictionary<string,object>;}catch(ArgumentException){return "错误：工具参数必须是 JSON 对象。";}if(args==null)return "错误：工具参数必须是 JSON 对象。";if(kernelTools!=null && kernelTools.Has(name)){if(readOnly && !kernelTools.IsReadOnly(name))return "错误：当前为计划模式，只能读取和分析文件，不能调用写入或执行工具。";var kernelContext=new AgentToolExecutionContext{SessionId=kernelSessionModel==null?"":kernelSessionModel.SessionId,AgentId=kernelProfile==null?"":kernelProfile.Id,CallId=callId,TaskId=kernelTaskId,ProjectId=invocation.ProjectId};return await kernelTools.ExecuteAsync(name,args,kernelContext,token,beforeEffect).ConfigureAwait(false);}var action=AgentPermissionRuntime.ActionForTool(name);var resource=AgentPermissionRuntime.ResourceForTool(name,args);
            var decision=await new AgentAuthorizationService(kernelPermissions,kernelApproval).AuthorizeAsync(invocation,token).ConfigureAwait(false);
            if(decision.Effect!=AgentPermissionEffect.Allow){if(kernelAudit!=null)kernelAudit.Record(new AgentAuditEvent{CallId=callId,TaskId=kernelTaskId,Tool=name,Action=action,Resource=resource,Result="denied",CreatedUtc=DateTime.UtcNow});return "错误：权限拒绝："+decision.Reason;}
            token.ThrowIfCancellationRequested();
            if(name=="spawn_subagent"||name=="get_subagent"||name=="wait_subagents"||name=="interrupt_subagent")return await ExecuteChildToolAsync(name,args,token).ConfigureAwait(false);
            if(beforeEffect!=null)beforeEffect();
            try{var result=await ExecuteCoreAsync(name,argumentsJson,callId,token).ConfigureAwait(false);if(kernelAudit!=null)kernelAudit.Record(new AgentAuditEvent{CallId=callId,TaskId=kernelTaskId,Tool=name,Action=action,Resource=resource,Result=result!=null&&result.StartsWith("错误：")?"error":"completed",CreatedUtc=DateTime.UtcNow});return result;}
            catch(Exception){if(kernelAudit!=null)kernelAudit.Record(new AgentAuditEvent{CallId=callId,TaskId=kernelTaskId,Tool=name,Action=action,Resource=resource,Result="error",CreatedUtc=DateTime.UtcNow});throw;}
        }
        private Task<string> ExecuteCoreAsync(string name, string argumentsJson, string callId, CancellationToken token)
        {
            return Task.Run(delegate
            {
                token.ThrowIfCancellationRequested();
                gate.Wait(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if(readOnly && !AgentToolCatalog.IsReadOnly(name) && !dynamicTools.Any(d=>d.Name==name&&d.ReadOnly))return "错误：当前为计划模式，只能读取和分析文件，不能创建、修改或打开目录。";
                    if (argumentsJson == null || argumentsJson.Length > MaxFileBytes * 2 + 4096)
                        return "错误：工具参数缺失或过长。";
                    var serializer = new JavaScriptSerializer { MaxJsonLength = MaxFileBytes * 2 + 4096, RecursionLimit = 12 };
                    var args = serializer.DeserializeObject(argumentsJson) as Dictionary<string, object>;
                    if (args == null) return "错误：工具参数必须是 JSON 对象。";
                    if(name=="spawn_subagent"||name=="complete_subagent"||name=="interrupt_subagent"){if(subagentScheduler==null)return "错误：子 Agent 生命周期尚未初始化。";var serializer2=new JavaScriptSerializer{MaxJsonLength=65536};if(name=="spawn_subagent"){var profile=new AgentProfileRegistry().Get(GetString(args,"profile","general"));if(profile==null)throw new ArgumentException("未知子 Agent Profile。");var child=subagentScheduler.Schedule(kernelTaskId,profile,GetString(args,"input",""));return serializer2.Serialize(new{child.Id,child.Status,child.ProfileId});}string id=GetString(args,"id",null);if(name=="complete_subagent"){bool ok=subagentScheduler.Complete(id,GetString(args,"result",""));return serializer2.Serialize(new{id,status=ok?"complete":"error"});}bool stopped=subagentScheduler.Interrupt(id);return serializer2.Serialize(new{id,status=stopped?"interrupted":"error"});}
                    if(name=="fetch_url" || name.StartsWith("github_",StringComparison.Ordinal)){if(github==null)return "错误：尚未连接 GitHub，请在工作设置中填写 owner/repo 和访问令牌。";return github.ExecuteAsync(name,args,token).GetAwaiter().GetResult();}
                    if(name=="load_skill" || name=="read_instructions")return InstructionTool(name,args);
                    string path = Resolve(GetString(args, "path", "."));
                    if (name == "list_files") return ListPage(path, GetInt(args,"offset",0,0,1000000), GetInt(args,"limit",200,1,200), token);
                    if (name == "find_files") return FindFiles(path,GetString(args,"pattern",null),GetInt(args,"offset",0,0,1500),GetInt(args,"limit",100,1,200),token);
                    if (name == "read_files") return ReadFiles(args,token);
                    if (name == "apply_patch") return ApplyPatch(args,token);
                    if (name == "restore_file") return RestoreFile(path,token);
                    if (name == "list_changes") return ListChanges();
                    if (name == "update_plan") return UpdatePlan(args);
                    if (name == "read_output" && args.ContainsKey("output_id") && CommandResults.Any(c=>c.CommandId==GetString(args,"output_id",null))) return ReadOutput(GetString(args,"output_id",null),GetInt(args,"offset",0,0,256*1024),GetInt(args,"limit",6000,1,12000));
                    if (name == "read_output" && args.ContainsKey("output_id")) return SavedOutput(GetString(args,"output_id",null),GetInt(args,"offset",0,0,24000),GetInt(args,"limit",6000,1,12000));
                    if (name == "read_output") return ReadOutput(GetString(args,"command_id",null),GetInt(args,"offset",0,0,256*1024),GetInt(args,"limit",6000,1,12000));
                    if (name == "read_file") return ReadFile(path, GetInt(args, "offset", 0, 0, 1000000), GetInt(args, "limit", 200, 1, 1000), token);
                    if (name == "search_files") return Search(path, GetString(args, "query", null), GetString(args,"pattern",""), args.ContainsKey("regex") && args["regex"] is bool && (bool)args["regex"], token);
                    if (name == "create_directory")
                    {
                        token.ThrowIfCancellationRequested();
                        using (LockDirectories(path, true)) { return "已创建目录 " + Relative(path) + "。"; }
                    }
                    if (name == "open_directory")
                    {
                        using (LockDirectories(path, false))
                        {
                            token.ThrowIfCancellationRequested(); DirectoryOpener(path);
                            return "已在资源管理器打开目录 " + Relative(path) + "。";
                        }
                    }
                    if (name == "write_file") return WriteFile(path, GetString(args, "content", null), token);
                    if (name == "edit_file")
                    {
                        string oldText = GetString(args, "old_text", null), newText = GetString(args, "new_text", null);
                        if (oldText.Length == 0) throw new ArgumentException("old_text 不能为空。");
                        if (DocumentFormats.IsDocument(path)) throw new ArgumentException("Word/Excel 文档不支持 old_text 精确修改；请用 write_file 以文字完整重写：" + Relative(path));
                        using (LockDirectories(Path.GetDirectoryName(path), false))
                        {
                            CheckExpected(path,ReadBytes(path,token));
                            string content = ReadText(path, token);
                            int start = content.IndexOf(oldText, StringComparison.Ordinal);
                            if (start < 0) return "错误：未找到 old_text，请先读取当前文件。";
                            if (content.IndexOf(oldText, start + oldText.Length, StringComparison.Ordinal) >= 0)
                                return "错误：old_text 匹配多处，请提供更完整的上下文。";
                            return WriteFile(path, content.Substring(0, start) + newText + content.Substring(start + oldText.Length), token);
                        }
                    }
                    return "错误：未知工具。可用工具：list_files、read_file、write_file、edit_file、search_files、create_directory、open_directory。";
                }
                catch (OperationCanceledException) { throw; }
                catch (DecoderFallbackException) { return "错误：文件不是有效的 UTF-8 文本，未进行写入。"; }
                catch (EncoderFallbackException) { return "错误：内容包含无效 Unicode 字符，未进行写入。"; }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return "错误：正则搜索超时，请缩小范围或简化表达式。"; }
                catch (ArgumentException ex) { return "错误：" + ex.Message; }
                catch (UnauthorizedAccessException) { return RestrictToRoot ? "错误：拒绝访问。请使用工作目录内的普通文件，禁止链接、受保护目录和越界路径。" : "错误：拒绝访问。请检查系统访问权限；禁止设备路径、链接及硬链接。"; }
                catch (FileNotFoundException) { return "错误：文件不存在，请先用 list_files 查看目录。"; }
                catch (DirectoryNotFoundException) { return "错误：目录不存在，请先用 list_files 查看目录。"; }
                catch (IOException) { return "错误：文件操作失败，文件可能被占用或工作目录已变化。"; }
                catch (System.ComponentModel.Win32Exception) { return "错误：无法打开资源管理器，请确认目录和系统权限。"; }
                catch (InvalidOperationException) { return "错误：JSON 参数无效或操作超出限制。"; }
                finally { gate.Release(); }
            }, token);
        }

        private string Resolve(string input)
        {
            ValidateSyntax(input, true, !RestrictToRoot);
            string path = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(Root, input));
            if (RestrictToRoot && !String.Equals(path, Root, StringComparison.OrdinalIgnoreCase) && !IsWithin(path, Root))
                throw new UnauthorizedAccessException();
            ValidateSegments(path);
            string relative = Relative(path);
            if (RestrictToRoot) foreach (string part in relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                if (String.Equals(part, BackupFolder, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
            return path;
        }

        private static void ValidateSyntax(string path, bool allowDrive, bool allowParent)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > 240 || path.StartsWith("\\") || path.StartsWith("//"))
                throw new ArgumentException("路径无效：禁止 UNC、设备路径和过长路径。");
            for (int i = 0; i < path.Length; i++)
                if (Char.IsControl(path[i]) || path[i] == '*' || path[i] == '?' || path[i] == '"' || path[i] == '<' || path[i] == '>' || path[i] == '|' ||
                    (path[i] == ':' && !(allowDrive && i == 1 && Char.IsLetter(path[0]) && path.Length > 2 && (path[2] == '\\' || path[2] == '/'))))
                    throw new ArgumentException("路径包含无效字符或备用数据流。");
            foreach (string part in path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if ((!allowParent && part == "..") || (part != "." && part != ".." && (part.EndsWith(".") || part.EndsWith(" "))))
                    throw new ArgumentException("禁止上级目录跳转及不明确的文件名。");
                string device = part.Split('.')[0].ToUpperInvariant();
                if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" || device == "CONIN$" || device == "CONOUT$" ||
                    (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && "123456789¹²³".IndexOf(device[3]) >= 0))
                    throw new ArgumentException("禁止设备文件名。");
            }
        }

        private void ValidateSegments(string path)
        {
            if (!RestrictToRoot) return;
            foreach (string part in path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                if (String.Equals(part, ".git", StringComparison.OrdinalIgnoreCase) || String.Equals(part, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(part, "System Volume Information", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        }

        private static IEnumerable<string> ProtectedRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        }

        private static bool IsWithin(string path, string root) { return path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        private string Relative(string path) { return String.Equals(path, Root, StringComparison.OrdinalIgnoreCase) ? "." : IsWithin(path, Root) ? path.Substring(prefix.Length).Replace('\\', '/') : path; }

        private string ReadFile(string path, int offset, int limit, CancellationToken token)
        {
            using (LockDirectories(Path.GetDirectoryName(path), false))
            {
                string content;
                if (DocumentFormats.IsDocument(path))
                {
                    byte[] documentBytes = ReadBytes(path, token, DocumentFormats.MaxDocumentBytes);
                    observed[path] = ContentHash(documentBytes);
                    content = DocumentFormats.ReadDocumentText(path, documentBytes, token);
                }
                else content = ReadText(path, token);
                var output = new StringBuilder();
                using (var reader = new StringReader(content))
                {
                    string line; int number = 0, emitted = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        if (number++ < offset) continue;
                        if (++emitted > limit || output.Length + line.Length > MaxResultChars - 100) { output.AppendLine("[已截断：请使用 offset/limit 分段读取]"); break; }
                        output.Append(number).Append(": ").AppendLine(line);
                    }
                }
                return output.Length == 0 ? "文件为空，或 offset 已超过文件末尾。" : Clip(output.ToString());
            }
        }

        private string Search(string path, string query, string pattern, bool useRegex, CancellationToken token)
        {
            if (String.IsNullOrEmpty(query) || query.Length > 1000) throw new ArgumentException("query 应为 1 至 1000 个字符的普通文本。");
            var expression=useRegex?new System.Text.RegularExpressions.Regex(query,System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)):null;
            var filter=String.IsNullOrEmpty(pattern)?null:FilePattern(pattern);
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(path, 0));
            var output = new StringBuilder(); int visited = 0, matches = 0;
            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var item = queue.Dequeue();
                using (LockDirectories(item.Key, false))
                {
                    foreach (string entry in Directory.EnumerateFileSystemEntries(item.Key))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > 1500) return Clip(output + "\n[已截断：最多检查 1500 项]");
                        string leaf = Path.GetFileName(entry);
                        if ((RestrictToRoot && leaf.Equals(".git", StringComparison.OrdinalIgnoreCase)) || leaf.Equals(BackupFolder, StringComparison.OrdinalIgnoreCase)) continue;
                        FileAttributes attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (item.Value < 12 && !IgnoredDirectories.Contains(leaf)) queue.Enqueue(new KeyValuePair<string, int>(entry, item.Value + 1));
                            continue;
                        }
                        if(filter!=null && !filter.IsMatch(Relative(entry)))continue;
                        if(DocumentFormats.IsLegacyWord(entry) || DocumentFormats.IsLegacyExcel(entry))continue;
                        string content;
                        try { content = DocumentFormats.IsDocument(entry) ? DocumentFormats.ReadDocumentText(entry, ReadBytes(entry, token, DocumentFormats.MaxDocumentBytes), token) : ReadText(entry, token); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        catch (ArgumentException) { continue; }
                        using (var reader = new StringReader(content))
                        {
                            string line; int number = 0;
                            while ((line = reader.ReadLine()) != null)
                            {
                                token.ThrowIfCancellationRequested(); number++;
                                if (expression==null ? line.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 : !expression.IsMatch(line)) continue;
                                output.Append(Relative(entry)).Append(':').Append(number).Append(": ").AppendLine(line.Length > 400 ? line.Substring(0, 400) + "…" : line);
                                if (++matches >= 100 || output.Length >= MaxResultChars - 600) return Clip(output + "[已截断：结果达到上限]");
                            }
                        }
                    }
                }
            }
            return output.Length == 0 ? "未找到匹配文本（搜索文字文件及 .docx/.xlsx，深度最多 12 层）。" : Clip(output.ToString());
        }

        private string ReadText(string path, CancellationToken token)
        {
            byte[] bytes = ReadBytes(path, token);
            observed[path]=ContentHash(bytes);
            return DecodeText(bytes);
        }
        private static string DecodeText(byte[] bytes)
        {
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string result = Utf8.GetString(bytes, start, bytes.Length - start);
            if (result.IndexOf('\0') >= 0) throw new ArgumentException("文件包含二进制数据，仅支持 UTF-8 文本。");
            return result;
        }

        // Binary documents are compared through their readable text so change
        // counts stay meaningful; unknown binaries yield no comparison text.
        private static string ReadComparisonText(string path, byte[] bytes)
        {
            try { return DecodeText(bytes); }
            catch (ArgumentException) { }
            if (DocumentFormats.IsDocument(path))
                try { return DocumentFormats.ReadDocumentText(path, bytes, CancellationToken.None); }
                catch (ArgumentException) { }
            return null;
        }

        private static string SizeMessage(long limit)
        {
            return "文件超过 " + (limit / 1024 / 1024) + " MiB 限制" + (limit == MaxFileBytes ? "，请缩小工作范围。" : "，请拆分文档后重试。");
        }

        private byte[] ReadBytes(string path, CancellationToken token, long maxBytes = MaxFileBytes)
        {
            using (SafeFileHandle handle = Open(path, 0x80000000, 1, 3, 0x00200000))
            {
                VerifyFinalPath(handle, path);
                FileInfoNative info = Inspect(handle);
                if ((info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 || info.Links > 1)
                    throw new UnauthorizedAccessException();
                if (info.SizeHigh != 0 || (long)info.SizeLow > maxBytes) throw new ArgumentException(SizeMessage(maxBytes));
                using (var stream = new FileStream(handle, FileAccess.Read))
                using (var data = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (data.Length + n > maxBytes) throw new ArgumentException(SizeMessage(maxBytes));
                        data.Write(buffer, 0, n);
                    }
                    return data.ToArray();
                }
            }
        }

        private string WriteFile(string path, string content, CancellationToken token, byte[] exactBytes=null)
        {
            if (path == Root) throw new ArgumentException("写入路径必须是文件。");
            EnsureChangeCapacity(new[]{path});
            if (exactBytes == null && DocumentFormats.IsDocument(path)) exactBytes = DocumentFormats.WriteDocumentBytes(path, content, token);
            byte[] bytes = exactBytes??Utf8.GetBytes(content);
            if (bytes.Length > MaxFileBytes) throw new ArgumentException("写入内容超过 1 MiB 限制。");
            if (content != null && content.IndexOf('\0') >= 0) throw new ArgumentException("写入内容必须是 UTF-8 文本。");
            using (LockDirectories(Path.GetDirectoryName(path), true))
            {
                token.ThrowIfCancellationRequested();
                bool exists = File.Exists(path);
                string backupName = null;
                string backupPath = null;
                string beforeHash=null,beforeText=null;
                if (exists)
                {
                    // Keep the original bytes, including BOM; decoding also refuses accidental binary overwrites.
                    byte[] previous = ReadBytes(path, token);
                    CheckExpected(path,previous);beforeHash=ContentHash(previous);
                    if (DocumentFormats.IsDocument(path)) beforeText=ReadComparisonText(path,previous);
                    else beforeText=DecodeText(previous);
                    if (BackupEnabled)
                    {
                        string backupDir = Path.Combine(RestrictToRoot ? Root : Path.GetDirectoryName(path), BackupFolder);
                        using (LockDirectories(backupDir, true))
                        {
                            int total = 0; long size = 0;
                            foreach (string backup in Directory.EnumerateFiles(backupDir))
                            {
                                token.ThrowIfCancellationRequested();
                                if (++total >= 200) throw new ArgumentException("备份达到 200 份上限，请手动整理工作目录中的备份。");
                                size += new FileInfo(backup).Length;
                                if (size + previous.Length > 64L * MaxFileBytes) throw new ArgumentException("备份达到 64 MiB 上限，请手动整理备份。");
                            }
                            backupName = Guid.NewGuid().ToString("N") + ".bak";
                            backupPath = Path.Combine(backupDir, backupName);
                            WriteNew(backupPath, previous, token);
                        }
                    }
                }
                else if (Directory.Exists(path)) throw new ArgumentException("写入路径是目录。");
                else if(observed.ContainsKey(path) && observed[path]!=null)throw new ArgumentException("读取过的文件已被删除，请核对后重试。");
                string temp = Path.Combine(Path.GetDirectoryName(path), ".deskpet-write-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    // Keep ownership of the temporary file through the atomic rename. Rename by
                    // handle replaces the directory entry, and never opens a replacement link target.
                    using (SafeFileHandle handle = Open(temp, 0x40010000, 0, 1, 0x00200000))
                    using (var stream = new FileStream(handle, FileAccess.Write))
                    {
                        WriteContents(stream, bytes, token);
                        token.ThrowIfCancellationRequested();
                        if(exists)CheckExpected(path,ReadBytes(path,token));
                        Rename(handle, path, exists);
                    }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                RecordChange(path,beforeHash,ContentHash(bytes),backupPath,beforeText,content,token);
                return "已写入 " + Relative(path) + "（" + bytes.Length + " 字节）" + (backupName == null ? "。" : "；原文件备份：" + Relative(backupPath) + "。");
            }
        }

        private static void WriteNew(string path, byte[] bytes, CancellationToken token)
        {
            using (SafeFileHandle handle = Open(path, 0x40000000, 0, 1, 0x00200000))
            using (var stream = new FileStream(handle, FileAccess.Write))
            {
                WriteContents(stream, bytes, token);
            }
        }
        private static void WriteContents(FileStream stream, byte[] bytes, CancellationToken token)
        {
            for (int position = 0; position < bytes.Length; position += 8192)
            {
                token.ThrowIfCancellationRequested();
                stream.Write(bytes, position, Math.Min(8192, bytes.Length - position));
            }
            stream.Flush(true);
        }
        private static void Rename(SafeFileHandle handle, string path, bool replace)
        {
            byte[] name = Encoding.Unicode.GetBytes(path);
            int lengthOffset = IntPtr.Size == 8 ? 16 : 8;
            int nameOffset = lengthOffset + 4;
            IntPtr info = Marshal.AllocHGlobal(nameOffset + name.Length + 2);
            try
            {
                for (int i = 0; i < nameOffset + name.Length + 2; i++) Marshal.WriteByte(info, i, 0);
                Marshal.WriteInt32(info, 0, replace ? 1 : 0);
                Marshal.WriteInt32(info, lengthOffset, name.Length);
                Marshal.Copy(name, 0, new IntPtr(info.ToInt64() + nameOffset), name.Length);
                if (!SetFileInformationByHandle(handle, 3, info, (uint)(nameOffset + name.Length + 2)))
                    throw new IOException("原子重命名失败。");
            }
            finally { Marshal.FreeHGlobal(info); }
        }

        // Hold handles without write/delete sharing for every ancestor until completion.
        // This blocks both directory swaps and setting reparse data in place.
        private DirectoryLocks LockDirectories(string target, bool create)
        {
            var paths = new List<string>();
            for (var directory = new DirectoryInfo(target); directory != null; directory = directory.Parent) paths.Add(directory.FullName);
            paths.Reverse();
            var locks = new DirectoryLocks();
            try
            {
                foreach (string path in paths)
                {
                    if (!Directory.Exists(path))
                    {
                        if (!create || (RestrictToRoot && !IsWithin(path, Root))) throw new DirectoryNotFoundException();
                        Directory.CreateDirectory(path);
                    }
                    SafeFileHandle handle = Open(path, 0, 1, 3, 0x02200000);
                    locks.Handles.Add(handle);
                    VerifyFinalPath(handle, path);
                    FileInfoNative info = Inspect(handle);
                    if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0)
                        throw new UnauthorizedAccessException();
                }
                return locks;
            }
            catch { locks.Dispose(); throw; }
        }

        private sealed class DirectoryLocks : IDisposable
        {
            public readonly List<SafeFileHandle> Handles = new List<SafeFileHandle>();
            public void Dispose() { for (int i = Handles.Count - 1; i >= 0; i--) Handles[i].Dispose(); }
        }

        private static SafeFileHandle Open(string path, uint access, uint sharing, uint creation, uint flags)
        {
            SafeFileHandle handle = CreateFile(path, access, sharing, IntPtr.Zero, creation, flags, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error == 2) throw new FileNotFoundException();
            if (error == 3) throw new DirectoryNotFoundException();
            if (error == 5 || error == 1920) throw new UnauthorizedAccessException();
            throw new IOException("文件句柄操作失败。");
        }
        private static FileInfoNative Inspect(SafeFileHandle handle)
        {
            FileInfoNative info;
            if (!GetFileInformationByHandle(handle, out info)) throw new IOException("无法验证文件句柄。");
            return info;
        }
        private void VerifyFinalPath(SafeFileHandle handle, string expected)
        {
            var buffer = new StringBuilder(1024);
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity) throw new UnauthorizedAccessException();
            string actual = buffer.ToString();
            if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) actual = actual.Substring(4);
            // Reject DOS short-name aliases as well as unexpected object redirection.
            if (!String.Equals(actual.TrimEnd('\\', '/'), Path.GetFullPath(expected).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException();
            ValidateSegments(actual);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FileInfoNative
        {
            public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, IntPtr info, uint length);

        private static void OpenExplorer(string directory)
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            int trailing = 0;
            for (int i = directory.Length - 1; i >= 0 && directory[i] == '\\'; i--) trailing++;
            string argument = "\"" + directory + new string('\\', trailing) + "\"";
            using (Process process = Process.Start(new ProcessStartInfo(explorer, argument) { UseShellExecute = false })) { }
        }

        private static string GetString(Dictionary<string, object> args, string key, string fallback)
        {
            object value;
            if (!args.TryGetValue(key, out value)) { if (fallback != null) return fallback; throw new ArgumentException("缺少参数 " + key + "。"); }
            string text = value as string;
            if (text == null) throw new ArgumentException(key + " 必须是字符串。");
            return text;
        }
        private static int GetInt(Dictionary<string, object> args, string key, int fallback, int min, int max)
        {
            object value; if (!args.TryGetValue(key, out value)) return fallback;
            if (!(value is int) || (int)value < min || (int)value > max) throw new ArgumentException(key + " 必须是 " + min + " 至 " + max + " 的整数。");
            return (int)value;
        }
        private static string Clip(string value) { return value.Length <= MaxResultChars ? value : value.Substring(0, MaxResultChars - 20) + "\n[结果已截断]"; }
        public void Dispose(){if(disposed)return;disposed=true;if(childWorkers!=null)childWorkers.Dispose();foreach(var transport in mcpTransports){var disposable=transport as IDisposable;if(disposable!=null)disposable.Dispose();}mcpTransports.Clear();if(github!=null){github.Dispose();github=null;}}
        readonly Dictionary<string,IMcpConnectionState> mcpAvailability=new Dictionary<string,IMcpConnectionState>();
        private void RegisterMcpTools(){if(kernelTools==null)return;dynamicTools.RemoveAll(d=>d.Name.StartsWith("mcp_",StringComparison.Ordinal));foreach(var transport in mcpTransports){try{foreach(var definition in new McpToolAdapter(transport).Register(kernelTools)){bool exists=false;foreach(var current in dynamicTools)if(current.Name==definition.Name){exists=true;break;}if(!exists)dynamicTools.Add(definition);var state=transport as IMcpConnectionState;if(state!=null)mcpAvailability[definition.Name]=state;}}catch(Exception e){throw new ApiException("MCP 工具注册失败："+e.Message);}}}
        private void BuildSchemas()
        {
            object[] a,b;AgentToolCatalog.Schemas(readOnly,providerCapabilities,dynamicTools,out a,out b,RuntimeToolAvailable);OpenAITools=a;AnthropicTools=b;
        }
        bool RuntimeToolAvailable(string name){if((name=="spawn_subagent"||name=="get_subagent"||name=="wait_subagents"||name=="interrupt_subagent")&&childWorkers==null)return false;IMcpConnectionState connection;if(mcpAvailability.TryGetValue(name,out connection)&&!connection.Connected)return false;return !desktopSession||DesktopToolAvailable(name);}
        public string AvailableToolNames {get{return String.Join(", ",AgentToolCatalog.Definitions.Cast<AgentToolDefinition>().Concat(dynamicTools).Where(d=>d.Name!="complete_subagent"&&(!readOnly||d.ReadOnly)&&(providerCapabilities==null||providerCapabilities.CanUseTool(d.Name))&&RuntimeToolAvailable(d.Name)).Select(d=>d.Name));}}
        internal int ContextSchemaOverheadTokens;
    }
}
