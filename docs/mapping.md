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

The rules are read once per rule set and kept, and the source document is read from the parse the exchange already made of it when there is one. Neither changes what a mapping produces: numbers are still read exactly and dates still as text. See `SharedPayloadParse` and `CacheCompiledTemplates` in [Configuration](configuration.md#bitween-section).

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

### CSV

`sourceFormat` and `targetFormat` can be `csv`, for delimited text, with `sourceCsv` and `targetCsv` saying
how each side is delimited, since a partner's semicolon file often becomes someone else's comma file:

```json
{ "version": 1, "sourceFormat": "csv", "targetFormat": "json",
  "sourceCsv": { "delimiter": ";", "hasHeader": true },
  "root": { "over": "", "fields": [ { "target": ["sku"], "from": { "kind": "path", "path": "SKU" } } ] } }
```

| Option | Default | |
|---|---|---|
| `delimiter` | `,` | Any characters: `;`, `\|`, `\t` for tab |
| `hasHeader` | `true` | Whether the first line names the columns. Without a header, columns are named by position: `1`, `2`, `3`. |
| `byteOrderMark` | `false` | When writing, start the file with a UTF-8 byte-order mark, so Excel opens accented names correctly |

When reading CSV, the document is a list of rows, so a list rule walks it with `"over": ""`, and `root`
makes the whole output a list, one entry per row. Every field
is text: nothing turns `041800` into a number, and a rule's own `type` does when one is wanted. A row
wider than the header gets its extra fields named by position rather than dropped. Blank rows are skipped,
and a file that is only a header is a list of no rows.

When writing CSV, the output is a list of rows, or one object for a single row. Every column any row has is
written, in the order they first appear, and a row without one gets an empty field there. A nested field
becomes a dotted column name, as `destination.city`. A list inside a row is refused rather than folded into
one cell.

The information type of a CSV document is carried, not read: it has no promoted properties, and only
subscriptions and routes without a filter pick it up. Its mapping reads it.

### Fixed-width text

`fixedwidth` reads and writes one record per line, each field at a set position. Nothing in such a file
says where a field ends, so `sourceFixedWidth` and `targetFixedWidth` give the layout, as the partner's
specification does:

```json
"sourceFixedWidth": {
  "columns": [
    { "name": "type", "start": 1, "length": 1 },
    { "name": "reference", "start": 2, "length": 8 },
    { "name": "amount", "start": 10, "length": 7, "align": "right", "pad": "0" }
  ],
  "skipLines": 1,
  "trim": true
}
```

| Option | Default | |
|---|---|---|
| `columns` | | Each column's `name`, its `start` (the line's first character is 1) and its `length`. Columns can't overlap. |
| `align`, `pad` | `left`, a space | When writing: which side a value keeps to, and what fills the rest. Amounts are usually `right` with `0`. |
| `skipLines` | `0` | When reading: lines at the top that aren't records, such as a title |
| `trim` | `true` | When reading: take the padding off each value |

Read, the document is a list of rows, as CSV's is. A line shorter than the layout is read as far as it
goes, and blank lines are skipped. When writing, each row has to be an object of the layout's columns
(a nested field becomes a dotted name, as `destination.city`): a field the layout has no column for is
refused, and **a value longer than its column is refused rather than cut**, because a cut amount or
reference would be booked without anyone noticing.

### Excel

`excel` reads and writes Excel workbooks (.xlsx), one sheet at a time. A workbook is binary and a message
in Bitween is text, so it travels as base64: give the receiver that picks it up (S3, Azure Blob, SFTP, FTP)
`ResponseEncoding` `base64`, and the upload handler that writes one `DataEncoding` `base64`.

| Option | Default | |
|---|---|---|
| `sheet` | first sheet, or `Sheet1` | `sourceExcel`: the sheet to read, by name. `targetExcel`: the name of the one written. |
| `hasHeader` | `true` | Whether the first row names the columns. Without it, columns are named by position: `1`, `2`, `3`. |

Read, the sheet is a list of rows, as CSV's is, and every cell is text: a reference keeps its leading
zeros, numbers are written the invariant way (`12.5`), cells formatted as dates become ISO 8601
(`2026-10-12`, or with a time), booleans are `true` or `false`, and a formula is the value Excel last
calculated. Empty rows are skipped. The older .xls format isn't read; save it as .xlsx.

Written, the workbook has one sheet, with a header row when `hasHeader` is on. Text is written as text,
so leading zeros survive, and numbers and booleans as numbers and booleans.

In the editor, the sample of an Excel source is a few rows copied from Excel and pasted, which arrive as
tab-separated text; the preview reads a pasted sample the same way, and shows an Excel output as its rows,
tab-separated, rather than as base64. `bitween mapping test` sends an `.xlsx` input file as base64.

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

The toolbar's format chip, as **JSON → XML**, opens what the mapping reads and writes: each side's format, and its settings for [CSV](#csv) (separator, header, the byte-order mark Excel wants), [fixed-width text](#fixed-width-text) (a table of columns, lines to skip, trimming, and on the written side each column's alignment and padding) and [Excel](#excel) (the sheet and the header).

## Legacy JSON mapper

`NativeJSONMapper` renders a [Scriban](https://github.com/scriban/scriban) template over the JSON payload and stores the result as JSON.

The template is compiled once per text and kept, and the payload is read from the parse the exchange already made of it when nothing was written into it. A template renders exactly as before, including `__partner__` and `__globals__`.

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
