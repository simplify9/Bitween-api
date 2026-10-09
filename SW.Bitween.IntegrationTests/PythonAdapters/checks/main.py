"""A Bitween validator in Python: an order needs an id."""
import json

import simplyworks_serverless as sw
from simplyworks_bitween import ExchangeFile, ValidationResult, Validator


class Checks(Validator):
    def validate(self, file: ExchangeFile) -> ValidationResult:
        result = ValidationResult()
        if "orderId" not in json.loads(file.data):
            result.add("orderId", "An order needs an id.")
        return result


if __name__ == "__main__":
    sw.run(Checks)
