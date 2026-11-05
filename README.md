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

**A link swapped in after the check leads nowhere.** The path is checked with its links resolved, and the file is
then opened through its folder with no link allowed anywhere on the folder's path — `O_NOFOLLOW_ANY` on macOS,
`openat2` with `RESOLVE_NO_SYMLINKS` on Linux, and on a Linux without `openat2` (a kernel before 5.6, gVisor, an amd64
image under emulation) one folder at a time from `/`, none of them a link — and made, renamed, read, listed and sized
relative to that open folder. Whatever else writes the copy — a shell, a build — cannot turn a folder into a link
between the check and the use and carry a read, a write or a listing out of the session; it gets a refusal.

**The roots are per session, not per process.** A session opened at `…/mcp?root=/path/to/copy` is served
that path and nothing else, which is what lets one server stand behind several agents without any of
them reaching another's working copy; repeat `root` for more than one. `SNAIL_MCP_FILESYSTEM_ROOTS__0`
is the volume those copies live in: a session naming a folder outside it is refused at the door, and a
session that names none is served the volume itself — a server started as a program for one caller behaves
as it always did. A request carrying an `Origin`, which a browser always sends and an agent never does, is
refused too.

## Two shapes, one server

**As a program.** Started with no port, it speaks stdio to whatever started it, belongs to that one
caller and is confined by `SNAIL_MCP_FILESYSTEM_ROOTS__0`. This is the shape a desktop client installs.

**At an address.** Given `SNAIL_MCP_FILESYSTEM_PORT`, it serves sessions at `/mcp`, each naming its own copy in
the URL. One server then stands behind every agent on the machine, and nothing in its image has to be repeated in
theirs. It listens on loopback; the image turns on `SNAIL_MCP_FILESYSTEM_LISTENS_ON_EVERY_INTERFACE`, since a container's
published port does not reach its loopback, and the published port is then the boundary: the endpoint asks for no
credential, so publish it where only the agents reach it — `docker run -p 127.0.0.1:8820:8820`, or the stand's own
network.

The image runs as its `app` user (uid 1654), as every Snail image does, and the server writes as whoever runs it.
Where the copies belong to another user, run it as that user: on the Swarm stand the workers write them as root, so
the stand starts it with `user: "0:0"` — as `app` it would read a task's files and refuse to change them.

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
| `SNAIL_MCP_FILESYSTEM_LISTENS_ON_EVERY_INTERFACE` | Whether that port is opened on every interface rather than on loopback; `true` in the image, `false` otherwise |
| `SNAIL_MCP_FILESYSTEM_ROOTS__0` | Absolute path the agent may work in when the session names none; add `ROOTS__1` for more |
| `SNAIL_MCP_FILESYSTEM_ALLOW_WRITE` | Whether `write_file` and `edit_file` do anything; `true` by default |
| `SNAIL_MCP_FILESYSTEM_OUTPUT_LIMIT_CHARS` | Characters of file text one call may return before it is cut |
| `SNAIL_MCP_FILESYSTEM_MAX_FILE_BYTES` | Bytes past which a file is not read as text at all |
| `SNAIL_MCP_FILESYSTEM_MAX_MATCHES` | Matches one search or listing may return |
| `SNAIL_MCP_FILESYSTEM_STEP_LIMIT` | Steps one `run_tools` call may take |

## Worth knowing

**`edit_file` replaces exact text, not a line range.** A line number is stale the moment anything above
it changes. A replacement that matches nothing is refused rather than written, and one that matches
twice is refused until `all` is passed: a replacement the caller did not mean is worse than none. Only the
replaced text changes: a file keeps its encoding — UTF-8, the UTF-16 or UTF-32 its byte order mark names, or any other
byte for byte — its byte order mark and its line ends, and the `\n` of a replacement is written with the line ends of
the text it replaces. A file is replaced in one step, so a reader never sees it half written, and two edits of one file
at once both land, however its name is cased. A file larger than `SNAIL_MCP_FILESYSTEM_MAX_FILE_BYTES` is not edited:
write it whole.

**Answers name paths you can hand back.** A path is named relative to the session's first root, which is where a
relative path is reached from, and whole when it lies under another root.

**`search_text` matches within one line.** A query with a line end in it is refused, rather than answered as if the
text were absent.

**Every change is journaled.** `write_file` and `edit_file` write a line to the log — standard error — with the file's
full path, which names the copy it is in, its size, and whether it was created or how many replacements were made.

**The walks stay in the copy.** `list_directory`, `find_files` and `search_text` follow no link, pass a
folder they may not open, and name in the answer what they could not read, so an empty answer is never
"nothing matched" when it was "nothing was looked at".

**`run_tools` keeps the grant.** A session opened with `&tool=` names may use those tools and no others,
whether it calls them itself or as steps of `run_tools`; and `run_tools` does not run itself.

## Every answer is JSON

```json
{"ok": true, "data": {…}, "error": null, "hint": null}
```

A refusal is data, not a failed call: `ok` is false, `error` says what happened and `hint` says what to
do about it. The same shape either way, because a model reads the shape it saw last.

Every tool lists the shape of what it answers as its `outputSchema`, and an answer that went through carries the
same object again as `structuredContent`, for a client that validates it or calls the tool typed. A refusal is an
error result in text alone. The shapes are in the [tool reference](docs/reference/tools/index.md).
