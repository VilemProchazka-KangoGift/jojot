using System.Text;
using JoJot.Resources;

namespace JoJot.AvaloniaExperiment;

internal static class TextNoteFiles
{
    internal const int ImportLimit = 500 * 1024;
    internal static readonly Encoding DefaultEncoding = new UTF8Encoding(true, true);
    private static readonly Encoding[] BomEncodings =
    [
        new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
        DefaultEncoding, new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true),
    ];
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal sealed class FileTooLargeException(string message) : IOException(message);
    internal sealed record Document(string Content, Encoding Encoding);

    internal static async Task<Document> ReadAsync(string path, int limit = ImportLimit)
    {
        await using var file = Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ReadAsync(file, path, limit).ConfigureAwait(false);
    }

    internal static async Task<Encoding> WriteAsync(string path, string content, Encoding fallback, int inspectionLimit)
    {
        await using var file = Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Validate the current target under the same handle used to write it.
        var encoding = file.Length > 0
            ? (await ReadAsync(file, path, inspectionLimit).ConfigureAwait(false)).Encoding
            : fallback;
        var bytes = encoding.GetBytes(content);
        file.Position = 0;
        await file.WriteAsync(encoding.GetPreamble()).ConfigureAwait(false);
        await file.WriteAsync(bytes).ConfigureAwait(false);
        file.SetLength(file.Position);
        return encoding;
    }

    private static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) =>
        new(path, mode, access, share, bufferSize: 4096, useAsync: true);

    private static async Task<Document> ReadAsync(FileStream file, string path, int limit)
    {
        if (file.Length > limit)
            throw new FileTooLargeException(string.Format(Strings.Drop_TooLarge, Path.GetFileName(path)));

        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes).ConfigureAwait(false);
        foreach (var encoding in BomEncodings)
        {
            var preamble = encoding.GetPreamble();
            if (bytes.AsSpan().StartsWith(preamble))
                return Decode(bytes, preamble.Length, encoding, path);
        }
        return Decode(bytes, 0, Utf8, path);
    }

    private static Document Decode(byte[] bytes, int offset, Encoding encoding, string path)
    {
        string text;
        try
        {
            text = encoding.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            throw BinaryFile(path);
        }
        foreach (var character in text)
            if (char.IsControl(character) && character is not ('\t' or '\n' or '\r' or '\f' or '\v' or '\u001b'))
                throw BinaryFile(path);
        return new Document(text, encoding);
    }

    private static InvalidDataException BinaryFile(string path) =>
        new(string.Format(Strings.Drop_Binary, Path.GetFileName(path)));
}
