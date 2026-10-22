using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed class AgentToolDefinition
    {
        internal string Name,Description,ApprovalResource;internal bool ReadOnly;internal Dictionary<string,object> Properties,InputSchema;internal string[] Required;
    }
    // Both protocol schemas and execution's plan-mode policy use this catalogue.
    internal static class AgentToolCatalog
    {
        static object P(string type,string description){return new{type=type,description=description};}
        static Dictionary<string,object> Fields(params object[] fields){var d=new Dictionary<string,object>();for(int i=0;i<fields.Length;i+=2)d[(string)fields[i]]=fields[i+1];return d;}
        static AgentToolDefinition D(string name,string description,bool readOnly,Dictionary<string,object> properties,params string[] required){return new AgentToolDefinition{Name=name,Description=description,ReadOnly=readOnly,Properties=properties,Required=required};}
        internal static readonly AgentToolDefinition[] Definitions={
            D("ask_user","在原会话中询问任务信息并等待回答。此工具不能授予文件、终端或其他操作权限。",true,Fields("question",P("string","必填问题，最多 4000 字符"),"options",new{type="array",items=new{type="string"},minItems=2,maxItems=6},"allow_free_input",P("boolean","允许自由回答，默认 true")),"question"),
            D("inspect_image","读取工作区图片并作为视觉输入交给当前模型；支持 PNG/JPEG/GIF/WebP，最多 3 MiB、2000 万像素，需模型支持图片输入。",true,Fields("path",P("string","图片路径"),"question",P("string","可选图片问题")),"path"),
            D("git_status","读取本地 Git 状态与分支，无写入或联网。",true,Fields("path",P("string","仓库内路径，默认工作区"))),
            D("git_diff","读取 Git 差异；不包括未跟踪文件补丁。",true,Fields("path",P("string","仓库内路径，默认工作区"),"scope",new{type="string",@enum=new[]{"unstaged","staged"}})),
            D("git_log","读取有界的本地 Git 提交记录。",true,Fields("path",P("string","仓库内路径，默认工作区"),"limit",P("integer","1～50，默认 10"))),
            D("generate_image","按文字描述生成图片并显示在原会话。",false,Fields("prompt",P("string","图片内容描述")),"prompt"),
            D("list_files","分页列出目录，稳定排序。offset 从 0 开始，limit 默认 200；截断时使用 next_offset 继续。",true,Fields("path",P("string","目录，默认 ."),"offset",P("integer","跳过项目数"),"limit",P("integer","1～200 项"))),
            D("read_file","读取文件文字并返回行号：UTF-8 文本，或 .docx/.xlsx 文档（含表格文字）；.doc/.xls 需本机 Word/Excel 或 WPS。文本最多 1 MiB，文档最多 8 MiB；offset 从 0 开始，limit 默认 200。",true,Fields("path",P("string","文件路径"),"offset",P("integer","跳过行数"),"limit",P("integer","1～1000 行")),"path"),
            D("write_file","创建或完整覆盖文件：文本按 UTF-8 写入；.docx 生成真实 Word 文档（每行一段，# 为标题，** 为加粗）；.xlsx 生成真实表格（制表符或逗号分列）；.doc/.xls 需本机 Word/Excel 或 WPS。保存原文件备份，拒绝覆盖读取后已变化的文件。",false,Fields("path",P("string","文件路径"),"content",P("string","完整文件内容")),"path","content"),
            D("edit_file","将唯一匹配的 old_text 替换为 new_text（仅文本文件；Word/Excel 文档请用 write_file 完整重写）；先读取文件，拒绝外部修改冲突。",false,Fields("path",P("string","文件路径"),"old_text",P("string","唯一匹配的旧文本"),"new_text",P("string","替换文本")),"path","old_text","new_text"),
            D("search_files","递归搜索文字文件与 .docx/.xlsx 文档内容，忽略依赖与构建目录；最多检查 1500 项、12 层。",true,Fields("path",P("string","目录，默认 ."),"query",P("string","1～1000 字符的文本或正则"),"regex",P("boolean","是否正则搜索，默认 false，每次匹配限时 100 毫秒"),"pattern",P("string","可选文件 glob，例如 **/*.cs")),"query"),
            D("create_directory","创建目录及所需父目录。",false,Fields("path",P("string","目录路径")),"path"),
            D("open_directory","在资源管理器中打开现有目录。",false,Fields("path",P("string","已有目录")),"path"),
            D("find_files","按 glob 查找文件，例如 **/*.cs；跳过依赖与构建目录。使用 next_offset 分页。",true,Fields("path",P("string","目录，默认 ."),"pattern",P("string","路径模式"),"offset",P("integer","跳过匹配项"),"limit",P("integer","1～200 项，默认 100")),"pattern"),
            D("read_files","批量读取最多 8 个文字文件或 .docx/.xlsx 文档；总输出有界，过长时单独分段读取。",true,Fields("paths",new{type="array",items=new{type="string"},maxItems=8},"offset",P("integer","每个文件跳过行数，默认 0"),"limit",P("integer","每个文件行数，默认 100")),"paths"),
            D("apply_patch","预检后精确修改多个文件，最多 20 个。已有文件提供 path/old_text/new_text（仅文本文件），新建文件提供 path/content（.docx/.xlsx 会生成真实文档）。",false,Fields("edits",new{type="array",items=new{type="object"},maxItems=20}),"edits"),
            D("list_changes","列出本任务实际文件变更、原文件备份与恢复状态。",true,Fields()),
            D("restore_file","从本任务原文件备份恢复；文件又被外部修改时拒绝。",false,Fields("path",P("string","需恢复的文件路径")),"path"),
            D("update_plan","更新任务步骤与进度，最多 20 步；步骤状态为 pending/in_progress/complete，同时最多一步进行中。",true,Fields("steps",new{type="array",items=new{type="object"},maxItems=20}),"steps"),
            D("read_output","分页读取本任务已执行命令的有界输出。",true,Fields("command_id",P("string","命令 ID"),"output_id",P("string","历史工具输出 ID；与 command_id 二选一"),"offset",P("integer","字符偏移，默认 0"),"limit",P("integer","1～12000 字符"))),
            D("fetch_url","读取公开网页并返回有界纯文本；仅支持 http/https，禁止本机地址。",true,Fields("url",P("string","网页 URL")),"url"),
            D("github_read_file","读取已连接 GitHub 仓库中的文件文字。",true,Fields("path",P("string","仓库内路径"),"ref",P("string","分支、标签或提交")),"path"),
            D("github_list_files","列出已连接 GitHub 仓库目录。",true,Fields("path",P("string","仓库内目录"),"ref",P("string","分支、标签或提交"))),
            D("github_search_code","在已连接 GitHub 仓库中搜索代码。",true,Fields("query",P("string","搜索内容")),"query"),
            D("github_read_issue","读取 GitHub Issue。",true,Fields("number",P("integer","Issue 编号")),"number"),
            D("github_create_issue","创建 GitHub Issue。",false,Fields("title",P("string","标题"),"body",P("string","正文")),"title"),
            D("github_comment","向 GitHub Issue 或 PR 添加评论。",false,Fields("number",P("integer","Issue/PR 编号"),"body",P("string","评论正文")),"number","body"),
            D("github_list_pull_requests","列出 GitHub Pull Requests。",true,Fields("state",P("string","open/closed/all"))),
            D("github_create_pull_request","创建 GitHub Pull Request。",false,Fields("title",P("string","标题"),"body",P("string","正文"),"head",P("string","来源分支"),"base",P("string","目标分支")),"title","head","base"),
            D("spawn_subagent","启动真正独立的只读 explore worker。最大并行 4、深度 1，不允许写入或终端。",true,Fields("profile",P("string","explore"),"input",P("string","子任务输入")),"profile","input"),
            D("complete_subagent","完成子 Agent 并记录结果。",false,Fields("id",P("string","子 Agent ID"),"result",P("string","结果")),"id","result"),
            D("interrupt_subagent","中断正在运行的子 Agent。",false,Fields("id",P("string","子 Agent ID")),"id")
        };
        internal static bool IsReadOnly(string name){var tool=Definitions.FirstOrDefault(d=>d.Name==name);return tool!=null && tool.ReadOnly;}
        internal static void Schemas(bool plan,out object[] openai,out object[] anthropic){Schemas(plan,null,null,out openai,out anthropic);}
        internal static void Schemas(bool plan,AgentModelCapabilities capabilities,IEnumerable<AgentToolDefinition> extra,out object[] openai,out object[] anthropic,Func<string,bool> enabled=null)
        {
            var a=new List<object>();var b=new List<object>();var all=Definitions.Concat(extra??new AgentToolDefinition[0]);
            foreach(var d in all.Where(d=>d!=null && d.Name!="complete_subagent" && (!plan || d.ReadOnly) && (capabilities==null || capabilities.CanUseTool(d.Name)) && (enabled==null || enabled(d.Name)))){
                object schema=d.InputSchema??(object)new{type="object",properties=d.Properties??new Dictionary<string,object>(),required=d.Required??new string[0],additionalProperties=false};
                a.Add(new{type="function",function=new{name=d.Name,description=d.Description??"",parameters=schema}});
                b.Add(new{name=d.Name,description=d.Description??"",input_schema=schema});
            }
            openai=a.ToArray();anthropic=b.ToArray();
        }
    }
}
