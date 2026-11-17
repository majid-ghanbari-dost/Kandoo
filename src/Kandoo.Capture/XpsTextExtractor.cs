using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Kandoo.Capture;

/// <summary>OpenXPS glyph extraction brought over from KandooPrintProbe.</summary>
public sealed class XpsTextExtractor
{
    public string ExtractText(string splFilePath)
    {
        if (!File.Exists(splFilePath))
        {
            throw new FileNotFoundException("SPL file not found.", splFilePath);
        }

        using var stream = new FileStream(splFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return ExtractText(stream);
    }

    public string ExtractText(Stream splStream)
    {
        ArgumentNullException.ThrowIfNull(splStream);
        var glyphs = new List<GlyphInfo>();
        using var archive = new ZipArchive(splStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var page in archive.Entries
                     .Where(entry => entry.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            using var stream = page.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxps.org/oxps/v1.0";
            foreach (var glyph in document.Descendants(ns + "Glyphs"))
            {
                var text = glyph.Attribute("UnicodeString")?.Value;
                if (!string.IsNullOrEmpty(text))
                {
                    glyphs.Add(new GlyphInfo(
                        text,
                        ReadDouble(glyph.Attribute("OriginX")?.Value),
                        ReadDouble(glyph.Attribute("OriginY")?.Value)));
                }
            }
        }

        return BuildText(glyphs);
    }

    private static string BuildText(List<GlyphInfo> glyphs)
    {
        var result = new StringBuilder();
        foreach (var line in BuildLines(glyphs))
        {
            var text = BuildLine(line);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.AppendLine(text);
            }
        }

        return result.ToString().TrimEnd();
    }

    private static List<List<GlyphInfo>> BuildLines(List<GlyphInfo> glyphs)
    {
        var lines = new List<List<GlyphInfo>>();
        foreach (var glyph in glyphs.OrderBy(glyph => glyph.Y))
        {
            List<GlyphInfo>? best = null;
            var bestDistance = double.MaxValue;
            foreach (var line in lines)
            {
                var distance = Math.Abs(glyph.Y - line.Average(item => item.Y));
                if (distance <= 3.0 && distance < bestDistance)
                {
                    best = line;
                    bestDistance = distance;
                }
            }

            if (best is null)
            {
                best = new List<GlyphInfo>();
                lines.Add(best);
            }

            best.Add(glyph);
        }

        return lines.OrderBy(line => line.Average(glyph => glyph.Y)).ToList();
    }

    private static string BuildLine(List<GlyphInfo> line)
    {
        var blocks = new List<TextBlock>();
        foreach (var glyph in line.OrderByDescending(glyph => glyph.X))
        {
            var direction = DetectDirection(glyph.Text);
            var text = direction == TextDirection.Rtl
                ? NormalizeText(ReverseRtlBlock(glyph.Text))
                : NormalizeText(glyph.Text);
            if (!string.IsNullOrEmpty(text))
            {
                blocks.Add(new TextBlock(text, direction));
            }
        }

        var merged = new List<TextBlock>();
        foreach (var block in blocks)
        {
            if (merged.Count > 0 && merged[^1].Direction == TextDirection.Rtl && block.Direction == TextDirection.Rtl)
            {
                merged[^1].Text += block.Text;
            }
            else
            {
                merged.Add(block);
            }
        }

        while (merged.Count > 1 && merged[0].Direction == TextDirection.Ltr && merged.Skip(1).Any(block => block.Direction == TextDirection.Rtl))
        {
            var first = merged[0];
            merged.RemoveAt(0);
            merged.Add(first);
        }

        return CleanupSpacing(string.Concat(merged.Select(block => block.Text)));
    }

    private static TextDirection DetectDirection(string text)
    {
        foreach (var character in text)
        {
            if (IsPersianOrArabic(character))
            {
                return TextDirection.Rtl;
            }

            if (char.IsLetterOrDigit(character))
            {
                return TextDirection.Ltr;
            }
        }

        return TextDirection.Rtl;
    }

    private static bool IsPersianOrArabic(char character) =>
        character is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or
            >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';

    private static string ReverseRtlBlock(string text)
    {
        var characters = text.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }

    private static string NormalizeText(string text) => text
        .Normalize(NormalizationForm.FormKC)
        .Replace("ي", "ی")
        .Replace("ى", "ی")
        .Replace("ك", "ک")
        .Replace("ة", "ه")
        .Replace("ۀ", "ه")
        .Replace("ـ", string.Empty);

    private static string CleanupSpacing(string text)
    {
        var result = new StringBuilder();
        var previousWasSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    result.Append(' ');
                }
            }
            else
            {
                result.Append(character);
            }

            previousWasSpace = char.IsWhiteSpace(character);
        }

        var value = result.ToString().Trim().Replace(" :", ":").Replace(":", ": ");
        while (value.Contains("  ", StringComparison.Ordinal))
        {
            value = value.Replace("  ", " ", StringComparison.Ordinal);
        }

        return value.Trim();
    }

    private static double ReadDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private enum TextDirection
    {
        Rtl,
        Ltr
    }

    private sealed record GlyphInfo(string Text, double X, double Y);

    private sealed class TextBlock(string text, TextDirection direction)
    {
        public string Text { get; set; } = text;
        public TextDirection Direction { get; } = direction;
    }
}
