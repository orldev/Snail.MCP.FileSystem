using System.Text;
using System.Text.Unicode;

namespace Snail.MCP.FileSystem.Application;

/// <summary>How the bytes of a file are its text and back: the encoding its byte order mark names, else UTF-8, else Latin-1.</summary>
/// <param name="Encoding">The encoding of the text after the mark.</param>
/// <param name="Preamble">How many bytes the byte order mark takes; they are written back as they were.</param>
/// <remarks>
/// Latin-1 maps every byte to one character and back, so a file that is not UTF-8 is edited byte for byte. A UTF-16 file was
/// taken for one: <c>read_file</c>, whose reader honours every byte order mark, showed its text, and the edit found none of it.
/// </remarks>
internal sealed record TextCodec(Encoding Encoding, int Preamble)
{
    /// <summary>UTF-8 that refuses what is not UTF-8, rather than turning it into U+FFFD.</summary>
    private static readonly Encoding Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Latin-1, which maps every byte to one character and back.</summary>
    private static readonly Encoding Bytewise = Encoding.GetEncoding("iso-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>The encodings a byte order mark names; UTF-32 first, since its little-endian mark begins with UTF-16's.</summary>
    private static readonly Encoding[] Marked =
    [
        new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true),
        new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true),
        new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true),
        new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true)
    ];

    /// <summary>The codec of a file's bytes.</summary>
    public static TextCodec Of(byte[] bytes)
    {
        foreach (Encoding marked in Marked)
        {
            int length = marked.Preamble.Length;

            if (bytes.AsSpan().StartsWith(marked.Preamble))
            {
                return new TextCodec(marked, length);
            }
        }

        return Utf8.IsValid(bytes) ? new TextCodec(Strict, 0) : new TextCodec(Bytewise, 0);
    }

    /// <summary>The text of the bytes, without their byte order mark.</summary>
    public string Decode(byte[] bytes) => Encoding.GetString(bytes, Preamble, bytes.Length - Preamble);

    /// <summary>The text as bytes behind the original's byte order mark, or null when the encoding has no bytes for a letter of it.</summary>
    public byte[]? Encode(byte[] original, string text)
    {
        try
        {
            byte[] body = Encoding.GetBytes(text);

            return [.. original.AsSpan(0, Preamble), .. body];
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }
}
