# Mapping

A mapper turns what arrived into what the destination expects. Bitween has two built-in mappers, each with its own visual editor.

| | Rules-based mapper | Legacy JSON mapper |
|---|---|---|
| Id | `NativeMapper` | `NativeJSONMapper` |
| Formats | JSON and XML, in either direction | JSON to JSON |
| Stored as | Mapping rules in JSON | A Scriban template |
| Partner and global values | Passed as separate context | Injected into the payload as `__partner__` and `__globals__` |
| Status | Current | Retiring |

Open either editor from a subscription's Transformation stage with **Open the visual mapping editor**. Its address is `/subscriptions/{id}/mapper`.

## Rules-based mapper

### How it runs

1. The source format reads the payload into a tree.
2. The rules build a new tree.
3. The target format writes the tree, with content type `application/json` or `application/xml`.

Every rule is attempted. If any rule fails, the exchange fails with a message listing each failing target and the reason, instead of producing a document with gaps.

### Stored properties

| Property | Content |
|---|---|
| `MappingRules` | The rules as JSON. Required at run time. |
| `SourceSample`, `TargetSample` | Samples kept for the editor. Never read while mapping. |

### Rules format

```json
{
  "version": 1,
  "sourceFormat": "json",
  "targetFormat": "json",
  "sourceDateOrder": "dayFirst",
  "fields": [
    { "target": ["reference"], "from": { "kind": "path", "path": "order.id" } },
    { "target": ["customer", "name"], "from": { "kind": "path", "path": "order.customer" },
      "transform": { "fn": "upper" } },
    { "target": ["account"], "from": { "kind": "partner", "key": "accountNumber" } },
    { "target": ["region"], "from": { "kind": "global", "setId": "regions", "key": "default" } },
    { "target": ["status"], "from": { "kind": "path", "path": "order.status" },
      "lookup": { "table": { "N": "New", "S": "Shipped" }, "fallback": "Unknown" } },
    { "target": ["orderedOn"], "from": { "kind": "path", "path": "order.date" },
      "transform": { "fn": "formatDate", "format": "yyyy-MM-dd" } }
  ],
  "lists": [
    {
      "over": "order.lines",
      "target": ["items"],
      "where": { "field": "qty", "operator": "greaterThan", "value": 0 },
      "fields": [
        { "target": ["sku"], "from": { "kind": "path", "path": "sku" } },
        { "target": ["quantity"], "from": { "kind": "path", "path": "qty" }, "type": "number" },
        { "target": ["orderRef"], "from": { "kind": "rootPath", "path": "order.id" } }
      ]
    }
  ]
}
```

Rules with a `version` above 1 are refused.

### Field rules

A field rule writes one value.

`target` is a list of path segments, so a key containing a dot can still be written. Missing objects along the path are created.

`from` says where the value comes from.

| `kind` | Value |
|---|---|
| `path` | A dot path read from the current scope, which is the document or the current list item |
| `rootPath` | A dot path read from the document root, even inside a list |
| `fixed` | The literal in `value`, keeping its JSON type |
| `partner` | The partner property named `key` |
| `global` | The value `key` in the global value set `setId` |
| `source` | A `path` into the original document: the input of the exchange whose delivery got this response. Read like any path. Only a response or bus gateway subscription has these; the editor calls them **Original**. |

A missing path, partner property, global value or source value gives null rather than an error. Paths match keys exactly and never step into a list. Use a list rule to reach list items.

The value then passes through these optional steps, in order.

1. `transform` applies one function.
2. `lookup` replaces the value from `table`, comparing as text. A value with no entry becomes `fallback`, which defaults to null.
3. `type` converts the value to `string`, `number` or `boolean`.

### Transforms

| `fn` | Arguments | Result |
|---|---|---|
| `upper`, `lower`, `trim` | | |
| `substring` | `start`, optional `length` | |
| `replace` | `find`, `with` | |
| `concat` | `with` | Appends text |
| `round` | `decimals`, 0 to 15 | Rounds halves away from zero |
| `multiply` | `by` | |
| `add` | `amount` | |
| `formatDate` | `format` | Parses the value as a date and formats it |
| `defaultIfEmpty` | `value` | Replaces null or empty text |

Null passes through every transform except `defaultIfEmpty`.

### Numbers, booleans and dates

- Numbers are decimals throughout, so `100 × 1.16` is exactly `116`. Whole numbers are written without a decimal point, and scientific notation is never used.
- Conversion to boolean accepts `true`, `1`, `yes`, `y`, `false`, `0`, `no`, `n` and the numbers 0 and 1. Anything else fails the rule.
- `formatDate` always accepts ISO-style dates such as `2026-09-10`, `2026-09-10T08:30:00Z` and `20260910`. Day-first dates such as `10/09/2026` are accepted only when `sourceDateOrder` is `dayFirst`, and month-first dates only when it is `monthFirst`. Ambiguous dates are refused, never guessed.

