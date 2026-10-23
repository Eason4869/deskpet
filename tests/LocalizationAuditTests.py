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


if __name__ == "__main__": unittest.main()
