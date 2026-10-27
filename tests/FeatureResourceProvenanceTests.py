"""A real Git fixture keeps new copy separate from historical migration evidence."""
import copy
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import verify_localization as audit


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')) + '\n').encode('utf8')


class FeatureProvenanceTests(unittest.TestCase):
    def setUp(self):
        (ROOT/'work').mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT / 'work', prefix='feature-provenance-')
        self.root = Path(self.temp.name)
        self.previous = audit.ROOT
        audit.ROOT = self.root
        self.git('init', '-q')
        self.locales = ['zh_CN','zh_TW','en_US','ja_JP','ko_KR','fr_FR','de_DE','hi_IN','id_ID','it_IT','pt_BR','es_419','es_ES']
        self.write('src/App.cs', 'class App { object old = TextResources.Get("ui.old"); }\n')
        for locale in self.locales:
            self.write('assets/locales/' + locale + '.json', {'ui.old': 'Original ' + locale})
        self.write('tests/fixtures/localization/text-baseline.json', {'resources': [{'key':'ui.old','expected':'Original zh_CN','format':False,'argument_count':0}]})
        self.write('docs/localization/migration-inventory.jsonl', json.dumps({'file':'src/App.cs','literal_hash':audit.digest('Original zh_CN'),'value':'Original zh_CN','decision':'migrate','resource_key':'ui.old','reviewed':True}) + '\n')
        self.base = self.commit('original fixture')
        self.previous_base=getattr(audit,'ORIGINAL_LOCALE_BASE',None)
        audit.ORIGINAL_LOCALE_BASE=self.base
        protected = {'schemaVersion':1, 'baseCommit':self.base, 'catalogs':{}}
        for locale in self.locales:
            protected['catalogs'][locale] = {'keyCount':1,'valuesSha256':hashlib.sha256(encoded({'ui.old':'Original ' + locale})).hexdigest()}
        self.write('tests/fixtures/localization/protected-originals.json', protected)
        self.write('src/App.cs', 'class App { object old = TextResources.Get("ui.old"); object added = TextResources.Format("ui.new", 3); }\n')
        for locale in self.locales:
            self.write('assets/locales/' + locale + '.json', {'ui.old':'Original ' + locale,'ui.new':'New {0} ' + locale})
        self.write('tests/fixtures/localization/feature-text-baseline.json', {'schemaVersion':1,'resources':[{'key':'ui.new','expected':'New {0} zh_CN','format':True,'argument_count':1}]})
        self.source = self.commit('new feature fixture')
        self.provenance = {'schemaVersion':1,'protectedBaseCommit':self.base,'resourceCommit':self.source,
            'translationMethod':'assistant-authored','nativeHumanReview':'not_performed','technicalReview':'pending_parent_review',
            'resources':[{'key':'ui.new','originCommit':self.source,'originKey':'ui.new','originValueSha256':audit.digest('New {0} zh_CN'),
                'textSha256':audit.digest('New {0} zh_CN'),
                'sourceReferences':[{'file':'src/App.cs','contextHash':audit.digest('class App { object old = TextResources.Get("ui.old"); object added = TextResources.Format("ui.new", 3); }'),'count':1}],
                'translations':{locale:audit.digest('New {0} ' + locale) for locale in self.locales}}], 'sourceExclusions':[]}
        self.write('docs/localization/feature-resource-provenance.json', self.provenance)

    def tearDown(self):
        audit.ROOT = self.previous
        if self.previous_base is None:del audit.ORIGINAL_LOCALE_BASE
        else:audit.ORIGINAL_LOCALE_BASE=self.previous_base
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.check_output(['git','-c','user.name=Codex','-c','user.email=codex@openai.com',*args], cwd=self.root, text=True).strip()

    def commit(self, message):
        self.git('add', '.')
        self.git('commit','-qm',message)
        return self.git('rev-parse','HEAD')

    def write(self, path, value):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n' if not isinstance(value,str) else value, encoding='utf8')

    def check(self):
        return audit.check()

    def test_exact_new_feature_contract_can_pass_without_forging_migration(self):
        result = self.check()
        self.assertTrue(result['success'], result['errors'])

    def test_missing_provenance_cannot_exempt_a_prefix(self):
        self.provenance['resources'] = []
        self.write('docs/localization/feature-resource-provenance.json',self.provenance)
        self.assertFalse(self.check()['success'])

    def test_changed_original_translation_is_rejected(self):
        self.write('assets/locales/ja_JP.json', {'ui.old':'Changed','ui.new':'New {0} ja_JP'})
        self.assertFalse(self.check()['success'])

    def test_forged_origin_or_source_commit_is_rejected(self):
        for field in ['resourceCommit','originCommit']:
            changed = copy.deepcopy(self.provenance)
            if field == 'resourceCommit': changed[field] = '0' * 40
            else: changed['resources'][0][field] = '0' * 40
            self.write('docs/localization/feature-resource-provenance.json', changed)
            self.assertFalse(self.check()['success'])

    def test_changed_reference_text_and_placeholder_are_rejected(self):
        cases = [('src/App.cs','class App { object old = TextResources.Get("ui.old"); object added = TextResources.Format("ui.new"); }\n'),
                 ('assets/locales/zh_CN.json',{'ui.old':'Original zh_CN','ui.new':'Different {0}'}),
                 ('assets/locales/hi_IN.json',{'ui.old':'Original hi_IN','ui.new':'New {1} hi_IN'})]
        originals = {path:(self.root/path).read_text(encoding='utf8') for path,_ in cases}
        for path,value in cases:
            self.write(path,value)
            self.assertFalse(self.check()['success'])
            self.write(path,originals[path])

    def test_explicit_catalog_format_rejects_dynamic_keys_and_missing_arguments(self):
        for source in ['TextResources.FormatForCatalog(catalog, key, 3);',
                       'TextResources.FormatForCatalog(catalog, "ui.new");']:
            _, errors = audit.check_references(source, {'ui.new':'New {0}'})
            self.assertTrue(errors, source)

    def test_language_audit_checks_new_format_contracts(self):
        import verify_language_packs as language
        self.write('assets/locales/hi_IN.json',{'ui.old':'Original hi_IN','ui.new':'New {1} hi_IN'})
        previous=language.ROOT
        try:
            language.ROOT=self.root
            self.assertFalse(language.audit(self.root/'assets/locales')['success'])
        finally:
            language.ROOT=previous

    def test_duplicate_evidence_fields_are_rejected(self):
        text=json.dumps(self.provenance)
        text=text.replace('"resourceCommit": "'+self.source+'"','"resourceCommit": "'+self.source+'", "resourceCommit": "'+self.source+'"',1)
        self.write('docs/localization/feature-resource-provenance.json',text)
        self.assertFalse(self.check()['success'])

    def test_mutually_reanchored_fixtures_cannot_redefine_original_values(self):
        baseline=json.loads((self.root/'tests/fixtures/localization/text-baseline.json').read_text())
        baseline['resources'].append({'key':'ui.new','expected':'New {0} zh_CN','format':True,'argument_count':1})
        self.write('tests/fixtures/localization/text-baseline.json',baseline)
        inventory=(self.root/'docs/localization/migration-inventory.jsonl').read_text()
        inventory+=json.dumps({'file':'src/App.cs','literal_hash':audit.digest('New {0} zh_CN'),'value':'New {0} zh_CN','decision':'migrate','resource_key':'ui.new','reviewed':True})+'\n'
        self.write('docs/localization/migration-inventory.jsonl',inventory)
        self.write('tests/fixtures/localization/feature-text-baseline.json',{'schemaVersion':1,'resources':[]})
        fake_base=self.commit('a feature commit must not become the historical base')
        protected={'schemaVersion':1,'baseCommit':fake_base,'catalogs':{}}
        for locale in self.locales:
            current=json.loads((self.root/('assets/locales/'+locale+'.json')).read_text())
            protected['catalogs'][locale]={'keyCount':2,'valuesSha256':hashlib.sha256(encoded(current)).hexdigest()}
        self.write('tests/fixtures/localization/protected-originals.json',protected)
        self.provenance.update(protectedBaseCommit=fake_base,resourceCommit=fake_base,resources=[])
        self.write('docs/localization/feature-resource-provenance.json',self.provenance)
        self.assertFalse(self.check()['success'],'the fixed original commit must be independent of editable evidence files')

    def test_unknown_feature_contract_fields_are_rejected(self):
        path=self.root/'tests/fixtures/localization/feature-text-baseline.json'
        value=json.loads(path.read_text())
        value['allowPrefix']='ui'
        self.write('tests/fixtures/localization/feature-text-baseline.json',value)
        self.assertFalse(self.check()['success'])


if __name__ == '__main__':
    unittest.main()
