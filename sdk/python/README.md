# simplyworks-bitween

The Bitween adapter contract for Python: the kinds of adapter Bitween runs and what it passes them.
It is the Python form of `SW.Bitween.Adapters/Contract/bitween-adapter-contract.v1.json`, as
`SimplyWorks.Bitween.Adapters` is its .NET one.

| Kind | Implement | Bitween calls |
|---|---|---|
| `Handler` | `handle(file) -> ExchangeFile` | `Handle` |
| `Mapper` | `map(file) -> ExchangeFile` | `Handle` |
| `Validator` | `validate(file) -> ValidationResult` | `Validate` |
| `Receiver` | `list_files()`, `get_file(id)`, `delete_file(id)`; optionally `initialize()`, `finalize()` | `Initialize`, `ListFiles`, `GetFile`, `DeleteFile`, `Finalize` |

```python
import simplyworks_serverless as sw
from simplyworks_bitween import ExchangeFile, Handler


class Orders(Handler):
    def __init__(self):
        sw.expect("Url", description="Where orders go")

    def handle(self, file: ExchangeFile) -> ExchangeFile:
        # A partner's rejection is returned with bad_data=True, not raised.
        return ExchangeFile(data=file.data, filename=file.filename)


if __name__ == "__main__":
    sw.run(Orders)
```

`ExchangeFile` has `data`, `filename`, `bad_data` and `content_type`, and is sent with the contract's
property names (`Data`, `Filename`, `BadData`, `ContentType`, `Hash`). A kind missing a method it
needs fails when the adapter starts.

The SW-Serverless CLI vendors a copy of this package into every Python adapter it builds; keep
`SW.Serverless.Tooling/Contracts/bitween/python` in SW-Serverless the same as `src/` here.

Tests: `PYTHONPATH=src:<SW-Serverless>/sdk/python/src python -m unittest discover -s tests`.
