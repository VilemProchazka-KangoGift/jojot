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

    private static readonly Encoding Windows1250 = Legacy(1250);
    private static readonly Encoding Windows1252 = Legacy(1252);

    internal static Encoding? GetLegacyEncoding(int codePage) => codePage switch
    {
        1250 => Windows1250,
        1252 => Windows1252,
        _ => null,
    };

    private static Encoding Legacy(int codePage)
    {
        var encoding = (Encoding)CodePagesEncodingProvider.Instance.GetEncoding(codePage)!.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        return encoding;
    }

    internal sealed class FileTooLargeException(string message) : IOException(message);
    internal sealed record Document(string Content, Encoding Encoding);

    internal static async Task<Document> ReadAsync(string path, int limit = ImportLimit, Encoding? preferred = null)
    {
        await using var file = Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ReadAsync(file, path, limit, preferred).ConfigureAwait(false);
    }

    internal static async Task<Encoding> WriteAsync(string path, string content, Encoding fallback, int inspectionLimit, Encoding? preferred = null)
    {
        await using var file = Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Validate the current target under the same handle used to write it.
        var encoding = file.Length > 0
            ? (await ReadAsync(file, path, inspectionLimit, preferred).ConfigureAwait(false)).Encoding
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

    private static async Task<Document> ReadAsync(FileStream file, string path, int limit, Encoding? preferred)
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
        var utf8 = TryDecode(bytes, 0, Utf8);
        if (utf8 is not null && (preferred is null || Array.Exists(bytes, value => value >= 0x80)))
            return new Document(utf8, Utf8);

        if (preferred is not null && TryDecode(bytes, 0, preferred) is { } remembered)
            return new Document(remembered, preferred);

        var central = TryDecode(bytes, 0, Windows1250);
        var western = TryDecode(bytes, 0, Windows1252);
        if (western is not null && (central is null || Score(western, "œŒÿŸ") > Score(central, "ąĄćĆęĘłŁńŃőŐřŘśŚťŤůŮűŰźŹżŻ")))
            return new Document(western, Windows1252);
        if (central is not null)
            return new Document(central, Windows1250);
        throw BinaryFile(path);
    }

    private static int Score(string text, string distinctiveLetters) =>
        text.Count(character => distinctiveLetters.Contains(character));

    private static Document Decode(byte[] bytes, int offset, Encoding encoding, string path) =>
        TryDecode(bytes, offset, encoding) is { } text ? new Document(text, encoding) : throw BinaryFile(path);

    private static string? TryDecode(byte[] bytes, int offset, Encoding encoding)
    {
        string text;
        try
        {
            text = encoding.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        foreach (var character in text)
            if (char.IsControl(character) && character is not ('\t' or '\n' or '\r' or '\f' or '\v' or '\u001b'))
                return null;
        return text;
    }

    private static InvalidDataException BinaryFile(string path) =>
        new(string.Format(Strings.Drop_Binary, Path.GetFileName(path)));
}
