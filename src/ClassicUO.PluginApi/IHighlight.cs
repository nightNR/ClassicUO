// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace ClassicUO.PluginApi;

/// <summary>What an area highlight's center follows.</summary>
public enum HighlightSnap { Mouse, Position, Serial }

/// <summary>Object categories an area highlight can tint. Combine with |.</summary>
[Flags]
public enum HighlightObjectTypes
{
    None = 0,
    Mobile = 1 << 0,
    Item = 1 << 1,
    Corpse = 1 << 2,
    Land = 1 << 3,
    Static = 1 << 4,
    Multi = 1 << 5,
    All = Mobile | Item | Corpse | Land | Static | Multi
}

/// <summary>
/// Plugin-driven object/area highlighting (OrionUO parity). Character
/// highlighting persists until explicitly removed (no timer, matching
/// AddHighlightCharacter). Area highlighting supports a duration and follows
/// the mouse, a fixed position, or a serial. All mutating methods auto-marshal
/// to the game thread; <see cref="GetAreaTimer"/> is a direct synchronous read.
/// </summary>
public interface IHighlight
{
    /// <summary>
    /// Adds or replaces the area highlight <paramref name="id"/>. When two
    /// areas overlap the same tile, the most recently added one wins.
    /// </summary>
    /// <param name="id">Area id; re-adding the same id replaces it.</param>
    /// <param name="durationMs">Lifetime in ms; -1 (default) never expires.</param>
    /// <param name="snap">What the area's center follows.</param>
    /// <param name="anchorSerial">Used when <paramref name="snap"/> is Serial.</param>
    /// <param name="hue">Highlight hue-table index.</param>
    /// <param name="rangeX">Half-width of the area along X.</param>
    /// <param name="rangeY">Half-height of the area along Y.</param>
    /// <param name="objectTypes">Which object categories this area tints.</param>
    /// <param name="x">Used when <paramref name="snap"/> is Position.</param>
    /// <param name="y">Used when <paramref name="snap"/> is Position.</param>
    void AddArea(
        string id,
        int durationMs = -1,
        HighlightSnap snap = HighlightSnap.Mouse,
        uint anchorSerial = 0,
        ushort hue = 0x0386,
        int rangeX = 3,
        int rangeY = 3,
        HighlightObjectTypes objectTypes = HighlightObjectTypes.All,
        int x = 0,
        int y = 0
    );

    /// <summary>Removes the area highlight with <paramref name="id"/> if present.</summary>
    void RemoveArea(string id);

    /// <summary>Removes every area highlight owned by this plugin.</summary>
    void ClearAreas();

    /// <summary>
    /// Remaining lifetime in ms for area <paramref name="id"/>. Returns 0 if the
    /// id doesn't exist or has expired; <see cref="int.MaxValue"/> if it never expires.
    /// </summary>
    int GetAreaTimer(string id);

    /// <summary>
    /// Tints mobile <paramref name="serial"/> with <paramref name="hue"/>.
    /// <paramref name="priorityHighlight"/> true always wins over the client's
    /// own status coloring (poison/paralyze/invul/attacked/notoriety); false
    /// loses to an active status color but wins over the default hue.
    /// </summary>
    void AddCharacter(uint serial, ushort hue, bool priorityHighlight = false);

    /// <summary>Removes the character highlight for <paramref name="serial"/> in the given tier.</summary>
    void RemoveCharacter(uint serial, bool priorityHighlight = false);

    /// <summary>Removes every character highlight this plugin owns in the given tier.</summary>
    void ClearCharacters(bool priorityHighlight = false);
}
