"""Regression: a future translation must fail the real corpus freshness validator."""
import copy
import unittest
from unittest.mock import patch
import relationship_title_fonts as fonts

class CorpusFreshnessTests(unittest.TestCase):
    def test_new_translation_requires_subset_regeneration(self):
        changed=copy.deepcopy(fonts.corpus())
        changed['ja_JP']['relationship.stage.firstMeeting']='初めまして'
        with patch.object(fonts,'corpus',return_value=changed):
            with self.assertRaisesRegex(AssertionError,'Title translations changed: regenerate'):
                fonts.check()

if __name__=='__main__': unittest.main()
