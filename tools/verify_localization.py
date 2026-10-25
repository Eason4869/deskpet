"""Validate the single embedded catalog and source inventory, using Python stdlib.

Roslyn is used only to establish/review the initial inventory. CI does not need it.
"""
import argparse
import collections
import hashlib
import json
import re
import io
import subprocess
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HAN = re.compile(r"[\u3400-\u9fff]")
ORIGINAL_LOCALE_BASE = 'fde04835ffce544c334205d881a232c579ded4df'


def digest(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def string_tokens(source):
    """Yield (start,end,value,kind), excluding comments and character literals.

    Interpolated/raw literals are surfaced explicitly for review, never silently
    treated as ordinary strings. Both preprocessor branches remain in the audit.
    """
    i = 0
    escapes = {"0": "\0", "a": "\a", "b": "\b", "f": "\f", "n": "\n", "r": "\r", "t": "\t", "v": "\v"}
    while i < len(source):
        if source.startswith("//", i):
            j = source.find("\n", i); i = len(source) if j < 0 else j + 1; continue
        if source.startswith("/*", i):
            j = source.find("*/", i + 2)
            if j < 0: raise ValueError("Unterminated C# comment")
            i = j + 2; continue
        if source[i] == "'":
            i += 1
            while i < len(source):
                if source[i] == "\\": i += 2
                elif source[i] == "'": i += 1; break
                else: i += 1
            continue
        prefix = next((p for p in ('$@"', '@$"', '@"', '$"', '"') if source.startswith(p, i)), None)
        if prefix is None: i += 1; continue
        start = i; i += len(prefix); value = []; verbatim = "@" in prefix; interpolated = "$" in prefix
        if source.startswith('""', i):
            quotes = 1
            while i < len(source) and source[i] == '"': quotes += 1; i += 1
            close = source.find('"' * quotes, i)
            if close < 0: raise ValueError("Unterminated raw C# literal")
            yield start, close + quotes, source[i:close], "raw"
            i = close + quotes; continue
        while i < len(source):
            c = source[i]; i += 1
            if interpolated and c=='{':
                if i<len(source) and source[i]=='{':value.append('{{');i+=1;continue
                end=interpolation_end(source,i)
                value.append('{'+source[i:end]);i=end;continue
            if c == '"':
                if verbatim and i < len(source) and source[i] == '"': value.append('"'); i += 1; continue
                break
            if c == "\\" and not verbatim:
                c = source[i]; i += 1
                if c in "uU":
                    count = 4 if c == "u" else 8
                    value.append(chr(int(source[i:i + count], 16))); i += count
                elif c == "x":
                    match = re.match(r"[0-9a-fA-F]{1,4}", source[i:])
                    if not match: raise ValueError("Invalid C# hex escape")
                    value.append(chr(int(match[0], 16))); i += len(match[0])
                else: value.append(escapes.get(c, c))
            else: value.append(c)
        else: raise ValueError("Unterminated C# string")
        yield start, i, "".join(value), "interpolated" if interpolated else "literal"


def interpolation_end(source,i):
    """Consume a balanced interpolation, including nested C# string expressions."""
    depth=1
    while i<len(source):
        if source.startswith('//',i):
            end=source.find('\n',i);i=len(source) if end<0 else end+1;continue
        if source.startswith('/*',i):
            end=source.find('*/',i+2)
            if end<0:raise ValueError('Unterminated interpolation comment')
            i=end+2;continue
        if any(source.startswith(p,i) for p in ('$@"','@$"','@"','$"','"')):
            token=next(string_tokens(source[i:]));i+=token[1];continue
        if source[i]=="'":
            i+=1
            while i<len(source):
                if source[i]=='\\':i+=2
                elif source[i]=="'":i+=1;break
                else:i+=1
            continue
        if source[i]=='{':depth+=1
        elif source[i]=='}':
            depth-=1
            if depth==0:return i+1
        i+=1
    raise ValueError('Unterminated interpolated expression')


def load_catalog(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result: raise ValueError("Duplicate resource key: " + key)
            result[key] = value
        return result
    result = json.loads(text, object_pairs_hook=pairs)
    if not isinstance(result, dict): raise ValueError("Catalog must be an object")
    for key, value in result.items():
        if not key.strip() or not isinstance(value, str): raise ValueError("Invalid catalog entry: " + key)
        if not re.fullmatch(r"[a-z][A-Za-z0-9]*(?:\.[a-z][A-Za-z0-9]*)+", key): raise ValueError("Invalid resource key: " + key)
    return result


def placeholders(template):
    indexes = set(); i = 0
    while i < len(template):
        if template[i:i + 2] in ("{{", "}}"): i += 2; continue
        if template[i] == "{":
            match = re.match(r"\{([0-9]+)(?:,-?\d+)?(?::[^{}]*)?\}", template[i:])
            if not match: raise ValueError("Invalid numbered format template: " + template)
            indexes.add(int(match[1])); i += len(match[0]); continue
        if template[i] == "}": raise ValueError("Unescaped closing brace: " + template)
        i += 1
    return indexes


def context_at(source, start):
    """Freeze the exact source line, so an exclusion cannot bless another use."""
    first=source.rfind('\n',0,start)+1
    last=source.find('\n',start)
    return digest(source[first:len(source) if last<0 else last].strip())


def resource_call_details(source):
    tokens=list(string_tokens(source))
    masked=list(source)
    for start,end,_,_ in tokens:
        masked[start:end]=' '*(end-start)
    # Comments must also be blanked before looking for executable calls.
    code=''.join(masked)
    code=re.sub(r'//[^\n]*|/\*[\s\S]*?\*/',lambda m:' '*(m.end()-m.start()),code)
    for match in re.finditer(r'\bTextResources\s*\.\s*(Get|Format|FormatForCatalog|StartupMessage|Message)\s*\(',code):
        depth=1;first=match.end();i=first;args=[]
        while i<len(code) and depth:
            c=code[i]
            if c in '([{':depth+=1
            elif c in ')]}':depth-=1
            if depth==1 and c==',':args.append((first,i));first=i+1
            if depth==0:args.append((first,i))
            i+=1
        if depth:raise ValueError('Unterminated resource call')
        method=match.group(1)
        if method=='Message':method='Format' if len(args)>1 else 'Get'
        yield ('Format' if method=='FormatForCatalog' else method),[source[a:b].strip() for a,b in args],match.start(),i


def resource_calls(source):
    for method,args,_,_ in resource_call_details(source):
        yield method,args


def canonical_digest(value):
    return hashlib.sha256((json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':'))+'\n').encode('utf8')).hexdigest()


