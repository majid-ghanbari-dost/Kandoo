using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace KandooPrintProbe
{
    public class EscPosRasterExtractor
    {
        private const byte GS = 0x1D;
        private const byte V = 0x76;
        private const byte RASTER = 0x30;

        public string ExtractToPng(
            string splFilePath,
            string outputPngPath)
        {
            if (!File.Exists(splFilePath))
            {
                throw new FileNotFoundException(
                    "SPL file not found.",
                    splFilePath);
            }

            byte[] data = File.ReadAllBytes(splFilePath);

            List<RasterBlock> blocks =
                ParseRasterBlocks(data);

            if (blocks.Count == 0)
            {
                throw new InvalidDataException(
                    "No ESC/POS raster image blocks (GS v 0) were found.");
            }

            int width = 0;
            int height = 0;

            foreach (RasterBlock block in blocks)
            {
                width = Math.Max(
                    width,
                    block.OutputWidthDots);

                height += block.OutputHeightDots;
            }

            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException(
                    "Raster image dimensions are invalid.");
            }

            string fullOutputPath =
                Path.GetFullPath(outputPngPath);

            string? directory =
                Path.GetDirectoryName(fullOutputPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var bitmap =
                new Bitmap(
                    width,
                    height,
                    PixelFormat.Format24bppRgb);

            using (Graphics graphics =
                   Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
            }

            int currentY = 0;

            foreach (RasterBlock block in blocks)
            {
                DrawBlock(
                    bitmap,
                    block,
                    currentY);

                currentY += block.OutputHeightDots;
            }

            bitmap.Save(
                fullOutputPath,
                ImageFormat.Png);

            return fullOutputPath;
        }

        private List<RasterBlock> ParseRasterBlocks(
            byte[] data)
        {
            var blocks =
                new List<RasterBlock>();

            int position = 0;

            while (position <= data.Length - 8)
            {
                int index =
                    FindNextRasterCommand(
                        data,
                        position);

                if (index < 0)
                    break;

                byte mode =
                    data[index + 3];

                int widthBytes =
                    data[index + 4] |
                    (data[index + 5] << 8);

                int heightDots =
                    data[index + 6] |
                    (data[index + 7] << 8);

                if (widthBytes <= 0 ||
                    heightDots <= 0)
                {
                    position = index + 3;
                    continue;
                }

                long payloadLength =
                    (long)widthBytes *
                    heightDots;

                long payloadStart =
                    index + 8;

                long payloadEnd =
                    payloadStart +
                    payloadLength;

                if (payloadEnd > data.Length)
                {
                    throw new InvalidDataException(
                        $"Raster block at offset {index} is truncated.");
                }

                if (mode > 3)
                {
                    throw new InvalidDataException(
                        $"Unsupported ESC/POS raster mode {mode} at offset {index}.");
                }

                if (payloadLength >
                    int.MaxValue)
                {
                    throw new InvalidDataException(
                        "Raster block is too large.");
                }

                byte[] payload =
                    new byte[(int)payloadLength];

                Buffer.BlockCopy(
                    data,
                    (int)payloadStart,
                    payload,
                    0,
                    (int)payloadLength);

                blocks.Add(
                    new RasterBlock
                    {
                        Mode = mode,
                        WidthBytes = widthBytes,
                        HeightDots = heightDots,
                        Data = payload
                    });

                position =
                    (int)payloadEnd;
            }

            return blocks;
        }

        private int FindNextRasterCommand(
            byte[] data,
            int start)
        {
            for (
                int i = start;
                i <= data.Length - 8;
                i++)
            {
                if (data[i] == GS &&
                    data[i + 1] == V &&
                    data[i + 2] == RASTER)
                {
                    return i;
                }
            }

            return -1;
        }

        private void DrawBlock(
            Bitmap bitmap,
            RasterBlock block,
            int destinationY)
        {
            int scaleX =
                block.Mode == 1 ||
                block.Mode == 3
                    ? 2
                    : 1;

            int scaleY =
                block.Mode == 2 ||
                block.Mode == 3
                    ? 2
                    : 1;

            int sourceWidthDots =
                block.WidthBytes * 8;

            int outputWidthDots =
                sourceWidthDots * scaleX;

            for (
                int y = 0;
                y < block.HeightDots;
                y++)
            {
                for (
                    int xByte = 0;
                    xByte < block.WidthBytes;
                    xByte++)
                {
                    byte value =
                        block.Data[
                            y * block.WidthBytes +
                            xByte];

                    for (
                        int bit = 0;
                        bit < 8;
                        bit++)
                    {
                        bool isBlack =
                            (value &
                             (0x80 >> bit)) != 0;

                        if (!isBlack)
                            continue;

                        int sourceX =
                            (xByte * 8) + bit;

                        int targetXStart =
                            sourceX * scaleX;

                        int targetYStart =
                            destinationY +
                            (y * scaleY);

                        for (
                            int dy = 0;
                            dy < scaleY;
                            dy++)
                        {
                            for (
                                int dx = 0;
                                dx < scaleX;
                                dx++)
                            {
                                int targetX =
                                    targetXStart + dx;

                                int targetY =
                                    targetYStart + dy;

                                if (
                                    targetX >= 0 &&
                                    targetX < bitmap.Width &&
                                    targetY >= 0 &&
                                    targetY < bitmap.Height)
                                {
                                    bitmap.SetPixel(
                                        targetX,
                                        targetY,
                                        Color.Black);
                                }
                            }
                        }
                    }
                }
            }
        }

        private class RasterBlock
        {
            public byte Mode { get; set; }

            public int WidthBytes { get; set; }

            public int HeightDots { get; set; }

            public byte[] Data { get; set; } =
                Array.Empty<byte>();

            public int OutputWidthDots =>
                WidthBytes *
                8 *
                (
                    (Mode == 1 ||
                     Mode == 3)
                        ? 2
                        : 1
                );

            public int OutputHeightDots =>
                HeightDots *
                (
                    (Mode == 2 ||
                     Mode == 3)
                        ? 2
                        : 1
                );
        }
    }
}