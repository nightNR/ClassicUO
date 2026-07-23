// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace ClassicUO.Assets;

/// <summary>
/// Header fields describing an atlas font as a whole (no glyph data).
/// Used both to write a new atlas font file and to surface the parsed
/// header back out of <see cref="AtlasFontFile"/>.
/// </summary>
public struct AtlasFontHeader
{
    public int PixelSize;
    public int Ascent;
    public int Descent;
    public int LineHeight;
    public char FirstChar;
    public char LastChar;
}

/// <summary>
/// A single rasterized glyph: its placement offsets, cell size, advance
/// width, and coverage (alpha) bitmap of length Width*Height.
/// </summary>
public readonly struct AtlasGlyph
{
    public sbyte OffsetX { get; init; }
    public sbyte OffsetY { get; init; }
    public byte Width { get; init; }
    public byte Height { get; init; }
    public short Advance { get; init; }
    public byte[] Coverage { get; init; }
}

/// <summary>
/// Reader/writer for the "UOAF" atlas font binary format. Pure serialization:
/// no dependency on FontsLoader or any graphics types.
/// </summary>
public sealed class AtlasFontFile
{
    private const ushort FormatVersion = 1;
    private static ReadOnlySpan<byte> Magic => "UOAF"u8;

    private const int HeaderSize =
        4 // magic
        + 2 // version
        + 2 // pixelSize
        + 2 // ascent
        + 2 // descent
        + 2 // lineHeight
        + 2 // firstChar
        + 2; // lastChar

    private const int GlyphFixedSize =
        1 // offsetX
        + 1 // offsetY
        + 1 // width
        + 1 // height
        + 2; // advance

    private readonly AtlasGlyph[] _glyphs;

    public int PixelSize { get; }
    public int Ascent { get; }
    public int Descent { get; }
    public int LineHeight { get; }
    public char FirstChar { get; }
    public char LastChar { get; }

    private AtlasFontFile(int pixelSize, int ascent, int descent, int lineHeight, char firstChar, char lastChar, AtlasGlyph[] glyphs)
    {
        PixelSize = pixelSize;
        Ascent = ascent;
        Descent = descent;
        LineHeight = lineHeight;
        FirstChar = firstChar;
        LastChar = lastChar;
        _glyphs = glyphs;
    }

    public bool TryGetGlyph(char c, out AtlasGlyph glyph)
    {
        if (c < FirstChar || c > LastChar)
        {
            glyph = default;
            return false;
        }

        glyph = _glyphs[c - FirstChar];
        return true;
    }

    public static AtlasFontFile Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            throw new ArgumentException("Buffer too small to contain an AtlasFontFile header.", nameof(bytes));
        }

        if (!bytes[..4].SequenceEqual(Magic))
        {
            throw new ArgumentException("Buffer does not start with the 'UOAF' magic.", nameof(bytes));
        }

        int offset = 4;

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        if (version != FormatVersion)
        {
            throw new NotSupportedException($"Unsupported AtlasFontFile version {version}.");
        }

        ushort pixelSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        short ascent = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        short descent = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        ushort lineHeight = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        ushort firstChar = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        ushort lastChar = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
        offset += 2;

        int glyphCount = lastChar - firstChar + 1;
        var glyphs = new AtlasGlyph[glyphCount];

        for (int i = 0; i < glyphCount; i++)
        {
            if (offset + GlyphFixedSize > bytes.Length)
            {
                throw new ArgumentException("Buffer truncated while reading glyph header.", nameof(bytes));
            }

            sbyte offsetX = (sbyte)bytes[offset];
            offset += 1;

            sbyte offsetY = (sbyte)bytes[offset];
            offset += 1;

            byte width = bytes[offset];
            offset += 1;

            byte height = bytes[offset];
            offset += 1;

            short advance = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, 2));
            offset += 2;

            int coverageLength = width * height;
            byte[] coverage;

            if (coverageLength == 0)
            {
                coverage = Array.Empty<byte>();
            }
            else
            {
                if (offset + coverageLength > bytes.Length)
                {
                    throw new ArgumentException("Buffer truncated while reading glyph coverage.", nameof(bytes));
                }

                coverage = bytes.Slice(offset, coverageLength).ToArray();
                offset += coverageLength;
            }

            glyphs[i] = new AtlasGlyph
            {
                OffsetX = offsetX,
                OffsetY = offsetY,
                Width = width,
                Height = height,
                Advance = advance,
                Coverage = coverage
            };
        }

        return new AtlasFontFile(pixelSize, ascent, descent, lineHeight, (char)firstChar, (char)lastChar, glyphs);
    }

    public static byte[] Write(AtlasFontHeader header, IReadOnlyList<AtlasGlyph> glyphs)
    {
        int expectedCount = header.LastChar - header.FirstChar + 1;

        if (glyphs.Count != expectedCount)
        {
            throw new ArgumentException(
                $"Expected {expectedCount} glyphs for range '{header.FirstChar}'..'{header.LastChar}', got {glyphs.Count}.",
                nameof(glyphs));
        }

        int totalSize = HeaderSize;

        for (int i = 0; i < glyphs.Count; i++)
        {
            totalSize += GlyphFixedSize + glyphs[i].Width * glyphs[i].Height;
        }

        byte[] buffer = new byte[totalSize];
        Span<byte> span = buffer;

        Magic.CopyTo(span);
        int offset = 4;

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), FormatVersion);
        offset += 2;

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)header.PixelSize);
        offset += 2;

        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), (short)header.Ascent);
        offset += 2;

        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), (short)header.Descent);
        offset += 2;

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), (ushort)header.LineHeight);
        offset += 2;

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), header.FirstChar);
        offset += 2;

        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), header.LastChar);
        offset += 2;

        for (int i = 0; i < glyphs.Count; i++)
        {
            AtlasGlyph g = glyphs[i];

            span[offset] = unchecked((byte)g.OffsetX);
            offset += 1;

            span[offset] = unchecked((byte)g.OffsetY);
            offset += 1;

            span[offset] = g.Width;
            offset += 1;

            span[offset] = g.Height;
            offset += 1;

            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(offset, 2), g.Advance);
            offset += 2;

            int coverageLength = g.Width * g.Height;

            if (coverageLength != 0)
            {
                g.Coverage.AsSpan(0, coverageLength).CopyTo(span.Slice(offset, coverageLength));
                offset += coverageLength;
            }
        }

        return buffer;
    }
}
