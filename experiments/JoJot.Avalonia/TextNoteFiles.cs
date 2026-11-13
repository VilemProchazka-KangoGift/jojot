using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using JoJot.Resources;

namespace JoJot.AvaloniaExperiment;

internal static partial class TextNoteFiles
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

    internal static async Task<Encoding> WriteAsync(string path, string content, Encoding fallback, int inspectionLimit,
        Encoding? preferred = null, Action? beforeCommit = null)
    {
        path = Path.GetFullPath(path);
        await using var original = OpenExisting(path);
        var identity = original is null ? default : GetIdentity(original);
        var encoding = original is { Length: > 0 }
            ? (await ReadAsync(original, path, inspectionLimit, preferred).ConfigureAwait(false)).Encoding
            : fallback;
        var bytes = encoding.GetBytes(content);
        var stagingPath = Path.Combine(Path.GetDirectoryName(path)!, $".jojot-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            await using (var staging = Open(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                await staging.WriteAsync(encoding.GetPreamble()).ConfigureAwait(false);
                await staging.WriteAsync(bytes).ConfigureAwait(false);
                staging.Flush(flushToDisk: true);
            }

            beforeCommit?.Invoke();
            if (original is null)
                File.Move(stagingPath, path, overwrite: false);
            else
            {
                using var current = OpenExisting(path);
                if (current is null || GetIdentity(current) != identity)
                    throw new IOException($"The destination changed while saving {Path.GetFileName(path)}.");

                // Windows replacement requires write sharing. Check while both handles
                // still exclude writers, then release them immediately before replacement.
                current.Dispose();
                original.Dispose();
                File.Replace(stagingPath, path, destinationBackupFileName: null);
            }
            return encoding;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(stagingPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    JoJot.Services.LogService.Warn("Could not remove the external save staging file {Path}", stagingPath, ex);
                }
            }
        }
    }

    private static FileStream? OpenExisting(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                throw new UnauthorizedAccessException($"Cannot replace {Path.GetFileName(path)}.");
            return Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (FileNotFoundException) { return null; }
    }

    private static (ulong Volume, ulong Low, ulong High) GetIdentity(FileStream file)
    {
        if (!GetFileInformationByHandleEx(file.SafeFileHandle, 18, out var identity, 24))
            throw new IOException("Could not determine the destination file identity.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        return (identity.Volume, identity.Low, identity.High);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdentity
    {
        public ulong Volume;
        public ulong Low;
        public ulong High;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileIdentity identity, uint size);

    private static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) =>
        new(path, mode, access, share, bufferSize: 4096, useAsync: true);

    internal static async Task<(Document Current, Document? Corrected)> ReadLegacyCorrectionAsync(
        string path, int limit, Encoding? preferred, Encoding requested)
    {
        await using var file = Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = await ReadBytesAsync(file, path, limit).ConfigureAwait(false);
        var current = Detect(bytes, path, preferred);
        return (current, GetLegacyEncoding(current.Encoding.CodePage) is null ? null : Decode(bytes, 0, requested, path));
    }

    private static async Task<Document> ReadAsync(FileStream file, string path, int limit, Encoding? preferred) =>
        Detect(await ReadBytesAsync(file, path, limit).ConfigureAwait(false), path, preferred);

    private static async Task<byte[]> ReadBytesAsync(FileStream file, string path, int limit)
    {
        if (file.Length > limit)
            throw new FileTooLargeException(string.Format(Strings.Drop_TooLarge, Path.GetFileName(path)));

        var bytes = new byte[(int)file.Length];
        await file.ReadExactlyAsync(bytes).ConfigureAwait(false);
        return bytes;
    }

    private static Document Detect(byte[] bytes, string path, Encoding? preferred)
    {
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