def feature_resources(catalog, baseline):
    """Bind each post-migration key to separate text, Git and callsite evidence.

    This is technical provenance, not a claim of human/native translation review.
    Historical expectations and migrated rows remain independently enforced.
    """
    errors=[]; exclusions=collections.Counter()
    prefix='Feature provenance: '
    base_keys={row['key'] for row in baseline['resources']}
    new_keys=set(catalog)-base_keys
    paths=['tests/fixtures/localization/protected-originals.json',
           'tests/fixtures/localization/feature-text-baseline.json',
           'docs/localization/feature-resource-provenance.json']
    if not all((ROOT/path).is_file() for path in paths):
        return [prefix+'missing independent feature/protected-original evidence'],exclusions
    try:
        def evidence_pairs(items):
            result={}
            for key,value in items:
                if key in result:raise ValueError('duplicate evidence field: '+key)
                result[key]=value
            return result
        protected,contract,provenance=[json.loads((ROOT/path).read_text(encoding='utf8'),object_pairs_hook=evidence_pairs) for path in paths]
        if any(data.get('schemaVersion')!=1 for data in (protected,contract,provenance)):raise ValueError('unknown evidence schema')
        if set(protected)!={'schemaVersion','baseCommit','catalogs'} or set(contract)!={'schemaVersion','resources'}:raise ValueError('unknown independent contract field')
        if set(provenance)!={'schemaVersion','protectedBaseCommit','resourceCommit','translationMethod','nativeHumanReview','technicalReview','resources','sourceExclusions'}:raise ValueError('unknown or missing provenance field')
        if provenance['translationMethod']!='assistant-authored' or provenance['nativeHumanReview']!='not_performed' or provenance['technicalReview']!='pending_parent_review':raise ValueError('unsupported review declaration; do not invent review approval')
        cache={}
        def git(*args):
            return subprocess.check_output(['git',*args],cwd=ROOT,stderr=subprocess.PIPE)
        def commit(value):
            if not re.fullmatch('[a-f0-9]{40}',value):raise ValueError('invalid commit anchor')
            git('cat-file','-e',value+'^{commit}')
            git('merge-base','--is-ancestor',value,'HEAD')
        def at(sha,path):
            if (sha,path) not in cache:cache[sha,path]=git('show',sha+':'+path).decode('utf-8-sig')
            return cache[sha,path]
        def json_at(sha,path):return json.loads(at(sha,path))
        original=protected['baseCommit'];source_commit=provenance['resourceCommit']
        if original!=ORIGINAL_LOCALE_BASE:raise ValueError('protected original commit is fixed; evidence cannot redefine it')
        if provenance['protectedBaseCommit']!=original:raise ValueError('protected base anchor mismatch')
        commit(original);commit(source_commit)
        original_zh=json_at(original,'assets/locales/zh_CN.json')
        if set(original_zh)!=base_keys:raise ValueError('historical key set changed')
        if json_at(original,'tests/fixtures/localization/text-baseline.json')!=baseline:raise ValueError('historical independent baseline changed')
        old_inventory=[json.loads(line) for line in at(original,'docs/localization/migration-inventory.jsonl').splitlines()]
        inventory=[json.loads(line) for line in (ROOT/'docs/localization/migration-inventory.jsonl').read_text(encoding='utf8').splitlines()]
        def immutable_rows(rows):return [{k:v for k,v in row.items() if k!='retired_reason'} for row in rows]
        if immutable_rows(old_inventory)!=immutable_rows(inventory):raise ValueError('historical migration provenance changed')
        locales={'zh_CN','zh_TW','en_US','ja_JP','ko_KR','fr_FR','de_DE','hi_IN','id_ID','it_IT','pt_BR','es_419','es_ES'}
        if set(protected['catalogs'])!=locales:raise ValueError('protected locale set changed')
        current={};anchored={}
        for locale in sorted(locales):
            path='assets/locales/'+locale+'.json';old=json_at(original,path);entry=protected['catalogs'][locale]
            if set(old)!=base_keys or entry!={'keyCount':len(old),'valuesSha256':canonical_digest(old)}:raise ValueError('invalid protected snapshot '+locale)
            current[locale]=load_catalog((ROOT/path).read_text(encoding='utf8'));anchored[locale]=load_catalog(at(source_commit,path))
            if canonical_digest({key:current[locale].get(key) for key in base_keys})!=entry['valuesSha256']:errors.append(prefix+'original translation drift: '+locale)
            if set(current[locale])!=set(catalog):errors.append(prefix+'locale key set differs: '+locale)
        def indexed(rows,label):
            result={}
            for row in rows:
                key=row['key']
                if key in result:raise ValueError('duplicate '+label+' key '+key)
                result[key]=row
            if set(result)!=new_keys:raise ValueError(label+' must cover exactly every nonhistorical key')
            return result
        contracts=indexed(contract['resources'],'independent feature contract');records=indexed(provenance['resources'],'source provenance')
        files={p.relative_to(ROOT).as_posix():p for p in production_files()}
        references=collections.defaultdict(collections.Counter);pinned_references=collections.defaultdict(collections.Counter);methods=collections.defaultdict(set)
        def references_in(source,file,result,actual=False):
            for method,args,start,_ in resource_call_details(source):
                key_at=1 if method=='Format' and not re.fullmatch(r'"[^"\n]*"',args[0]) else 0
                if len(args)<=key_at or not re.fullmatch(r'"[^"\n]*"',args[key_at]):continue
                key=args[key_at][1:-1]
                if key not in new_keys:continue
                result[key][file,context_at(source,start)]+=1
                if actual:methods[key].add(method)
        for file,path in files.items():references_in(path.read_text(encoding='utf-8-sig'),file,references,True)
        pinned_files={ref['file'] for row in records.values() for ref in row['sourceReferences']}
        for file in pinned_files:
            if file not in files:raise ValueError('source reference outside production files: '+file)
            references_in(at(source_commit,file),file,pinned_references)
        renames={('relationship.'+kind+'.'+old):('relationship.'+kind+'.'+new) for kind in ('stage','welcome','interaction') for old,new in [('first_meeting','firstMeeting'),('best_friend','bestFriend')]}
        for key,row in records.items():
            if set(row)!={'key','originCommit','originKey','originValueSha256','textSha256','sourceReferences','translations'}:raise ValueError('unknown resource provenance field: '+key)
            expected=contracts[key]
            if set(expected)!={'key','expected','format','argument_count'} or type(expected['format']) is not bool or type(expected['argument_count']) is not int or expected['argument_count']<0:raise ValueError('invalid text/format contract: '+key)
            if catalog[key]!=expected['expected'] or digest(catalog[key])!=row['textSha256']:errors.append(prefix+'new text drift: '+key)
            origin=row['originCommit'];commit(origin);git('merge-base','--is-ancestor',origin,source_commit)
            origin_key=row['originKey'];origin_catalog=json_at(origin,'assets/locales/zh_CN.json')
            if origin_key in base_keys or (origin_key!=key and renames.get(origin_key)!=key):raise ValueError('invalid new-key origin mapping: '+key)
            if origin_key not in origin_catalog or digest(origin_catalog[origin_key])!=row['originValueSha256']:raise ValueError('origin text/hash mismatch: '+key)
            declared=collections.Counter()
            for ref in row['sourceReferences']:
                if set(ref)!={'file','contextHash','count'} or type(ref['count']) is not int or ref['count']<1 or not re.fullmatch('[a-f0-9]{64}',ref['contextHash']):raise ValueError('invalid source reference: '+key)
                item=(ref['file'],ref['contextHash'])
                if item in declared:raise ValueError('duplicate source reference: '+key)
                declared[item]=ref['count']
            if declared!=references[key] or declared!=pinned_references[key]:errors.append(prefix+'literal callsite drift: '+key)
            if set(row['translations'])!=locales:raise ValueError('missing locale text hashes: '+key)
            for locale in sorted(locales):
                value=current[locale].get(key);pinned=anchored[locale].get(key)
                if value is None or digest(value)!=row['translations'][locale] or pinned!=value:errors.append(prefix+'translation/commit drift: '+locale+' '+key)
                if expected['format'] and value is not None and placeholders(value)!=set(range(expected['argument_count'])):errors.append(prefix+'placeholder drift: '+locale+' '+key)
            if expected['format'] and methods[key]-{'Format'}:errors.append(prefix+'formatted key read without Format: '+key)
            if not expected['format'] and 'Format' in methods[key]:errors.append(prefix+'missing independent Format contract: '+key)
        for row in provenance['sourceExclusions']:
            if set(row)!={'file','value','literalHash','sourceContextHash','sourceCommit','reason'} or not row['reason'].strip() or row['sourceCommit']!=source_commit or row['literalHash']!=digest(row['value']):raise ValueError('invalid feature source exclusion')
            file=row['file']
            if file not in files:raise ValueError('excluded source outside production')
            def matching(text):return sum(value==row['value'] and context_at(text,start)==row['sourceContextHash'] for start,_,value,_ in string_tokens(text))
            if matching(files[file].read_text(encoding='utf-8-sig'))!=1 or matching(at(source_commit,file))!=1:raise ValueError('excluded source context drift: '+file)
            item=(file,row['literalHash'],row['sourceContextHash'])
            if item in exclusions:raise ValueError('duplicate feature source exclusion')
            exclusions[item]=1
    except (KeyError,TypeError,ValueError,OSError,subprocess.CalledProcessError) as error:
        errors.append(prefix+str(error))
    return errors,exclusions


