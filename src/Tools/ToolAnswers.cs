using System.ComponentModel;

namespace Snail.MCP.FileSystem.Tools;

/// <summary>What <c>read_file</c> answers.</summary>
public sealed record FileRead(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("The first line given, counted from 1.")] int From,
    [property: Description("The last line given.")] int To,
    [property: Description("How many lines the file has.")] int Lines,
    [property: Description("The lines from..to, joined by line ends.")] string Text,
    [property: Description("Whether the text was cut at the server's output limit; read on from the line after the last one shown.")] bool IsCut);

/// <summary>What <c>write_file</c> answers.</summary>
public sealed record FileWritten(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("Whether the file was not there before.")] bool IsCreated,
    [property: Description("The length of the file now, in bytes.")] long Bytes);

/// <summary>What <c>edit_file</c> answers.</summary>
public sealed record FileEdited(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("How many places were replaced.")] int Replaced,
    [property: Description("The length of the file now, in bytes.")] long Bytes);

/// <summary>What <c>list_directory</c> answers.</summary>
public sealed record FolderListed(
    [property: Description("The folder listed, relative to the session's root.")] string Path,
    IReadOnlyList<FolderEntry> Entries,
    [property: Description("Whether there was more than the server lists at once.")] bool IsCut,
    [property: Description("Folders that could not be opened, so nothing in them is listed.")] IReadOnlyList<string> Unread,
    [property: Description("What the unread folders mean for the answer; null when there are none.")] string? Note);

/// <summary>One file or folder of a listing.</summary>
public sealed record FolderEntry(
    [property: Description("Relative to the session's root.")] string Path,
    [property: Description("Whether it is a folder.")] bool IsDirectory,
    [property: Description("The length of a file in bytes; 0 for a folder.")] long Bytes);

/// <summary>What <c>find_files</c> answers.</summary>
public sealed record FilesFound(
    [property: Description("The pattern as matched.")] string Pattern,
    [property: Description("The files matched, relative to the session's root.")] IReadOnlyList<string> Files,
    [property: Description("Whether there were more matches than the server gives at once.")] bool IsCut,
    [property: Description("Folders that could not be opened, so nothing in them was matched.")] IReadOnlyList<string> Unread,
    [property: Description("What the unread folders mean for the answer; null when there are none.")] string? Note);

/// <summary>What <c>search_text</c> answers.</summary>
public sealed record TextFound(
    [property: Description("The text searched for.")] string Query,
    IReadOnlyList<TextLine> Matches,
    [property: Description("Whether there were more matches than the server gives at once.")] bool IsCut,
    [property: Description("Files and folders not searched: too large, or not readable.")] IReadOnlyList<string> Unread,
    [property: Description("What the unread files mean for the answer; null when there are none.")] string? Note);

/// <summary>One line that holds the text searched for.</summary>
public sealed record TextLine(
    [property: Description("The file, relative to the session's root.")] string Path,
    [property: Description("The line's number, counted from 1.")] int Line,
    [property: Description("The line, cut to 240 characters.")] string Text);
