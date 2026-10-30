using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Kandoo.Capture;

/// <summary>ESC/POS GS-v-0 raster reconstruction without a platform-specific bitmap dependency.</summary>
public sealed class EscPosRasterExtractor
{
    private const byte GS = 0x1D;
    private const byte V = 0x76;
    private const byte Raster = 0x30;

    public EscPosRasterImage Extract(ReadOnlySpan<byte> data)
    {
        var blocks = new List<RasterBlock>();
        var position = 0;
        while (position <= data.Length - 8)
        {
            var index = FindNext(data, position);
            if (index < 0)
            {
                break;
            }

            var mode = data[index + 3];
            var widthBytes = data[index + 4] | data[index + 5] << 8;
            var heightDots = data[index + 6] | data[index + 7] << 8;
            if (widthBytes <= 0 || heightDots <= 0)
            {
                position = index + 3;
                continue;
            }

            if (mode > 3)
            {
                throw new InvalidDataException($"Unsupported ESC/POS raster mode {mode} at offset {index}.");
            }

            var payloadLength = (long)widthBytes * heightDots;
            var payloadStart = (long)index + 8;
            var payloadEnd = payloadStart + payloadLength;
            if (payloadEnd > data.Length || payloadLength > int.MaxValue)
            {
                throw new InvalidDataException($"Raster block at offset {index} is truncated or too large.");
            }

            var payload = data[(int)payloadStart..(int)payloadEnd].ToArray();
            blocks.Add(new RasterBlock(mode, widthBytes, heightDots, payload));
            position = (int)payloadEnd;
        }

        if (blocks.Count == 0)
        {
            throw new InvalidDataException("No ESC/POS raster image blocks (GS v 0) were found.");
        }

        var width = 0;
        var height = 0;
        foreach (var block in blocks)
        {
            width = Math.Max(width, block.OutputWidth);
            height = checked(height + block.OutputHeight);
        }

        var image = new EscPosRasterImage(width, height);
        var destinationY = 0;
        foreach (var block in blocks)
        {
            DrawBlock(image, block, destinationY);
            destinationY += block.OutputHeight;
        }

        return image;
    }

    public string ExtractToPng(string splFilePath, string outputPngPath)
    {
        if (!File.Exists(splFilePath))
        {
            throw new FileNotFoundException("SPL file not found.", splFilePath);
        }

        var image = Extract(File.ReadAllBytes(splFilePath));
        var fullPath = Path.GetFullPath(outputPngPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        image.WritePng(output);
        return fullPath;
    }

    private static int FindNext(ReadOnlySpan<byte> data, int start)
    {
        for (var index = start; index <= data.Length - 8; index++)
        {
            if (data[index] == GS && data[index + 1] == V && data[index + 2] == Raster)
            {
                return index;
            }
        }

        return -1;
    }

    private static void DrawBlock(EscPosRasterImage image, RasterBlock block, int destinationY)
    {
        var scaleX = block.Mode is 1 or 3 ? 2 : 1;
        var scaleY = block.Mode is 2 or 3 ? 2 : 1;
        for (var y = 0; y < block.HeightDots; y++)
        {
            for (var xByte = 0; xByte < block.WidthBytes; xByte++)
            {
                var value = block.Data[y * block.WidthBytes + xByte];
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((value & (0x80 >> bit)) == 0)
                    {
                        continue;
                    }

                    for (var dy = 0; dy < scaleY; dy++)
                    {
                        for (var dx = 0; dx < scaleX; dx++)
                        {
                            image.SetBlack((xByte * 8 + bit) * scaleX + dx, destinationY + y * scaleY + dy);
                        }
                    }
                }
            }
        }
    }

    private sealed record RasterBlock(byte Mode, int WidthBytes, int HeightDots, byte[] Data)
    {
        public int OutputWidth => WidthBytes * 8 * (Mode is 1 or 3 ? 2 : 1);
        public int OutputHeight => HeightDots * (Mode is 2 or 3 ? 2 : 1);
    }
}

public sealed class EscPosRasterImage
{
    private readonly bool[] _pixels;

    internal EscPosRasterImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Raster image dimensions are invalid.");
        }

        Width = width;
        Height = height;
        _pixels = new bool[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }

    public bool IsBlack(int x, int y)
    {
        CheckBounds(x, y);
        return _pixels[y * Width + x];
    }

    public void WritePng(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), Height);
        header[8] = 8; // bit depth
        header[9] = 0; // grayscale
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        for (var y = 0; y < Height; y++)
        {
            raw.WriteByte(0); // PNG filter: none
            for (var x = 0; x < Width; x++)
            {
                raw.WriteByte(IsBlack(x, y) ? (byte)0 : (byte)255);
            }
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    internal void SetBlack(int x, int y)
    {
        CheckBounds(x, y);
        _pixels[y * Width + x] = true;
    }

    private void CheckBounds(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(x < 0 || x >= Width ? nameof(x) : nameof(y));
        }
    }

    private static void WriteChunk(Stream output, string name, byte[] data)
    {
        var type = Encoding.ASCII.GetBytes(name);
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(integer, data.Length);
        output.Write(integer);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(integer, Crc32(type, data));
        output.Write(integer);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = Update(crc, value);
        }

        foreach (var value in data)
        {
            crc = Update(crc, value);
        }

        return ~crc;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ crc >> 1 : crc >> 1;
        }

        return crc;
    }
}
