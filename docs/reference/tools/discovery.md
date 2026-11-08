# Finding a tool

## <code>find_<wbr>tool</code>

Finds tools of this server for a task in words, best first, with the parameters of each. Ask it before deciding that no tool exists.

*read-only · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `task` | `string?` | — | `null` | What you are trying to do, in words. |
| `limit` | `integer` | — | `5` | How many tools to name, 1 to 20. |
| `intent` | `string?` | — | `null` | Same as task. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "task": {
      "description": "What you are trying to do, in words.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "limit": {
      "description": "How many tools to name, 1 to 20.",
      "type": "integer",
      "default": 5
    },
    "intent": {
      "description": "Same as task.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  }
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
        "task": {
          "type": [
            "string",
            "null"
          ]
        },
        "isMatched": {
          "type": "boolean"
        },
        "note": {
          "type": "string"
        },
        "tools": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "name": {
                "type": "string"
              },
              "command": {
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
                      "type": "string"
                    },
                    "isRequired": {
                      "type": "boolean"
                    },
                    "description": {
                      "type": "string"
                    },
                    "fields": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "required": [
                          "name",
                          "type",
                          "isRequired"
                        ]
                      }
                    }
                  },
                  "required": [
                    "name",
                    "type",
                    "isRequired"
                  ]
                }
              },
              "skill": {
                "type": "string"
              },
              "isLoaded": {
                "type": "boolean"
              }
            },
            "required": [
              "name",
              "description"
            ]
          }
        },
        "enabledSkills": {
          "type": "array",
          "items": {
            "type": "string"
          }
        }
      },
      "required": [
        "task",
        "isMatched",
        "tools"
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