def check_references(source,catalog):
    references=collections.Counter();errors=[]
    for method,args in resource_calls(source):
        key_at=0
        if method=='Format' and not re.fullmatch(r'"[^"\n]*"',args[0]):key_at=1
        if len(args)<=key_at or not re.fullmatch(r'"[^"\n]*"',args[key_at]):
            errors.append('Resource key must be a reviewed literal: '+str(args));continue
        key=args[key_at][1:-1];references[key]+=1
        if key not in catalog:errors.append('missing key '+key);continue
        if method=='Format':
            try:
                needed=placeholders(catalog[key]);supplied=len(args)-key_at-1
                if needed and max(needed)>=supplied:errors.append('Insufficient format arguments: '+key)
            except ValueError as error:errors.append(str(error))
    return references,errors


def production_files():
    return sorted(list((ROOT / "src").rglob("*.cs")) + [p for folder in ["desktop/DeskPet.Desktop", "desktop/DeskPet.Platform.Mac"] for p in (ROOT / folder).rglob("*.cs") if not {"obj", "bin", "Tests"}.intersection(p.parts)])


def inventory_at(baseline):
    """Read committed source, never regenerate the independent text baseline."""
    commit=subprocess.check_output(['git','rev-parse',baseline+'^{commit}'],cwd=ROOT,text=True).strip()
    archive=subprocess.check_output(['git','archive','--format=tar',commit,'src','desktop'],cwd=ROOT)
    rows=[json.loads(line) for line in (ROOT/'docs/localization/migration-inventory.jsonl').read_text(encoding='utf8').splitlines()]
    reviewed=collections.Counter((r['file'],r['literal_hash']) for r in rows if r['baseline_sha']==commit and r.get('reviewed'))
    candidates=[]
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        for entry in tar:
            name=entry.name
            if not entry.isfile() or not name.endswith('.cs') or {'Tests','obj','bin'}.intersection(Path(name).parts) or not (name.startswith('src/') or name.startswith('desktop/DeskPet.Desktop/') or name.startswith('desktop/DeskPet.Platform.Mac/')):continue
            source=tar.extractfile(entry).read().decode('utf-8-sig')
            for start,end,value,kind in string_tokens(source):
                if kind=='interpolated':value=source[start:end]
                if HAN.search(value):candidates.append(dict(file=name,literal_hash=digest(value),kind=kind,line=source.count('\n',0,start)+1))
    actual=collections.Counter((r['file'],r['literal_hash']) for r in candidates)
    unmatched=[dict(file=file,literal_hash=hashed,count=count) for (file,hashed),count in (actual-reviewed).items()]
    return dict(success=not unmatched,baseline_sha=commit,candidates=len(candidates),unclassified=unmatched)


