using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentDocumentTests
    {
        static int checks;
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static string Call(AgentWorkspace w,string tool,object args){return w.ExecuteAsync(tool,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();}
        static string Hash(string path){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"documents-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var w=new AgentWorkspace(root);
                string docxPath=Path.Combine(root,"notes.docx");
                string wrote=Call(w,"write_file",new{path="notes.docx",content="项目计划\n\n# 第一部分\n**重点**：完成读取\n- 事项一"});
                Check(!wrote.StartsWith("错误：") && File.Exists(docxPath),"write_file creates a real .docx package");
                byte[] signature=File.ReadAllBytes(docxPath);
                Check(signature.Length>4 && signature[0]==0x50 && signature[1]==0x4B,"written .docx uses the ZIP package format");
                using(var zip=ZipFile.OpenRead(docxPath))
                {
                    var part=zip.GetEntry("word/document.xml");
                    Check(part!=null,"written .docx contains word/document.xml");
                    using(var stream=part.Open())using(var reader=new StreamReader(stream))
                    {
                        string xml=reader.ReadToEnd();
                        Check(xml.Contains("Heading1") && xml.Contains("<w:b/>"),"markdown headings and bold become real Word styles");
                    }
                }
                string read=Call(w,"read_file",new{path="notes.docx"});
                Check(read.Contains("1: 项目计划") && read.Contains("第一部分") && read.Contains("重点：完成读取") && read.Contains("事项一"),"read_file extracts .docx paragraphs, headings and bold text with line numbers");
                string firstHash=Hash(docxPath);
                var change=w.Changes.First(c=>Path.GetFileName(c.Path)=="notes.docx");
                Check(change.AddedLines.HasValue && change.AddedLines.Value>=3,"document writes report readable added line counts");
                Check(!Call(w,"write_file",new{path="notes.docx",content="第二版\n内容"}).StartsWith("错误："),"an existing .docx can be fully rewritten");
                string restore=Call(w,"restore_file",new{path="notes.docx"});Check(!restore.StartsWith("错误：") && Hash(docxPath)==firstHash,"restore_file returns the exact original document bytes");
                string edit=Call(w,"edit_file",new{path="notes.docx",old_text="重点",new_text="其他"});
                Check(edit.StartsWith("错误：") && edit.Contains("write_file"),"edit_file refuses binary Word documents with clear guidance");
                string patch=Call(w,"apply_patch",new{edits=new[]{new{path="notes.docx",old_text="重点",new_text="其他"}}});
                Check(patch.StartsWith("错误：") && patch.Contains("write_file"),"apply_patch refuses document old_text edits");

                string wroteXlsx=Call(w,"write_file",new{path="data.xlsx",content="名称\t数量\n苹果\t3\n梨\t12.5"});
                Check(!wroteXlsx.StartsWith("错误：") && File.Exists(Path.Combine(root,"data.xlsx")),"write_file creates a real .xlsx package");
                string xlsxRead=Call(w,"read_file",new{path="data.xlsx"});
                Check(xlsxRead.Contains("[工作表 Sheet1]") && xlsxRead.Contains("名称\t数量") && xlsxRead.Contains("苹果\t3") && xlsxRead.Contains("梨\t12.5"),"read_file extracts .xlsx rows and real cell values");
                Call(w,"write_file",new{path="csv.xlsx",content="a,b\n\"x, y\",2"});
                string csvRead=Call(w,"read_file",new{path="csv.xlsx"});
                Check(csvRead.Contains("a\tb") && csvRead.Contains("x, y\t2"),"quoted comma input becomes real quoted cells");

                string shared=Path.Combine(root,"shared.xlsx");WriteSharedFixture(shared);
                string sharedRead=Call(w,"read_file",new{path="shared.xlsx"});
                Check(sharedRead.Contains("[工作表 明细]") && sharedRead.Contains("甲\t\t丙") && sharedRead.Contains("乙"),"shared strings and column gaps map to tabs");
                string table=Path.Combine(root,"table.docx");WriteTableFixture(table);
                string tableRead=Call(w,"read_file",new{path="table.docx"});
                Check(tableRead.Contains("前言") && tableRead.Contains("甲列 \t乙列") && tableRead.Contains("结尾"),"docx tables extract cell text with tabs");
                Call(w,"write_file",new{path="search.docx",content="唯一标记-文档搜索\n其他内容"});
                string found=Call(w,"search_files",new{query="唯一标记-文档搜索"});
                Check(found.Contains("search.docx"),"search_files includes .docx document text");

                string legacy=Call(w,"write_file",new{path="old.doc",content="旧版文档\n第二行"});
                if(!legacy.StartsWith("错误："))
                {
                    string legacyRead=Call(w,"read_file",new{path="old.doc"});
                    Check(legacyRead.Contains("旧版文档") && legacyRead.Contains("第二行"),"legacy .doc round-trips through the installed WPS or Word");
                }
                else Check(legacy.Contains(".docx") || legacy.Contains("Office/WPS"),"unavailable .doc conversion explains the .docx fallback");
                string legacyXls=Call(w,"write_file",new{path="old.xls",content="名称\t数量\n苹果\t3"});
                if(!legacyXls.StartsWith("错误："))
                {
                    string legacyXlsRead=Call(w,"read_file",new{path="old.xls"});
                    Check(legacyXlsRead.Contains("苹果\t3"),"legacy .xls round-trips through the installed WPS or Excel");
                }
                else Check(legacyXls.Contains(".xlsx") || legacyXls.Contains("Office/WPS"),"unavailable .xls conversion explains the .xlsx fallback");
                var legacyType=typeof(DocumentFormatsLegacy);
                var harden=legacyType.GetMethod("HardenApplication",BindingFlags.Static|BindingFlags.NonPublic);
                Check(harden!=null,"legacy Office automation exposes explicit security hardening");
                var fake=new FakeLegacyApplication();
                harden.Invoke(null,new object[]{fake,true});
                Check(fake.AutomationSecurity==3 && !fake.EnableEvents && !fake.Visible && Object.Equals(fake.DisplayAlerts,0),"legacy Word automation disables macros and alerts before opening documents");
                harden.Invoke(null,new object[]{fake,false});
                Check(fake.AutomationSecurity==3 && !fake.EnableEvents && !fake.Visible && Object.Equals(fake.DisplayAlerts,false),"legacy Excel automation disables macros and events before opening workbooks");
                var run=legacyType.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="RunSta" && m.IsGenericMethodDefinition && m.GetParameters().Length==4);
                bool cleaned=false;DateTime timeoutStarted=DateTime.UtcNow;bool timedOut=false;
                try{
                    run.MakeGenericMethod(typeof(int)).Invoke(null,new object[]{(Func<CancellationToken,int>)(ct=>{ct.WaitHandle.WaitOne();return 0;}),CancellationToken.None,50,(Action)(()=>cleaned=true)});
                }catch(TargetInvocationException error){timedOut=error.InnerException is ArgumentException;}
                Check(timedOut && cleaned && DateTime.UtcNow-timeoutStarted<TimeSpan.FromSeconds(2),"legacy conversion timeout cancels work and performs cleanup");
                var releaseCleanup=new ManualResetEvent(false);int cleanupCalls=0;bool callerReturned=false;
                var caller=new Thread(delegate(){try{run.MakeGenericMethod(typeof(int)).Invoke(null,new object[]{(Func<CancellationToken,int>)(ct=>{ct.WaitHandle.WaitOne();return 0;}),CancellationToken.None,50,(Action)(()=>{releaseCleanup.WaitOne();Interlocked.Increment(ref cleanupCalls);})});}catch(TargetInvocationException error){Check(error.InnerException is ArgumentException,"non-cooperative cleanup still reports conversion timeout");}finally{callerReturned=true;}});
                caller.IsBackground=true;caller.Start();bool returnedBeforeCleanup=caller.Join(500);releaseCleanup.Set();bool cleanupFinished=caller.Join(2000);DateTime cleanupDeadline=DateTime.UtcNow.AddSeconds(2);while(cleanupCalls==0 && DateTime.UtcNow<cleanupDeadline)Thread.Sleep(10);
                Check(returnedBeforeCleanup && cleanupFinished && callerReturned && cleanupCalls>0,"conversion timeout returns while cleanup finishes asynchronously");
                Console.WriteLine("Agent document tests: "+checks+" passed");
                return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        sealed class FakeLegacyApplication
        {
            public bool Visible{get;set;}
            public object DisplayAlerts{get;set;}
            public int AutomationSecurity{get;set;}
            public bool EnableEvents{get;set;}
        }
        static void Add(ZipArchive zip,string name,string content)
        {
            var entry=zip.CreateEntry(name);
            using(var stream=entry.Open())using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(content);
        }
        static void WriteSharedFixture(string path)
        {
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
            {
                Add(zip,"[Content_Types].xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Add(zip,"_rels/.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add(zip,"xl/workbook.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"明细\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add(zip,"xl/_rels/workbook.xml.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Add(zip,"xl/sharedStrings.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"3\" uniqueCount=\"3\"><si><t>甲</t></si><si><t>丙</t></si><si><t>乙</t></si></sst>");
                Add(zip,"xl/worksheets/sheet1.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"C1\" t=\"s\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c></row></sheetData></worksheet>");
            }
        }
        static void WriteTableFixture(string path)
        {
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
            {
                Add(zip,"[Content_Types].xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                Add(zip,"_rels/.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                Add(zip,"word/document.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>前言</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>甲列</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>乙列</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p><w:r><w:t>结尾</w:t></w:r></w:p></w:body></w:document>");
            }
        }
    }
}
