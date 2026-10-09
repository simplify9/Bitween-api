import json
import os
import unittest

import simplyworks_serverless as sw
from simplyworks_bitween import ExchangeFile, Handler, Mapper, Receiver, ValidationResult, Validator

CONTRACT = os.path.join(os.path.dirname(__file__), "..", "..", "..", "SW.Bitween.Adapters", "Contract")


def contract():
    with open(os.path.join(CONTRACT, "bitween-adapter-contract.v1.json"), encoding="utf-8") as f:
        return json.load(f)


class Echo(Handler):
    def handle(self, file):
        return file


class Upper(Mapper):
    def map(self, file):
        return ExchangeFile(data=file.data.upper())


class Check(Validator):
    def validate(self, file):
        return {"Data": "empty"} if not file.data else None


class Inbox(Receiver):
    def list_files(self):
        return []

    def get_file(self, file_id):
        return ExchangeFile(data="")

    def delete_file(self, file_id):
        pass


class ContractTests(unittest.TestCase):
    def test_each_kind_exposes_exactly_the_contract_s_methods(self):
        kinds = contract()["kinds"]
        for cls in (Echo, Upper, Check, Inbox):
            described = sw.describe(cls)
            kind = described["kinds"][0]
            self.assertEqual({"bitween": 1}, described["contracts"])
            expected = [m["name"] for m in kinds[kind]["methods"]]
            self.assertEqual(sorted(expected), sorted(c["name"] for c in described["commands"]), kind)
            for method in kinds[kind]["methods"]:
                command = next(c for c in described["commands"] if c["name"] == method["name"])
                self.assertEqual(method["output"] is not None, command["returnsValue"], method["name"])
                self.assertEqual(method["input"] is None, command["inputSchema"] is None, method["name"])

    def test_exchange_files_carry_the_contract_s_property_names(self):
        schema_file = os.path.join(CONTRACT, "exchange-file.schema.json")
        with open(schema_file, encoding="utf-8") as f:
            names = set(json.load(f)["properties"])
        wire = ExchangeFile(data="{}", filename="a.json", bad_data=True, content_type="application/json").to_wire()
        self.assertEqual(names, set(wire))
        self.assertEqual("bf21a9e8fbc5a3846fb05b4fa0859e0917b2202f", wire["Hash"])  # SHA-1 of "{}"

    def test_the_contract_s_examples_read_and_write_back_the_same(self):
        for kind in contract()["kinds"].values():
            for method in kind["methods"]:
                for example in method.get("examples", []):
                    file = ExchangeFile.from_wire(example)
                    back = file.to_wire()
                    for key, value in example.items():
                        self.assertEqual(value, back[key])

    def test_a_validation_result_says_success_from_its_failures(self):
        self.assertEqual({"Success": True, "Validations": []}, ValidationResult().to_wire())
        failed = ValidationResult().add("orderId", "missing").to_wire()
        self.assertEqual({"Success": False, "Validations": [{"Key": "orderId", "Value": "missing"}]}, failed)

    def test_a_kind_without_its_methods_fails_when_it_starts(self):
        class Lazy(Receiver):
            def list_files(self):
                return []
        with self.assertRaises(TypeError) as raised:
            Lazy().__sw_check__()
        self.assertIn("get_file", str(raised.exception))
        self.assertIn("delete_file", str(raised.exception))
        Echo().__sw_check__()


if __name__ == "__main__":
    unittest.main()
