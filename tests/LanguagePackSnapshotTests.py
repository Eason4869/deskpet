import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import verify_language_packs as language_packs


class SnapshotTests(unittest.TestCase):
    def test_shipped_byte_snapshot_detects_even_whitespace_drift(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            codes=['zh_CN','zh_TW','en_US','ja_JP','ko_KR','fr_FR','de_DE','hi_IN','id_ID','it_IT','pt_BR','es_419','es_ES']
            hashes=[]
            for code in codes:
                data=json.dumps({'message':code}).encode('utf-8')
                (root/(code+'.json')).write_bytes(data)
                hashes.append(hashlib.sha256(data).hexdigest())
            source=root/'snapshot.cs'
            source.write_text('static readonly string[] Codes={'+','.join(json.dumps(x) for x in codes)+'};\nstatic readonly string[] Hashes={'+','.join(json.dumps(x) for x in hashes)+'};',encoding='utf-8')
            self.assertEqual([], language_packs.snapshot_errors(root,source))
            with (root/'zh_CN.json').open('ab') as stream:stream.write(b'\n')
            self.assertTrue(any('zh_CN' in error for error in language_packs.snapshot_errors(root,source)))
            source.write_text(source.read_text(encoding='utf-8').replace(json.dumps(hashes[0]),'null'),encoding='utf-8')
            self.assertTrue(any('thirteen independent' in error for error in language_packs.snapshot_errors(root,source)))


if __name__ == '__main__':
    unittest.main()
