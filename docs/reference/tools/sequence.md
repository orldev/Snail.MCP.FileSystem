# Several tools in one call

## <code>run_<wbr>tools</code>

Runs several of this server's tools in one call, in order, stopping at the first failure. For steps that do not depend on reading the previous answer. Each step is an ordinary tool call.

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

