// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClassicUO.Assets;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace FontAtlasBaker;

internal static class Program
{
    // Codepoint range baked into every atlas: ASCII printable + Latin-1
    // Supplement + Latin Extended-A. Covers English plus Slovak/Czech
    // diacritics (á ä č ď ě é í ĺ ľ ň ó ô ŕ ř š ť ú ů ý ž and uppercase).
    private const char FirstChar = (char)0x20;
    private const char LastChar = (char)0x17F;

    private static readonly int[] Sizes = { 14, 18, 22 };

    // Slovak/Czech letters that must never be silently missing in any
    // baked family, display fonts included.
    private static readonly char[] SkCzLetters =
        "áäčďěéíĺľňóôŕřšťúůýžÁÄČĎĚÉÍĹĽŇÓÔŔŘŠŤÚŮÝŽ".ToCharArray();

    private static readonly (string Family, string RelativeTtfPath)[] Families =
    {
        ("Cinzel", "Assets/fonts/Cinzel/static/Cinzel-SemiBold.ttf"),
        ("Cormorant", "Assets/fonts/Cormorant_Garamond/static/CormorantGaramond-SemiBold.ttf"),
        ("Inter", "Assets/fonts/Inter/static/Inter_18pt-Regular.ttf"),
        ("SourceSans", "Assets/fonts/Source_Sans_3/static/SourceSans3-Regular.ttf"),
    };

