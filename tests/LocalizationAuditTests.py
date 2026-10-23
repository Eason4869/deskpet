"""Independent scanner/catalog negative cases; standard library only."""
import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class LocalizationAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location("verify_localization", ROOT / "tools/verify_localization.py")
        cls.audit = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.audit)

    def test_comments_escapes_and_verbatim(self):
        source = '// "注释"\nvar a="\\u4e2d文"; /* "注释" */ var b=@"引号""与\\路径";'
        self.assertEqual([t[2] for t in self.audit.string_tokens(source)], ["中文", '引号"与\\路径'])

    def test_interpolation_is_explicit(self):
        tokens = list(self.audit.string_tokens('var a=$"共 {count} 项";'))
        self.assertEqual(tokens[0][3], "interpolated")

    def test_duplicate_key_rejected(self):
        with self.assertRaises(ValueError):
            self.audit.load_catalog('{"x":"甲","x":"乙"}')

    def test_invalid_values_rejected(self):
        for value in ['[]', '{"x":3}', '{" ":"甲"}', '{"x":null}']:
            with self.assertRaises(ValueError): self.audit.load_catalog(value)

    def test_format_parameters(self):
        self.assertEqual(self.audit.placeholders("{{字面}} {0,8:N1} / {1} / {0}"), {0, 1})
        for value in ["{", "{name}", "{0", "}"]:
            with self.assertRaises(ValueError): self.audit.placeholders(value)

    def test_references_ignore_comments_and_read_multiline_provider_calls(self):
        source='// TextResources.Get("missing.key");\nvar x=TextResources.Format(\n CultureInfo.InvariantCulture, "files.count",\n Math.Max(1, 2));'
        counts, errors = self.audit.check_references(source, {'files.count':'共 {0} 项'})
        self.assertEqual(errors, [])
        self.assertEqual(dict(counts), {'files.count': 1})

    def test_format_call_argument_mismatch_is_rejected(self):
        for source in ['TextResources.Format("files.count");', 'TextResources.Format("files.count", nested(1, 2));']:
            _,errors = self.audit.check_references(source, {'files.count':'{0} / {1}'})
            self.assertTrue(errors)

    def test_commented_key_does_not_hide_missing_reference(self):
        _,errors=self.audit.check_references('TextResources.Get("files.missing"); /* TextResources.Get("files.ok") */',{'files.ok':'值'})
        self.assertEqual(len(errors),1)

    def test_exclusion_cannot_move_to_unrelated_context(self):
        self.assertNotEqual(self.audit.context_at('void Model(){Run("提示");}',17), self.audit.context_at('void UI(){Show("提示");}',15))


if __name__ == "__main__": unittest.main()
