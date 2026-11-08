using System.Text;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests.Chaos;

/// <summary>
/// <c>edit_file</c> promises an exact replacement and nothing else. It reads the file as text, decoded and normalised,
/// and writes the whole file back as UTF-8: whatever the decoding changed is written too, and two edits at once each
/// write back the file as it was before the other.
/// </summary>
public sealed class EditChaosTests
{
    /// <summary>
    /// <c>read_file</c> joins lines with <c>\n</c> whatever the file uses, and <c>edit_file</c> wants the exact text
    /// "including its line ends". Every multi-line edit of a file with Windows line ends is refused, with a hint to read
    /// the file first — which shows the same <c>\n</c> again.
    /// </summary>
    [Fact]
    public void EditFile_TextAsReadFileShowedItOfACrlfFile_IsReplaced()
    {
        using var copy = new Copy();
        copy.Write("App.cs", "first();\r\nsecond();\r\n");

        string shown = copy.Files.Read("App.cs", 1, 2).Value!["text"]!.GetValue<string>();
        Answer<JsonObject> answer = copy.Files.Edit("App.cs", shown, "both();", all: false);

        Assert.True(answer.IsSuccess, answer.Error);
    }

    /// <summary>
    /// A file that is not UTF-8 — Latin-1, Windows-1251, the encoding of half the legacy code there is — is decoded with
    /// each foreign byte turned into U+FFFD and written back as UTF-8. One edit of one ASCII line rewrites every non-ASCII
    /// byte of the file into <c>EF BF BD</c>, and the answer says <c>ok</c>.
    /// </summary>
    [Fact]
    public void EditFile_Latin1BytesOutsideTheEdit_StayAsTheyWere()
    {
        using var copy = new Copy();
        string path = copy.Write("about.txt", "");
        byte[] before = [.. "author: Ren"u8, 0xE9, .. "\nversion: 1\n"u8];
        File.WriteAllBytes(path, before);

        copy.Files.Edit("about.txt", "version: 1", "version: 2", all: false);

        byte[] expected = [.. "author: Ren"u8, 0xE9, .. "\nversion: 2\n"u8];
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    /// <summary>The byte order mark is dropped by the read and never written again: a one-character edit changes the first three bytes of the file.</summary>
    [Fact]
    public void EditFile_FileWithAByteOrderMark_KeepsIt()
    {
        using var copy = new Copy();
        string path = copy.Write("App.cs", "");
        File.WriteAllText(path, "var a = 1;\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        copy.Files.Edit("App.cs", "var a = 1;", "var a = 2;", all: false);

        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
    }

    /// <summary>
    /// Read, replace, write, with nothing between two callers: a client sends a model's parallel calls in parallel, and
    /// each edit writes back the file as it read it, undoing the edits that landed in between. Every caller hears
    /// <c>ok</c>; the file keeps only some of them.
    /// </summary>
    [Fact]
    public async Task EditFile_ParallelEditsOfOneFile_LoseNoReplacement()
    {
        using var copy = new Copy();
        const int slots = 32;
        string path = copy.Write("slots.txt", string.Concat(Enumerable.Range(0, slots).Select(slot => $"slot{slot}\n")));

        string[] outcomes = await Task.WhenAll(Enumerable.Range(0, slots).Select(slot => Task.Run(() => Edited(copy, slot))));

        int left = (await File.ReadAllLinesAsync(path)).Count(line => line.StartsWith("slot", StringComparison.Ordinal));
        Assert.True(left == 0, $"{left} of {slots} edits are gone; the callers heard: {string.Join(", ", outcomes.CountBy(outcome => outcome))}");
    }

    private static string Edited(Copy copy, int slot)
    {
        try
        {
            return copy.Files.Edit("slots.txt", $"slot{slot}\n", $"done{slot}\n", all: false).IsSuccess ? "ok" : "refused";
        }
        catch (IOException exception)
        {
            return exception.GetType().Name;
        }
    }
}
