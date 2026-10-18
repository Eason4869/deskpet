using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
namespace Dafeiyu.Tests
{
    public static class ManagedSkillLibraryTests
    {
        static int count;
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static bool Reject(Action action){try{action();return false;}catch(ArgumentException){return true;}catch(UnauthorizedAccessException){return true;}catch(InvalidOperationException){return true;}}
        static bool IoReject(Action action){try{action();return false;}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}}
        static string Skill(string parent,string id){string folder=Path.Combine(parent,id);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"SKILL.md"),"---\nname: "+id+"\ndescription: Imported review fixture\n---\nFind MANAGED_FINDINGS. Never run scripts.");Directory.CreateDirectory(Path.Combine(folder,"references"));File.WriteAllText(Path.Combine(folder,"references","guide.txt"),"managed reference");return folder;}
        static void ImportWriteFailureRecovery(string root,string originals)
        {
            string managed=Path.Combine(root,"transaction-library"),source=Skill(originals,"retry");Directory.CreateDirectory(managed);
            string registry=Path.Combine(managed,"registry.json"),originalRegistry="{\"Version\":1,\"Skills\":[]}";File.WriteAllText(registry,originalRegistry);
            var library=new ManagedSkillLibrary(managed);int events=0;library.Changed+=delegate{events++;};bool failed=false;
            // A readable registry can still reject atomic replacement while held without delete sharing.
            using(var held=new FileStream(registry,FileMode.Open,FileAccess.Read,FileShare.Read)){
                try{library.Import(source);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
            }
            Check(failed,"registry sharing violation fails import rather than claiming success");
            Check(!Directory.Exists(Path.Combine(managed,"retry")),"failed registry commit releases promoted skill name for retry");
            string quarantine=Directory.EnumerateDirectories(managed,".failed-import-retry-*").Single();
            Check(File.ReadAllText(Path.Combine(quarantine,"references","guide.txt"))=="managed reference"&&File.Exists(Path.Combine(source,"SKILL.md")),"failed import preserves quarantined resources and original source");
            Check(File.ReadAllText(registry)==originalRegistry&&library.List().Count==0&&events==0,"failed import preserves registry and emits no successful catalog change");
            Check(library.Import(source).Id=="retry"&&library.List().Single().Enabled&&events==1,"import retries successfully after registry lock is released");
        }
        static void RemoveWriteFailureRecovery(string root,string originals)
        {
            string managed=Path.Combine(root,"remove-transaction-library"),source=Skill(originals,"removal");var library=new ManagedSkillLibrary(managed);var imported=library.Import(source);
            string registry=Path.Combine(managed,"registry.json"),originalRegistry=File.ReadAllText(registry);int events=0;library.Changed+=delegate{events++;};
            // Open the directory itself without FILE_SHARE_DELETE, reproducing a failed rename.
            using(var held=CreateFile(imported.Directory,0x80000000,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){
                Check(!held.IsInvalid,"directory rename lock fixture opens successfully");
                Check(IoReject(delegate{library.Remove("removal");}),"blocked directory rename fails removal explicitly");
                Check(File.ReadAllText(registry)==originalRegistry&&library.List().Single().Id=="removal"&&Directory.Exists(imported.Directory)&&events==0,"failed removal rename preserves installed registry and visible copy");
            }
            using(var held=new FileStream(registry,FileMode.Open,FileAccess.Read,FileShare.Read)){
                Check(IoReject(delegate{library.Remove("removal");}),"blocked registry replacement fails removal explicitly");
                Check(File.ReadAllText(registry)==originalRegistry&&library.List().Single().Id=="removal"&&File.Exists(Path.Combine(imported.Directory,"references","guide.txt"))&&events==0,"failed removal registry commit restores installed directory and catalog");
            }
            library.Remove("removal");Check(library.List().Count==0&&!Directory.Exists(imported.Directory)&&File.Exists(Path.Combine(source,"SKILL.md"))&&events==1,"removal retries successfully after directory and registry locks release");
            Check(library.Import(source).Id=="removal"&&library.List().Count==1&&events==2,"successfully removed name can be reimported without orphan collision");
        }
        public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"managed-skills-"+Guid.NewGuid().ToString("N"));
            try{
                Directory.CreateDirectory(root);string originals=Path.Combine(root,"originals"),managed=Path.Combine(root,"library"),project=Path.Combine(root,"project");Directory.CreateDirectory(project);
                ImportWriteFailureRecovery(root,originals);
                RemoveWriteFailureRecovery(root,originals);
                var library=new ManagedSkillLibrary(managed);int events=0;library.Changed+=delegate{events++;};string source=Skill(originals,"review");
                var imported=library.Import(source);Check(imported.Id=="review"&&imported.Name=="review"&&imported.Enabled&&imported.Description=="Imported review fixture","explicit import validates metadata and enables managed copy");
                Check(imported.Directory==Path.Combine(managed,"review")&&File.Exists(Path.Combine(source,"SKILL.md"))&&File.ReadAllText(Path.Combine(imported.Directory,"references","guide.txt"))=="managed reference","import copies resources and retains original source");
                Check(new ManagedSkillLibrary(managed).List().Single().Id=="review","managed metadata survives reopening");
                Check(library.Read("review").Hash.Length==64&&library.Read("review").Content.Contains("MANAGED_FINDINGS"),"managed read returns real body and source hash");
                Check(Reject(delegate{library.Import(source);}),"duplicate import cannot overwrite installed skill");
                library.SetEnabled("review",false);Check(!new ManagedSkillLibrary(managed).List().Single().Enabled,"disable persists across reopening");
                Skill(Path.Combine(project,".agents","skills"),"project-only");File.WriteAllText(Path.Combine(project,"AGENTS.md"),"PROJECT_SECRET");ManagedSkillLibrary.ActiveDirectory=managed;
                using(var workspace=new AgentWorkspace(project)){workspace.ConfigureInstructions(false);workspace.ConfigureDesktopSession();Check(!workspace.AvailableToolNames.Contains("load_skill")&&!workspace.AvailableToolNames.Contains("read_instructions")&&!workspace.ProjectInstructionText.Contains("PROJECT_SECRET")&&!workspace.ProjectInstructionText.Contains("project-only"),"disabled managed and untrusted project skills are omitted");}
                library.SetEnabled("review",true);
                using(var workspace=new AgentWorkspace(project)){
                    workspace.ConfigureInstructions(false);workspace.ConfigureDesktopSession();Check(workspace.AvailableToolNames.Contains("load_skill")&&!workspace.AvailableToolNames.Contains("read_instructions")&&workspace.ProjectInstructionText.Contains("review")&&!workspace.ProjectInstructionText.Contains("MANAGED_FINDINGS")&&!workspace.ProjectInstructionText.Contains("project-only"),"untrusted project receives only enabled managed metadata");
                    string result=workspace.ExecuteAsync("load_skill","{\"name\":\"review\"}",CancellationToken.None).Result;Check(result.Contains("MANAGED_FINDINGS")&&result.Contains("SHA256：")&&result.Contains(imported.Directory),"managed body loads through real tool without project trust");
                    Check(workspace.ExecuteAsync("load_skill","{\"name\":\"review\",\"resource\":\"references/guide.txt\"}",CancellationToken.None).Result.Contains("managed reference"),"managed relative resources use real load_skill");
                    Check(workspace.ExecuteAsync("load_skill","{\"name\":\"review\",\"resource\":\"../../originals/review/SKILL.md\"}",CancellationToken.None).Result.StartsWith("错误："),"managed resource traversal is denied");
                    Check(workspace.ExecuteAsync("exec_command","{\"command\":\"echo bad\"}",CancellationToken.None).Result.StartsWith("错误："),"imported instructions do not grant execution permission");
                    library.SetEnabled("review",false);workspace.ConfigureInstructions(false);Check(!workspace.AvailableToolNames.Contains("load_skill"),"next request snapshot drops disabled skill");
                }
                library.SetEnabled("review",true);Skill(Path.Combine(project,".agents","skills"),"review");using(var workspace=new AgentWorkspace(project))Check(Reject(delegate{workspace.ConfigureInstructions(true);}),"managed and project name collision fails explicitly");
                Check(Reject(delegate{library.Remove("../originals");})&&Reject(delegate{library.SetEnabled("review/../x",false);}),"registry ids cannot escape managed root");
                string invalid=Skill(originals,"invalid");File.WriteAllText(Path.Combine(invalid,"SKILL.md"),"---\nname: ../../escape\ndescription: bad\n---\nbody");Check(Reject(delegate{library.Import(invalid);}),"invalid metadata name is rejected");
                string spoof=Skill(originals,"spoof");File.WriteAllText(Path.Combine(spoof,"SKILL.md"),"No front matter\nname: spoof\ndescription: Instructions pretending to be metadata\n");Check(Reject(delegate{library.Import(spoof);}),"instruction body cannot spoof skill metadata");
                string huge=Skill(originals,"huge");File.WriteAllText(Path.Combine(huge,"SKILL.md"),new string('x',65537));Check(Reject(delegate{library.Import(huge);}),"oversized skill body is rejected");
                string large=Skill(originals,"large");using(var file=new FileStream(Path.Combine(large,"large.bin"),FileMode.CreateNew)){file.SetLength(64L*1024*1024);}Check(Reject(delegate{library.Import(large);}),"resource total bytes are bounded before copying");
                string crowded=Skill(originals,"crowded");for(int i=0;i<2048;i++)File.WriteAllText(Path.Combine(crowded,"r"+i),"");Check(Reject(delegate{library.Import(crowded);}),"resource file count is bounded before import");
                string linked=Skill(originals,"linked"),junction=Path.Combine(linked,"outside");var process=Process.Start(new ProcessStartInfo("cmd.exe","/c mklink /J \""+junction+"\" \""+project+"\""){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true});process.WaitForExit();Check(process.ExitCode==0,"junction fixture created (requires unsandboxed test execution)");Check(Reject(delegate{library.Import(linked);}),"linked import resources cannot cross source boundary");Check(Reject(delegate{new ManagedSkillLibrary(junction);}),"library root cannot be a junction");Directory.Delete(junction);
                library.Remove("review");Check(library.List().Count==0&&File.Exists(Path.Combine(source,"SKILL.md"))&&!Directory.Exists(imported.Directory),"remove unregisters managed copy and preserves original");Check(events==6,"mutations notify catalog changes");
                Skill(managed,"unregistered");Check(library.List().Count==0,"unregistered folders are never auto-enabled");
                Check(Directory.EnumerateFiles(managed,".registry-*.bak").Any()&&Directory.EnumerateDirectories(managed,".removed-review-*").Any(),"registry history and removed copy are retained for recovery");
                File.WriteAllText(Path.Combine(managed,"registry.json"),"{broken registry");var corrupt=new ManagedSkillLibrary(managed);Check(corrupt.List().Count==0&&!String.IsNullOrEmpty(corrupt.Warning),"corrupt registry disables discovery and reports warning");Check(Reject(delegate{corrupt.Import(source);})&&File.ReadAllText(Path.Combine(managed,"registry.json"))=="{broken registry","corrupt registry is retained and cannot be silently overwritten");
                Console.WriteLine("Managed skill library tests: "+count+" passed; fixtures: "+root);return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{ManagedSkillLibrary.ActiveDirectory=null;}
        }
    }
}
