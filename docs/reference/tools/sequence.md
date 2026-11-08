# Several tools in one call

## <code>run_<wbr>tools</code>

Runs tools of this server in order in one call, stopping at the first failure; no step sees an earlier answer. An answer past the budget is flagged isOmitted: call that tool alone.

*may overwrite or delete · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `steps` | `array` | yes | — | The steps, in order. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "steps": {
      "description": "The steps, in order.",
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "tool": {
            "description": "The tool to call.",
            "type": [
              "string",
              "null"
            ]
          },
          "arguments": {
            "description": "The arguments of a direct call.",
            "type": [
              "object",
              "null"
            ],
            "default": null
          }
        },
        "required": [
          "tool"
        ]
      }
    }
  },
  "required": [
    "steps"
  ]
}
```

</details>

<details><summary>Output schema</summary>

```json
{
  "type": "object",
  "properties": {
    "ok": {
      "type": "boolean",
      "const": true
    },
    "data": {
      "type": "object",
      "properties": {
        "ran": {
          "type": "integer"
        },
        "steps": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "tool": {
                "type": "string"
              },
              "ok": {
                "type": "boolean"
              },
              "data": {},
              "isOmitted": {
                "type": "boolean"
              },
              "error": {
                "type": "string"
              },
              "hint": {
                "type": "string"
              },
              "type": {
                "type": "string"
              }
            },
            "required": [
              "tool",
              "ok"
            ]
          }
        },
        "omitted": {
          "type": "integer"
        },
        "note": {
          "type": "string"
        }
      },
      "required": [
        "ran",
        "steps"
      ]
    }
  },
  "required": [
    "ok",
    "data"
  ]
}
```

</details>

