# Snail.MCP.FileSystem

A local MCP server that lets an AI agent read, write and search the files of **the working copy its
session names**, and nothing outside it.

Eight tools: `read_file`, `list_directory`, `find_files`, `search_text` read; `write_file` and
`edit_file` change; `find_tool` says which of them answers a task described in words, and `run_tools`
runs several of them in one call. There is no tool that deletes, moves or runs anything: this server
cannot destroy work it did not create.

## Why the roots matter

A filesystem server is one call away from every file its process can read. This one refuses every path
that is not inside one of its roots, comparing full paths segment by segment so that a sibling whose
name merely begins the same way is not mistaken for one inside, and **resolving every link along the
path** so that a symlink in the middle of it cannot lead out. A server given no root serves nothing
rather than serving the disk.

**The roots are per session, not per process.** A session opened at `…/mcp?root=/path/to/copy` is served
that path and nothing else, which is what lets one server stand behind several agents without any of
them reaching another's working copy; repeat `root` for more than one. `SNAIL_MCP_FILESYSTEM_ROOTS__0`
stays for sessions that name none — a server started as a program for one caller behaves as it always did.

## Two shapes, one server

**As a program.** Started with no port, it speaks stdio to whatever started it, belongs to that one
caller and is confined by `SNAIL_MCP_FILESYSTEM_ROOTS__0`. This is the shape a desktop client installs.

**At an address.** Given `SNAIL_MCP_FILESYSTEM_PORT`, it listens on every interface and serves sessions
at `/mcp`, each naming its own copy in the URL. One server then stands behind every agent on the
machine, and nothing in its image has to be repeated in theirs.

A session opened at a root **this machine does not have** is refused on the spot, with the root named:

```
400 this machine has no /workspaces/task-7: the session named a working copy that is not on the host
    this server runs on
```

That refusal is the point. Once servers answer at addresses, the worst thing that can happen is an
agent on one machine reaching the server of another — it would answer, about a folder that is not the
task's copy, and a wrong answer shaped like a right one is far worse than an error.

## Install

```bash
dotnet tool install --global Snail.MCP.FileSystem
```

## Configuration

Every setting is a field of the settings file or an environment variable prefixed `SNAIL_MCP_FILESYSTEM_`; a variable
wins over the file. The file is the one `SNAIL_MCP_FILESYSTEM_CONFIG` names, the only one read when it is set, or else
the first that exists of the chain every Snail MCP server reads ([Snail.Sdk](https://orldev.github.io/Snail.Sdk/)):
`.s3l-mcp-filesystem.json` or `s3l-mcp-filesystem.json` in the working directory,
`~/.config/s3l-mcp-filesystem/config.json`, `~/.s3l-mcp-filesystem.json`. A setting of this machine goes into `~/.config/s3l-mcp-filesystem/config.json`, readable by you alone
(`chmod 600`), never into a launchd job or a client's registration, which travel wherever they are copied. In the file
`SNAIL_MCP_FILESYSTEM_ROOTS__0` is the first item of `"Roots"` and `SNAIL_MCP_FILESYSTEM_MAX_MATCHES` is
`"MaxMatches"`; the fields may also be nested under `"Snail"`:

```json
{ "Roots": ["/path/to/your/copy"], "AllowWrite": false }
```

| Variable | Meaning |
|---|---|
| `SNAIL_MCP_FILESYSTEM_PORT` | Port to answer sessions on; unset means the server is started as a program and speaks stdio |
| `SNAIL_MCP_FILESYSTEM_ROOTS__0` | Absolute path the agent may work in when the session names none; add `ROOTS__1` for more |
| `SNAIL_MCP_FILESYSTEM_ALLOW_WRITE` | Whether `write_file` and `edit_file` do anything; `true` by default |
| `SNAIL_MCP_FILESYSTEM_OUTPUT_LIMIT_CHARS` | Characters of file text one call may return before it is cut |
| `SNAIL_MCP_FILESYSTEM_MAX_FILE_BYTES` | Bytes past which a file is not read as text at all |
| `SNAIL_MCP_FILESYSTEM_MAX_MATCHES` | Matches one search or listing may return |
| `SNAIL_MCP_FILESYSTEM_STEP_LIMIT` | Steps one `run_tools` call may take |

## Two things worth knowing

**`edit_file` replaces exact text, not a line range.** A line number is stale the moment anything above
it changes. A replacement that matches nothing is refused rather than written, and one that matches
twice is refused until `all` is passed: a replacement the caller did not mean is worse than none.

**`run_tools` widens one grant.** A caller given it can reach every tool of this server, including ones
it was not given separately. The server's own boundary — the session's roots — holds all the same.

## Every answer is JSON

```json
{"ok": true, "data": {…}, "error": null, "hint": null}
```

A refusal is data, not a failed call: `ok` is false, `error` says what happened and `hint` says what to
do about it. The same shape either way, because a model reads the shape it saw last.
