# Several tools in one call

## <code>run_<wbr>tools</code>

Runs several of this server's tools in one call, in order, stopping at the first failure. For steps that do not depend on reading the previous answer. Each step is an ordinary tool call under the same grant; run_tools is not a step.

*may overwrite or delete · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `steps` | `array` | yes | — | Steps in order: {"tool": "read_file", "params": {"path": "src/App.cs"}}. Call find_tool for what a tool takes. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "steps": {
      "description": "Steps in order: {\u0022tool\u0022: \u0022read_file\u0022, \u0022params\u0022: {\u0022path\u0022: \u0022src/App.cs\u0022}}. Call find_tool for what a tool takes.",
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "tool": {
            "type": [
              "string",
              "null"
            ]
          },
          "params": {
            "type": [
              "object",
              "null"
            ]
          }
        },
        "required": [
          "tool",
          "params"
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
      "description": "Whether every step ran and went through.",
      "type": "boolean"
    },
    "ran": {
      "description": "How many steps ran.",
      "type": "integer"
    },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "tool": {
            "description": "The tool the step called.",
            "type": "string"
          },
          "ok": {
            "description": "Whether the step went through.",
            "type": "boolean"
          },
          "data": {
            "default": null
          },
          "error": {
            "description": "The tool\u0027s refusal; absent when it went through.",
            "type": [
              "string",
              "null"
            ],
            "default": null
          },
          "isOmitted": {
            "description": "Whether the answer was left out to stay inside the output budget; absent when it was not.",
            "type": "boolean",
            "default": false
          }
        },
        "required": [
          "tool",
          "ok"
        ]
      }
    },
    "omitted": {
      "description": "How many answers were left out to stay inside the output budget.",
      "type": "integer"
    },
    "note": {
      "description": "Why answers were left out; null when none were.",
      "type": [
        "string",
        "null"
      ]
    },
    "error": {
      "description": "Why the run stopped; null when every step went through.",
      "type": [
        "string",
        "null"
      ]
    },
    "hint": {
      "description": "What to do about it; null when there is nothing to do.",
      "type": [
        "string",
        "null"
      ]
    }
  },
  "required": [
    "ok",
    "ran",
    "steps",
    "omitted",
    "note",
    "error",
    "hint"
  ]
}
```

</details>

