using System.Text;
using System.Text.Json;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class CanonicalJsonU7Tests
{
    public sealed record Vector(string Id, int[] Codepoints, string Utf8Hex, string Sha256);

    // Pinned golden table, addendum section 6 option (b), delivered and verified by the
    // Coordination Manager (W11-C-U7). Inputs are rebuilt here purely from the codepoint
    // column - the JSON column is never copy-pasted because it carries invisible characters
    // such as ZWNJ (U+200C).
    public static IEnumerable<object[]> Vectors =>
        new[]
        {
            new object[]
            {
                new Vector(
                    "V1",
                    new[] { 0x06A9, 0x0646, 0x062F, 0x0648 },
                    "7b2270726f6265223a22daa9d986d8afd988227d",
                    "ceed0b19692bbc3184107f0a4c4f1d328a895656ae247b74b1bcf8006d61e7db")
            },
            new object[]
            {
                new Vector(
                    "V2",
                    new[] { 0x062F, 0x06CC, 0x062C, 0x06CC, 0x062A, 0x0627, 0x0644, 0x200C, 0x0641, 0x0627, 0x06A9, 0x062A, 0x0648, 0x0631 },
                    "7b2270726f6265223a22d8afdb8cd8acdb8cd8aad8a7d984e2808cd981d8a7daa9d8aad988d8b1227d",
                    "039a818afb59196c436d4058a6aace3d545645a1ba013f003694cffd6a127531")
            },
            new object[]
            {
                new Vector(
                    "V3",
                    new[] { 0x0645, 0x062F, 0x064A, 0x0631 },
                    "7b2270726f6265223a22d985d8afd98ad8b1227d",
                    "4463ef265ef926d185317b2fe5e1ba85f50a5f997b02e7f833a9ade4b93749fb")
            },
            new object[]
            {
                new Vector(
                    "V4",
                    new[] { 0x0643, 0x0646, 0x062F, 0x0648 },
                    "7b2270726f6265223a22d983d986d8afd988227d",
                    "c8e64e79a339b24cadc288bf5d9e43ca4fa922f02d05b1bf78a0a8f1c638adf0")
            },
            new object[]
            {
                new Vector(
                    "V5",
                    new[] { 0x06A9, 0x0646, 0x062F, 0x0648, 0x0020, 0x0645, 0x062F, 0x06CC, 0x0631 },
                    "7b2270726f6265223a22daa9d986d8afd98820d985d8afdb8cd8b1227d",
                    "edd9bfff735b41d49585defca405ae536b3798692237fe7519740e4291f8eb76")
            },
            new object[]
            {
                new Vector(
                    "V6",
                    new[] { 0x0061, 0x0000, 0x0062, 0x0001, 0x0063, 0x001F, 0x0064 },
                    "7b2270726f6265223a22615c7530303030625c7530303031635c753030314664227d",
                    "e86f78c17e387ba3f7c3d537389bd6a8fad119cf6ddc46795cc28795f0bc30a4")
            },
            new object[]
            {
                new Vector(
                    "V7",
                    new[] { 0x0064, 0x0065, 0x006C, 0x007F, 0x0078 },
                    "7b2270726f6265223a2264656c5c753030374678227d",
                    "0d51cd0b6002c85b33a72365357afd73575f2dcc1507df5b21757efb27f68aa0")
            },
            new object[]
            {
                new Vector(
                    "V8",
                    new[] { 0x0639, 0x0644, 0x06CC, 0x200C, 0x0631, 0x0636, 0x0627, 0x0007, 0x0628 },
                    "7b2270726f6265223a22d8b9d984db8ce2808cd8b1d8b6d8a75c7530303037d8a8227d",
                    "9217d07efe51414fed0987f807631f80e5a11c8b1842671045ea17c6c3853006")
            },
            new object[]
            {
                new Vector(
                    "V9",
                    new[] { 0x0022, 0x005C, 0x0022, 0x002F },
                    "7b2270726f6265223a225c225c5c5c222f227d",
                    "199c497795cd4b23321f62baa1ab9a5715ddc9dfc4a8f10f7e2b3eebc514283a")
            },
            new object[]
            {
                new Vector(
                    "V10",
                    new[] { 0x0074, 0x0009, 0x006E, 0x000A, 0x06A9 },
                    "7b2270726f6265223a22745c75303030396e5c7530303041daa9227d",
                    "8c4e12e66dc4824a50677b24ce0e62e932c5a615015a6634ef4e9e0715fdebe0")
            },
            new object[]
            {
                new Vector(
                    "V11",
                    new[] { 0x200C },
                    "7b2270726f6265223a22e2808c227d",
                    "7386d86c90f48784415b6f59fcf171bf95f6fd7710e68661bbb8a49ad82ed849")
            },
            new object[]
            {
                new Vector(
                    "V12",
                    Array.Empty<int>(),
                    "7b2270726f6265223a22227d",
                    "2c59ad8dd7bbce6c3bf974f4a5911ee9758a403f386bc058b373dda3daa6b3c4")
            }
        };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void U7_V1V12_GoldenVectorsMatchPinnedProfile(Vector vector)
    {
        // Build the probe string purely from codepoints. Never copy-paste the JSON column:
        // it contains invisible characters (e.g. ZWNJ) that would silently corrupt the input.
        var probe = string.Concat(vector.Codepoints.Select(cp => char.ConvertFromUtf32(cp)));

        // Build the input document by JSON-escaping the probe ourselves so it round-trips to
        // the exact probe chars, independent of any serializer defaults.
        var input = $"{{\"probe\":\"{EscapeJsonInput(probe)}\"}}";
        using var document = JsonDocument.Parse(input);
        var element = document.RootElement.Clone();

        var canonical = CanonicalJson.Serialize(element);
        var actualUtf8 = Convert.ToHexString(Encoding.UTF8.GetBytes(canonical)).ToLowerInvariant();
        var actualSha256 = CanonicalJson.Sha256Hex(element);

        Assert.Equal(vector.Utf8Hex, actualUtf8);
        Assert.Equal(vector.Sha256, actualSha256);
    }

    /// <summary>JSON-escapes a probe string for building a valid input document.</summary>
    /// <remarks>
    /// Escapes " and \ and control chars (U+0000..U+001F and DEL U+007F) as \u00XX uppercase;
    /// leaves non-ASCII raw UTF-8 and / literal. The exact input escaping is irrelevant as
    /// long as JsonDocument round-trips it to the original probe chars.
    /// </remarks>
    private static string EscapeJsonInput(string s)
    {
        var output = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '"')
            {
                output.Append("\\\"");
                continue;
            }

            if (c == '\\')
            {
                output.Append("\\\\");
                continue;
            }

            if (c <= 0x1F || c == 0x7F)
            {
                output.Append('\\');
                output.Append('u');
                output.Append(((int)c).ToString("X4"));
                continue;
            }

            output.Append(c);
        }

        return output.ToString();
    }
}