### List rules

A list rule writes an array.

| Property | Meaning |
|---|---|
| `over` | What to walk. A path walks that list. An empty string walks the document itself. Leaving it out walks nothing, so the list holds only its fixed entries. |
| `target` | Where the array is written. |
| `where` | Optional filter with `field`, `operator` and `value`. `equal` and `notEqual` compare as text. `greaterThan`, `greaterThanOrEqual`, `lessThan` and `lessThanOrEqual` compare as numbers. Items without the field are skipped. |
| `fields`, `lists` | Rules applied to each item, with paths relative to the item. Lists can nest. |
| `item` | One field rule that makes each entry a plain value rather than an object. |
| `fixed` | Entries placed before the walked items, each with its own `item`, `fields` or `lists`, read from the enclosing scope. |

A top-level `root` list rule makes the whole output an array. It cannot be combined with top-level `fields` or `lists`.

### XML

When reading XML:

- The root element's name becomes the top-level key.
- Attributes become `@name` keys, and mixed text becomes `#text`.
- Repeated elements become lists. A list path that finds a single element walks it as a list of one.
- Namespaces are dropped and only local names are kept.
- DTDs and nesting deeper than 64 levels are refused.

When writing XML:

- The output needs exactly one top-level key, and it cannot be a list.
- A list directly inside another list is refused.
- Fields named `@xmlns` or `@xmlns:prefix` become namespace declarations. A prefix without a declaration is refused.

### Preview

`POST /api/mappingpreviews` runs rules against a sample through the same code as the pipeline, without saving anything.

```json
{ "mappingRules": "{ ... }", "sourceDocument": "{ ... }", "partnerId": 7 }
```

The reply holds `outputDocument`, `contentType`, `ruleErrors` with a `target` and `reason` each, and `error` for problems that stop mapping altogether.

### The editor

The rules editor has three panels.

- **Source.** Paste a sample, choose the date order, then click or drag a path onto a rule.
- **Output.** A tree of target fields and lists. Each row picks a source kind and opens the transform, lookup and type steps. Lists have filters and fixed entries. A new list gets its content from **+ Value**, **+ Field** or **+ List**. Choosing + Value makes a list of plain values, whose value is a row of its own.
- **Preview.** The server's output for the current rules, with failing rows marked.

**Build from a sample** reads a target sample and adds rules for fields not yet mapped, filling in sources it can recognise. **Preview as** chooses a partner for resolving partner values. Save with Ctrl+S or Cmd+S.

## Legacy JSON mapper

`NativeJSONMapper` renders a [Scriban](https://github.com/scriban/scriban) template over the JSON payload and stores the result as JSON.

### Retirement

The legacy mapper stays in the adapter picker only while at least one subscription uses it. Once the last subscription moves to the rules-based mapper, the option disappears for good. Existing subscriptions keep running and keep their editor.

### Template semantics

- The only property is `ScribanTemplate`, defaulting to `{}`.
- The payload must be a JSON object or array. Each key can also be reached with its first letter in lower case, so `CustomerId` and `customerId` both work.
- Member access on an array reads its first element. A root array is available as `items`, and its first element's keys are also available at the top level.
- `| json` writes any value as JSON, quoting strings. `| to_float` converts a number or numeric text.
- Unknown variables render as empty.
- After rendering, trailing commas are removed and the text must parse as JSON.
- Keys containing dots are expanded into nested objects, so `"a.b": 1` becomes `{"a": {"b": 1}}`.
- Partner and global values are available as `__partner__` and `__globals__`.

```scriban
{
  "reference": {{ order.id | json }},
  "customer": {{ order.customer | json }},
  "account": {{ __partner__?.accountNumber | json }},
  "region": {{ __globals__?.regions["default"] | json }},
  "total": {{ order.total | to_float | json }}
}
```

### The editor

The legacy editor draws connections from a source sample tree to an output tree. Each output field takes a source path, a fixed value, a partner property or a global value, with an optional lookup table. Arrays open a dialog for the source array, a filter and per-item fields. A manual mode edits the Scriban text directly, and switching modes converts between the two. Live preview calls `POST /api/mappers`.

The editor saves `ScribanTemplate` plus its own layout data in `ArrayRules`, `SourceJson`, `TargetJson` and `PartnerId`.

The preview endpoint injects partner and global values into each element of a root array, which the pipeline does not do. A preview of a root-array payload can therefore differ from a real run.