def check(phase="complete"):
    catalog_path = ROOT / "assets/locales/zh_CN.json"
    raw = catalog_path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"): raise ValueError("Catalog must be UTF-8 without BOM")
    catalog = load_catalog(raw.decode("utf-8"))
    inventory = [json.loads(line) for line in (ROOT / "docs/localization/migration-inventory.jsonl").read_text(encoding="utf-8").splitlines()]
    errors = []; references = collections.Counter(); residual = collections.Counter()
    for path in production_files():
        relative = path.relative_to(ROOT).as_posix()
        source = path.read_text(encoding="utf-8-sig")
        for start, _, value, kind in string_tokens(source):
            if HAN.search(value): residual[(relative, digest(value), context_at(source,start))] += 1
        found,issues=check_references(source,catalog);references.update(found)
        errors.extend(relative+': '+issue for issue in issues)
    expected = collections.Counter()
    for item in inventory:
        if item['literal_hash']!=digest(item['value']):errors.append('Inventory literal hash mismatch: '+item['file'])
        if item["decision"] == "exclude":
            if not item.get("exclusion_reason") or not item.get("reviewed"): errors.append("Unreviewed exclusion: " + item["file"])
            if HAN.search(item["value"]) and not item.get('retired_reason'):
                if not item.get('source_context_hash'):errors.append('Missing exclusion source context: '+item['file'])
                expected[(item["file"], digest(item["value"]),item.get('source_context_hash',''))] += 1
        elif item["decision"] == "migrate":
            if item["resource_key"] not in catalog: errors.append("Inventory resource absent: " + item["resource_key"])
        else: errors.append("Unclassified candidate")
    if phase == "complete":
        baseline = json.loads((ROOT / "tests/fixtures/localization/text-baseline.json").read_text(encoding="utf-8"))
        feature_errors,feature_exclusions=feature_resources(catalog,baseline)
        errors.extend(feature_errors);expected.update(feature_exclusions)
        for (file, hashed, context), count in (residual - expected).items(): errors.append(file + ": new/unclassified C# text " + hashed[:12] + " x" + str(count))
        for (file, hashed, context), count in (expected - residual).items(): errors.append(file + ": excluded text/context changed " + hashed[:12] + " x" + str(count))
        baseline_keys={item['key'] for item in baseline['resources']}
        migrated_keys={item['resource_key'] for item in inventory if item['decision']=='migrate' and item.get('reviewed')}
        for key in catalog:
            if key in baseline_keys and key not in migrated_keys:errors.append('Resource lacks reviewed inventory provenance: '+key)
        for item in baseline["resources"]:
            key = item["key"]
            if catalog.get(key) != item["expected"]: errors.append("Original text drift: " + key)
            if item["format"]:
                try:
                    actual = placeholders(catalog[key])
                    if actual != set(range(item["argument_count"])): errors.append("Placeholder mismatch: " + key)
                except ValueError as error: errors.append(str(error))
        retired_path=ROOT/'docs/localization/retired-keys.json'
        retired=json.loads(retired_path.read_text(encoding='utf8')) if retired_path.exists() else {}
        for key,reason in retired.items():
            if key not in catalog or not reason.strip():errors.append('Invalid retired key explanation: '+key)
        for key in catalog:
            if not references[key] and key not in retired: errors.append("Unreferenced key: " + key)
            if not catalog[key]: errors.append("Unexplained empty resource: " + key)
    return {"success": not errors, "phase": phase, "resources": len(catalog), "candidates": len(inventory), "migrated": sum(i["decision"] == "migrate" for i in inventory), "excluded": sum(i["decision"] == "exclude" for i in inventory), "errors": errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["check", "inventory"])
    parser.add_argument("--phase", choices=["inventory", "complete"], default="complete")
    parser.add_argument("--baseline")
    parser.add_argument("--report")
    args = parser.parse_args()
    if args.command == "inventory":
        if not args.baseline:parser.error('inventory requires --baseline <commit>')
        result=inventory_at(args.baseline)
    else:result = check(args.phase)
    if args.report:
        path = ROOT / args.report; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["success"] else 1


if __name__ == "__main__": raise SystemExit(main())
