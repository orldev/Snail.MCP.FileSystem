# Tools

8 tools in 3 families.
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

A local MCP server that lets an AI agent read, write and search the files of the working copy its session names, and nothing outside it. `read_file`, `list_directory`, `find_files` and `search_text` read; `write_file` and `edit_file` change; `find_tool` says which of them answers a task described in words, and `run_tools` runs several of them in one call. There is no tool that deletes, moves or runs anything.

| Tool | Family | What it does |
| --- | --- | --- |
| [`read_file`](files.md#read_file) | Files of the working copy | Reads a file as text, whole or by lines. |
| [`write_file`](files.md#write_file) | Files of the working copy | Writes a file whole, creating the folders above it. |
| [`edit_file`](files.md#edit_file) | Files of the working copy | Replaces exact text in a file; nothing else in it changes, its encoding and line ends included. |
| [`list_directory`](files.md#list_directory) | Files of the working copy | Lists what is under a folder, to the depth asked for. |
| [`find_files`](files.md#find_files) | Files of the working copy | Names the files whose path matches a glob: *.cs, **/Tests/*.cs, appsettings.*.json. |
| [`search_text`](files.md#search_text) | Files of the working copy | Names the lines that contain a text, with the file and the line number of each. |
| [`find_tool`](discovery.md#find_tool) | Finding a tool | Finds tools of this server for a task in words, best first, with the parameters of each. |
| [`run_tools`](sequence.md#run_tools) | Several tools in one call | Runs tools of this server in order in one call, stopping at the first failure; no step sees an earlier answer. |
