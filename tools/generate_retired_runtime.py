"""Rebuild exact historical hashes; never derive deletion rules from user installations."""
from pathlib import Path
import hashlib
import subprocess

ROOT=Path(__file__).resolve().parents[1]
BASE='318a82bf754b349445a08ed0bfc3bc3893e1dbe0'

def records():
    import ast,csv,io
    names=[name for name in subprocess.check_output(['git','ls-tree','-rz','--name-only',BASE],cwd=ROOT).decode('utf-8').split('\0') if name]
    def blob(name):return subprocess.check_output(['git','show',BASE+':'+name],cwd=ROOT)
    def text(name):return blob(name).decode('utf-8-sig').replace('\r\n','\n').replace('\r','\n')
    def written(value):return value.replace('\n','\r\n').encode('utf-8')
    old=ast.parse(text('Package.py'));table=next(node for node in ast.walk(old) if isinstance(node,ast.FunctionDef) and node.name=='table_row')
    namespace={};exec(compile(ast.fix_missing_locations(ast.Module(body=[table],type_ignores=[])),'historical-table-converter','exec'),namespace)
    high=[name for name in names if name.startswith('templates/high-fps-v2/') and Path(name).suffix.lower() in {'.png','.json','.svg','.md','.txt','.csv'}]
    note_names={Path(name).name:Path(name).with_suffix('.md').name for name in high if Path(name).suffix.lower() in {'.txt','.csv'}}
    planned={}
    for name in names:
        path=Path(name);suffix=path.suffix.lower()
        if path.parent.as_posix()=='previews' and suffix in {'.png','.jpg','.webp','.svg'}:planned[name]=blob(name)
        elif path.parent.as_posix()=='assets/avatars' and suffix=='.svg':planned['previews/avatars/'+path.name]=blob(name)
        elif name.startswith('templates/') and not name.startswith('templates/high-fps-v2/'):planned[name]=blob(name)
    for name in high:
        path=Path(name);suffix=path.suffix.lower();target=path.with_suffix('.md').as_posix() if suffix in {'.csv','.txt'} else name
        if suffix=='.csv':
            rows=list(csv.reader(io.StringIO(text(name))));row=namespace['table_row'];content='# '+path.stem+'\n\n'+row(rows[0])+'\n'+row(['---']*len(rows[0]))+'\n'+'\n'.join(row(value) for value in rows[1:])+'\n';planned[target]=written(content)
        elif suffix=='.txt':planned[target]=written('# '+path.stem+'\n\n'+text(name))
        elif suffix=='.md':
            content=text(name)
            for old_name,new_name in note_names.items():content=content.replace(old_name,new_name)
            planned[target]=written(content)
        else:planned[target]=blob(name)
    for name in ['docs/native-agent-design.md','docs/roadmap.md','docs/release-naming.md']:planned[name]=written(text(name))
    for name,content in sorted(planned.items()):yield name,hashlib.sha256(content).hexdigest()

if __name__=='__main__':
    data=list(records())
    lines=['using System.Collections.Generic;','namespace Dafeiyu { internal static class RetiredRuntimeFiles {',
           '    // Exact Windows 0.9.3 runtime-plan bytes; modified user files are retained.',
           '    internal static readonly IDictionary<string,string> Known=new Dictionary<string,string> {']
    lines += ['        {"'+name+'","'+digest+'"},' for name,digest in data]
    lines += ['    };','} }']
    (ROOT/'src/RetiredRuntimeFiles.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    pascal=['procedure RetireKnownFiles;','begin']
    pascal += ["  RetireFile('"+name.replace('/','\\').replace("'","''")+"', '"+digest+"');" for name,digest in data]
    pascal += ['end;']
    (ROOT/'installer/RetiredFiles.iss').write_text('\n'.join(pascal)+'\n',encoding='utf-8-sig')
