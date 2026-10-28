import unittest


class ExampleTests(unittest.TestCase):
    def test_sum(self):
        self.assertEqual(sum([2, 3, 5]), 10)

    def test_unicode(self):
        self.assertEqual("zażółć".encode("utf-8").decode("utf-8"), "zażółć")
