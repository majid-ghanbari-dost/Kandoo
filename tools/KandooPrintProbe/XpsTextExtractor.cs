using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace KandooPrintProbe
{
    public class XpsTextExtractor
    {
        public string ExtractText(string splFilePath)
        {
            if (!File.Exists(splFilePath))
                throw new FileNotFoundException(
                    "SPL file not found.",
                    splFilePath);

            var glyphs = new List<GlyphInfo>();

            using (var archive = ZipFile.OpenRead(splFilePath))
            {
                var pageFiles = archive.Entries
                    .Where(e =>
                        e.FullName.EndsWith(
                            ".fpage",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName)
                    .ToList();

                foreach (var page in pageFiles)
                {
                    using var stream = page.Open();

                    var document = XDocument.Load(stream);

                    XNamespace ns =
                        "http://schemas.openxps.org/oxps/v1.0";

                    foreach (var glyph in document.Descendants(ns + "Glyphs"))
                    {
                        string? text =
                            glyph.Attribute("UnicodeString")?.Value;

                        if (string.IsNullOrEmpty(text))
                            continue;

                        glyphs.Add(new GlyphInfo
                        {
                            Text = text,
                            X = ReadDouble(
                                glyph.Attribute("OriginX")?.Value),
                            Y = ReadDouble(
                                glyph.Attribute("OriginY")?.Value)
                        });
                    }
                }
            }

            return BuildText(glyphs);
        }

        private string BuildText(List<GlyphInfo> glyphs)
        {
            if (glyphs.Count == 0)
                return "";

            var lines = BuildLines(glyphs);

            var result = new StringBuilder();

            foreach (var line in lines)
            {
                string text = BuildLine(line);

                if (!string.IsNullOrWhiteSpace(text))
                    result.AppendLine(text);
            }

            return result.ToString().TrimEnd();
        }

        private List<List<GlyphInfo>> BuildLines(
            List<GlyphInfo> glyphs)
        {
            var lines = new List<List<GlyphInfo>>();

            foreach (var glyph in glyphs.OrderBy(g => g.Y))
            {
                List<GlyphInfo>? bestLine = null;
                double bestDistance = double.MaxValue;

                foreach (var line in lines)
                {
                    double averageY =
                        line.Average(g => g.Y);

                    double distance =
                        Math.Abs(glyph.Y - averageY);

                    if (distance <= 3.0 &&
                        distance < bestDistance)
                    {
                        bestLine = line;
                        bestDistance = distance;
                    }
                }

                if (bestLine == null)
                {
                    bestLine = new List<GlyphInfo>();
                    lines.Add(bestLine);
                }

                bestLine.Add(glyph);
            }

            return lines
                .OrderBy(line => line.Average(g => g.Y))
                .ToList();
        }

        private string BuildLine(List<GlyphInfo> line)
        {
            var glyphs = line
                .OrderByDescending(g => g.X)
                .ToList();

            var blocks = new List<TextBlock>();

            foreach (var glyph in glyphs)
            {
                string rawText = glyph.Text;

                if (string.IsNullOrEmpty(rawText))
                    continue;

                TextDirection direction =
                    DetectDirection(rawText);

                string text;

                if (direction == TextDirection.Rtl)
                {
                    /*
                     * XPS stores many Persian/Arabic glyph blocks
                     * in visual/reversed order.
                     *
                     * IMPORTANT:
                     * Reverse first, normalize second.
                     */

                    text = ReverseRtlBlock(rawText);
                    text = NormalizeText(text);
                }
                else
                {
                    /*
                     * English and numeric blocks stay unchanged.
                     */

                    text = NormalizeText(rawText);
                }

                if (string.IsNullOrEmpty(text))
                    continue;

                blocks.Add(new TextBlock
                {
                    Text = text,
                    X = glyph.X,
                    Direction = direction
                });
            }

            blocks = MergeCompatibleBlocks(blocks);

            return ReconstructLogicalLine(blocks);
        }

        private List<TextBlock> MergeCompatibleBlocks(
            List<TextBlock> blocks)
        {
            if (blocks.Count <= 1)
                return blocks;

            var result = new List<TextBlock>();

            foreach (var block in blocks)
            {
                if (result.Count == 0)
                {
                    result.Add(block);
                    continue;
                }

                var previous = result[^1];

                /*
                 * Adjacent RTL blocks are part of the same
                 * Persian phrase.
                 *
                 * English/numbers remain independent.
                 */

                if (previous.Direction == TextDirection.Rtl &&
                    block.Direction == TextDirection.Rtl)
                {
                    previous.Text += block.Text;
                }
                else
                {
                    result.Add(block);
                }
            }

            return result;
        }

        private string ReconstructLogicalLine(
            List<TextBlock> blocks)
        {
            if (blocks.Count == 0)
                return "";

            var working = new List<TextBlock>(blocks);

            /*
             * Example visual order:
             *
             * Lenovo | نام | کالا | : | لپ | تاپ
             *
             * Logical order:
             *
             * نام کالا: لپ تاپ Lenovo
             */

            while (
                working.Count > 1 &&
                working[0].Direction == TextDirection.Ltr &&
                working.Skip(1).Any(b =>
                    b.Direction == TextDirection.Rtl))
            {
                TextBlock first = working[0];

                working.RemoveAt(0);
                working.Add(first);
            }

            var result = new StringBuilder();

            foreach (var block in working)
            {
                result.Append(block.Text);
            }

            return CleanupSpacing(result.ToString());
        }

        private TextDirection DetectDirection(string text)
        {
            foreach (char c in text)
            {
                if (IsPersianOrArabic(c))
                    return TextDirection.Rtl;

                if (char.IsLetterOrDigit(c))
                    return TextDirection.Ltr;
            }

            return TextDirection.Rtl;
        }

        private bool IsPersianOrArabic(char c)
        {
            return
                (c >= '\u0600' && c <= '\u06FF') ||
                (c >= '\u0750' && c <= '\u077F') ||
                (c >= '\uFB50' && c <= '\uFDFF') ||
                (c >= '\uFE70' && c <= '\uFEFF');
        }

        private string ReverseRtlBlock(string text)
        {
            char[] chars = text.ToCharArray();

            Array.Reverse(chars);

            return new string(chars);
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            /*
             * First perform Unicode compatibility normalization.
             *
             * This converts many Arabic/Persian presentation forms
             * such as:
             *
             * ﻋﻠﯽ
             *
             * into their normal Unicode characters.
             */

            text = text.Normalize(NormalizationForm.FormKC);

            /*
             * Then normalize common Arabic/Persian variants.
             */

            return text
                .Replace("ي", "ی")
                .Replace("ى", "ی")
                .Replace("ك", "ک")
                .Replace("ة", "ه")
                .Replace("ۀ", "ه")
                .Replace("ـ", "");
        }

        private string CleanupSpacing(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var sb = new StringBuilder();

            bool previousWasSpace = false;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWasSpace)
                    {
                        sb.Append(' ');
                        previousWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    previousWasSpace = false;
                }
            }

            string result = sb.ToString().Trim();

            /*
             * Normalize spacing around colon.
             */

            result = result.Replace(" :", ":");
            result = result.Replace(":", ": ");

            while (result.Contains("  "))
                result = result.Replace("  ", " ");

            return result.Trim();
        }

        private static double ReadDouble(string? value)
        {
            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double result))
            {
                return result;
            }

            return 0;
        }

        private enum TextDirection
        {
            Rtl,
            Ltr
        }

        private class GlyphInfo
        {
            public string Text { get; set; } = "";

            public double X { get; set; }

            public double Y { get; set; }
        }

        private class TextBlock
        {
            public string Text { get; set; } = "";

            public double X { get; set; }

            public TextDirection Direction { get; set; }
        }
    }
}