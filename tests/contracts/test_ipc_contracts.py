"""B-02B wire fixtures, independent JSON Schema validator; no process or Siemens IO."""
import copy
import json
from pathlib import Path
import unittest
from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]


class IpcContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        schema = json.loads((ROOT / 'contracts/ipc/v1/message.schema.json').read_text(encoding='utf-8'))
        Draft202012Validator.check_schema(schema)
        cls.validator = Draft202012Validator(schema, format_checker=FormatChecker())
        cls.examples = [json.loads(p.read_text(encoding='utf-8')) for p in sorted((ROOT / 'examples/ipc/v1').glob('*.json'))]

    def test_every_message_kind_has_a_valid_synthetic_example(self):
        self.assertEqual(9, len({item['kind'] for item in self.examples}))
        for item in self.examples:
            with self.subTest(kind=item['kind']):
                self.validator.validate(item)

    def test_unknown_missing_types_versions_and_identity_are_rejected(self):
        for example in self.examples:
            mutations = [dict(example, protocol_version='2.0'), dict(example, worker_epoch='invalid'), dict(example, sequence=0), dict(example, required_feature='unknown'), dict(example, fence=-1)]
            for name in example:
                missing = copy.deepcopy(example)
                del missing[name]
                mutations.append(missing)
            extra = copy.deepcopy(example)
            extra['body']['unknown_required_field'] = True
            mutations.append(extra)
            for item in mutations:
                with self.subTest(kind=example['kind'], item=item):
                    self.assertTrue(list(self.validator.iter_errors(item)))

    def test_artifact_paths_and_unbounded_payloads_are_not_wire_references(self):
        dispatch = next(item for item in self.examples if item['kind'] == 'Dispatch')
        for artifact in ['../outside', 'C:\\foreign', 'artifact-' + '1' * 31]:
            bad = copy.deepcopy(dispatch)
            bad['body']['request']['artifact_id'] = artifact
            self.assertTrue(list(self.validator.iter_errors(bad)))
        receipt = copy.deepcopy(next(item for item in self.examples if item['kind'] == 'Receipt'))
        receipt['body']['receipt']['report'] = 'x' * 65537
        self.assertTrue(list(self.validator.iter_errors(receipt)))