    private static int Main()
    {
        string repoRoot = FindRepoRoot();
        string outputDir = Path.Combine(repoRoot, "Assets", "fonts", "atlas");
        Directory.CreateDirectory(outputDir);

        var missingByFamily = new Dictionary<string, int>();
        var skCzMissingByFamily = new Dictionary<string, List<string>>();
        bool hardFailure = false;

        foreach ((string family, string relativeTtfPath) in Families)
        {
            string ttfPath = Path.Combine(repoRoot, relativeTtfPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(ttfPath))
            {
                Console.Error.WriteLine($"ERROR: TTF source not found for {family}: {ttfPath}");
                hardFailure = true;
                continue;
            }

            byte[] ttfBytes = File.ReadAllBytes(ttfPath);

            int familyMissing = 0;
            var familySkCzMissing = new List<string>();

            foreach (int size in Sizes)
            {
                (byte[] fileBytes, int glyphCount, int missingCount, List<string> skCzMissing) =
                    BakeFont(ttfBytes, family, size);

                familyMissing += missingCount;
                familySkCzMissing.AddRange(skCzMissing);

                string outPath = Path.Combine(outputDir, $"{family}-{size}.uofont");
                File.WriteAllBytes(outPath, fileBytes);

                Console.WriteLine(
                    $"{family}-{size}.uofont: {fileBytes.Length,7} bytes, {glyphCount} glyphs, {missingCount} missing");
            }

            missingByFamily[family] = familyMissing;
            skCzMissingByFamily[family] = familySkCzMissing;

            if (familySkCzMissing.Count > 0)
            {
                hardFailure = true;
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Missing-glyph summary ===");

        foreach ((string family, _) in Families)
        {
            int missing = missingByFamily.TryGetValue(family, out int m) ? m : 0;
            List<string> skCzMissing = skCzMissingByFamily.TryGetValue(family, out var l) ? l : new List<string>();

            string skCzText = skCzMissing.Count == 0
                ? "none"
                : string.Join(" ", skCzMissing);

            Console.WriteLine($"  {family,-11} total-missing-glyph-instances={missing,4}  SK/CZ-missing={skCzText}");
        }

        if (hardFailure)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "HARD ERROR: one or more required Slovak/Czech letters (or a TTF source file) is missing. See above.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Done.");
        return 0;
    }

    private static unsafe (byte[] FileBytes, int GlyphCount, int MissingCount, List<string> SkCzMissing) BakeFont(
        byte[] ttfBytes,
        string family,
        int size)
    {
        stbtt_fontinfo? info = CreateFont(ttfBytes, 0);

        if (info == null)
        {
            throw new InvalidOperationException($"stbtt_InitFont failed for family '{family}' (corrupt/unsupported TTF).");
        }

        float scale = stbtt_ScaleForPixelHeight(info, size);

        int ascentRaw, descentRaw, lineGapRaw;
        stbtt_GetFontVMetrics(info, &ascentRaw, &descentRaw, &lineGapRaw);

        int ascentPx = (int)MathF.Round(ascentRaw * scale);
        int descentPx = (int)MathF.Round(descentRaw * scale);
        int lineHeight = (int)MathF.Round((ascentRaw - descentRaw + lineGapRaw) * scale);

        int glyphCount = LastChar - FirstChar + 1;
        var glyphs = new AtlasGlyph[glyphCount];

        int missingCount = 0;
        var skCzMissing = new List<string>();

        for (char c = FirstChar; c <= LastChar; c++)
        {
            int index = c - FirstChar;
            int codepoint = c;

            int glyphIndex = stbtt_FindGlyphIndex(info, codepoint);

            if (glyphIndex == 0)
            {
                Console.Error.WriteLine(
                    $"MISSING {family} U+{codepoint:X4} ({DescribeChar(c)})");
                missingCount++;

                if (Array.IndexOf(SkCzLetters, c) >= 0)
                {
                    skCzMissing.Add($"U+{codepoint:X4}({c})");
                }

                int fallbackAdvance = 0;
                int fallbackLsb;
                stbtt_GetCodepointHMetrics(info, codepoint, &fallbackAdvance, &fallbackLsb);

                glyphs[index] = new AtlasGlyph
                {
                    OffsetX = 0,
                    OffsetY = 0,
                    Width = 0,
                    Height = 0,
                    Advance = (short)fallbackAdvance,
                    Coverage = Array.Empty<byte>(),
                };

                continue;
            }

            int advanceWidth, lsb;
            stbtt_GetCodepointHMetrics(info, codepoint, &advanceWidth, &lsb);
            int advancePx = (int)MathF.Round(advanceWidth * scale);

            int w, h, xoff, yoff;
            byte* bitmap = stbtt_GetCodepointBitmap(info, scale, scale, codepoint, &w, &h, &xoff, &yoff);

            byte[] coverage;

            if (bitmap == null || w <= 0 || h <= 0)
            {
                coverage = Array.Empty<byte>();
                w = 0;
                h = 0;
            }
            else
            {
                coverage = new byte[w * h];

                fixed (byte* pCoverage = coverage)
                {
                    Buffer.MemoryCopy(bitmap, pCoverage, coverage.Length, coverage.Length);
                }

                stbtt_FreeBitmap(bitmap, null);
            }

            int offsetYRaw = ascentPx + yoff;

            glyphs[index] = new AtlasGlyph
            {
                OffsetX = ClampToSByte(xoff),
                OffsetY = ClampToSByte(offsetYRaw),
                Width = (byte)w,
                Height = (byte)h,
                Advance = (short)advancePx,
                Coverage = coverage,
            };
        }

        var header = new AtlasFontHeader
        {
            PixelSize = size,
            Ascent = ascentPx,
            Descent = descentPx,
            LineHeight = lineHeight,
            FirstChar = FirstChar,
            LastChar = LastChar,
        };

        byte[] fileBytes = AtlasFontFile.Write(header, glyphs);

        return (fileBytes, glyphCount, missingCount, skCzMissing);
    }

    private static sbyte ClampToSByte(int value)
    {
        if (value < sbyte.MinValue)
        {
            return sbyte.MinValue;
        }

        if (value > sbyte.MaxValue)
        {
            return sbyte.MaxValue;
        }

        return (sbyte)value;
    }

    private static string DescribeChar(char c)
    {
        return char.IsControl(c) ? "<ctrl>" : c.ToString(CultureInfo.InvariantCulture);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClassicUO.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (ClassicUO.sln) starting from '{AppContext.BaseDirectory}'.");
    }
}
