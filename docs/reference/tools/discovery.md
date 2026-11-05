# Finding a tool

## <code>find_<wbr>tool</code>

Names the tools that answer a task described in words, with the parameters each takes.

*read-only · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `task` | `string` | yes | — | What you are trying to do, in words: 'change one line', 'where is this text'. |
| `limit` | `integer` | — | `5` | How many tools to name; five by default. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "task": {
      "description": "What you are trying to do, in words: \u0027change one line\u0027, \u0027where is this text\u0027.",
      "type": "string"
    },
    "limit": {
      "description": "How many tools to name; five by default.",
      "type": "integer",
      "default": 5
    }
  },
  "required": [
    "task"
  ]
}
```

</details>

<details><summary>Output schema</summary>

```json
{
  "type": "object",
  "properties": {
    "task": {
      "description": "The task as asked.",
      "type": [
        "string",
        "null"
      ]
    },
    "isMatched": {
      "description": "Whether any tool matched; when none did, every tool is listed.",
      "type": "boolean"
    },
    "note": {
      "description": "Why every tool is listed; null when some matched.",
      "type": [
        "string",
        "null"
      ]
    },
    "tools": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "name": {
            "type": "string"
          },
          "description": {
            "type": "string"
          },
          "parameters": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string"
                },
                "type": {
                  "description": "The JSON type of the value.",
                  "type": "string"
                },
                "isRequired": {
                  "description": "Whether the call must give it.",
                  "type": "boolean"
                },
                "description": {
                  "type": [
                    "string",
                    "null"
                  ]
                }
              },
              "required": [
                "name",
                "type",
                "isRequired",
                "description"
              ]
            }
          }
        },
        "required": [
          "name",
          "description",
          "parameters"
        ]
      }
    }
  },
  "required": [
    "task",
    "isMatched",
    "note",
    "tools"
  ]
}
```

</details>

