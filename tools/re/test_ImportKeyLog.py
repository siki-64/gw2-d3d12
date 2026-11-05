import unittest

from ImportKeyLog import parse_key_log, to_document


class ImportKeyLogTests(unittest.TestCase):
    def test_parses_dedupes_and_flags(self):
        lines = [
            "noise",
            "KEY\ttext\t1A2B\t123456789A",
            "KEY\ttext\t1A2B\t123456789A",
            "KEY\ttext\t0005\t0",
            "KEY\ttext\t0006\tFFFF000000000001",
            "KEY\ttext\t0007\t11",
            "KEY\ttext\t0007\t22",
        ]
        keys, conflicts, rejected = parse_key_log(lines)
        self.assertEqual({0x1A2B: 0x123456789A, 0x7: 0x11}, keys["text"])
        self.assertEqual(1, len(conflicts))
        self.assertEqual(1, len(rejected))
        document = to_document("1", keys, conflicts)
        self.assertEqual("000000123456789A", document["keys"]["text"]["6699"])


if __name__ == "__main__":
    unittest.main()
