namespace Kandoo.HolooDiscovery;

public static class BackupInspector
{
    public const int MagicByteLimit = 16;

    public static BackupInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var metadata = new FileInfo(fullPath);
        if (!metadata.Exists)
        {
            throw new FileNotFoundException("The user-selected backup does not exist.", fullPath);
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.SequentialScan);
        var magic = ReadMagic(stream);
        var (compression, encryption) = Guess(magic);
        return new BackupInspection(
            fullPath,
            metadata.Length,
            Convert.ToHexString(magic),
            compression,
            encryption);
    }

    internal static byte[] ReadMagic(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new byte[MagicByteLimit];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = stream.Read(buffer, count, buffer.Length - count);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        return buffer[..count];
    }

    internal static (string Compression, string Encryption) Guess(ReadOnlySpan<byte> magic)
    {
        if (StartsWith(magic, [0x50, 0x4B, 0x03, 0x04]) ||
            StartsWith(magic, [0x50, 0x4B, 0x05, 0x06]) ||
            StartsWith(magic, [0x50, 0x4B, 0x07, 0x08]))
        {
            var encrypted = magic.Length >= 8 && (magic[6] & 0x01) != 0;
            return ("likely-zip", encrypted ? "possible-zip-flag" : "not-indicated-in-first-16-bytes");
        }

        if (StartsWith(magic, [0x1F, 0x8B])) return ("likely-gzip", "unknown");
        if (StartsWith(magic, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])) return ("likely-7z", "unknown");
        if (StartsWith(magic, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07])) return ("likely-rar", "unknown");
        if (StartsWith(magic, [0x42, 0x5A, 0x68])) return ("likely-bzip2", "unknown");
        if (StartsWith(magic, [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00])) return ("likely-xz", "unknown");
        if (StartsWith(magic, [0x28, 0xB5, 0x2F, 0xFD])) return ("likely-zstd", "unknown");
        return ("unknown", "unknown");
    }

    private static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix) =>
        value.Length >= prefix.Length && value[..prefix.Length].SequenceEqual(prefix);
}
