# Files of the working copy

## <code>read_<wbr>file</code>

Reads a file as text, whole or by lines. Says how many lines it has and whether the answer was cut.

*read-only · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string` | yes | — | Path of the file, absolute or relative to the session's root. |
| `from` | `integer?` | — | `null` | First line to return, counting from one; omit for the start of the file. |
| `to` | `integer?` | — | `null` | Last line to return; omit for the end of the file. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "Path of the file, absolute or relative to the session\u0027s root.",
      "type": "string"
    },
    "from": {
      "description": "First line to return, counting from one; omit for the start of the file.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    },
    "to": {
      "description": "Last line to return; omit for the end of the file.",
      "type": [
        "integer",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "path"
  ]
}
```

</details>

## <code>write_<wbr>file</code>

Writes a file whole, creating the folders above it. Overwrites without asking: read it first, or use edit_file to change a part.

*may overwrite or delete · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string` | yes | — | Path of the file, absolute or relative to the session's root. |
| `text` | `string` | yes | — | The whole new contents of the file. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "Path of the file, absolute or relative to the session\u0027s root.",
      "type": "string"
    },
    "text": {
      "description": "The whole new contents of the file.",
      "type": "string"
    }
  },
  "required": [
    "path",
    "text"
  ]
}
```

</details>

## <code>edit_<wbr>file</code>

Replaces exact text in a file. Refuses when the text is not there, and when it is there more than once without 'all' — a replacement the caller did not mean is worse than no replacement.

*may overwrite or delete · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string` | yes | — | Path of the file, absolute or relative to the session's root. |
| `find` | `string` | yes | — | The exact text to replace, including its indentation and line ends. |
| `replace` | `string` | yes | — | What to put in its place; empty removes it. |
| `all` | `boolean` | — | `false` | Replace every occurrence rather than refusing when there is more than one. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "Path of the file, absolute or relative to the session\u0027s root.",
      "type": "string"
    },
    "find": {
      "description": "The exact text to replace, including its indentation and line ends.",
      "type": "string"
    },
    "replace": {
      "description": "What to put in its place; empty removes it.",
      "type": "string"
    },
    "all": {
      "description": "Replace every occurrence rather than refusing when there is more than one.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "path",
    "find",
    "replace"
  ]
}
```

</details>

## <code>list_<wbr>directory</code>

Lists what is under a folder, to the depth asked for. Build output and .git are never walked into.

*read-only · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string?` | — | `null` | Folder to work in; omit for the session's root. |
| `depth` | `integer` | — | `2` | How many folders deep to go; two by default, eight at most. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "Folder to work in; omit for the session\u0027s root.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "depth": {
      "description": "How many folders deep to go; two by default, eight at most.",
      "type": "integer",
      "default": 2
    }
  }
}
```

</details>

## <code>find_<wbr>files</code>

Names the files whose path matches a glob: *.cs, **/Tests/*.cs, appsettings.*.json.

*read-only · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `pattern` | `string` | yes | — | The glob to match. '*' stays inside one folder, '**' crosses them. |
| `path` | `string?` | — | `null` | Folder to work in; omit for the session's root. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "pattern": {
      "description": "The glob to match. \u0027*\u0027 stays inside one folder, \u0027**\u0027 crosses them.",
      "type": "string"
    },
    "path": {
      "description": "Folder to work in; omit for the session\u0027s root.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "pattern"
  ]
}
```

</details>

## <code>search_<wbr>text</code>

Names the lines that contain a text, with the file and the line number of each. Exact text, not a pattern.

*read-only · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `query` | `string` | yes | — | The text to look for, matched exactly. |
| `path` | `string?` | — | `null` | Folder to work in; omit for the session's root. |
| `pattern` | `string?` | — | `null` | Only search files whose path matches this glob. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "query": {
      "description": "The text to look for, matched exactly.",
      "type": "string"
    },
    "path": {
      "description": "Folder to work in; omit for the session\u0027s root.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "pattern": {
      "description": "Only search files whose path matches this glob.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "query"
  ]
}
```

</details>

