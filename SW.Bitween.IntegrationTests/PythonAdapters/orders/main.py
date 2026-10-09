"""A Bitween handler in Python: answers an order with its settings, or rejects it as bad data."""
import json

import sw_serverless as sw
from simplyworks_bitween import ExchangeFile, Handler


class Orders(Handler):
    def __init__(self):
        sw.expect("Partner", description="Who receives the orders")
        sw.expect("Token", secret=True, description="The partner's key")

    def handle(self, file: ExchangeFile) -> ExchangeFile:
        order = json.loads(file.data)
        if order.get("reject"):
            return ExchangeFile(data='{"error":"rejected"}', bad_data=True)
        answer = {"to": sw.value_of("Partner"), "token": len(sw.value_of("Token") or ""), "order": order}
        return ExchangeFile(data=json.dumps(answer, sort_keys=True), filename="answer.json")


if __name__ == "__main__":
    sw.run(Orders)
