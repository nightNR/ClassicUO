// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;

namespace ClassicUO.Configuration
{
    // Face null/empty => Classic (use the location's classic fallback).
    public struct FontChoice
    {
        public string Face { get; set; }
        public byte Size { get; set; }
    }

    public static class FontLocation
    {
        public const string Chat = "chat";
        public const string Tooltip = "tooltip";
        public const string JournalControls = "journal_controls";
    }

    public readonly struct LocationDef
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly byte ClassicFont;
        public readonly bool ClassicUnicode;

        public LocationDef(string id, string displayName, byte classicFont, bool classicUnicode)
        {
            Id = id;
            DisplayName = displayName;
            ClassicFont = classicFont;
            ClassicUnicode = classicUnicode;
        }
    }

    public static class FontLocationCatalog
    {
        // Grows one entry per per-location increment. ClassicFont/ClassicUnicode
        // must equal the location's current hardcoded literal so Classic = zero change.
        private static readonly LocationDef[] _locations =
        {
            new LocationDef(FontLocation.Chat,            "Chat",             1, true),
            new LocationDef(FontLocation.Tooltip,         "Tooltip",          1, true),
            new LocationDef(FontLocation.JournalControls, "Journal controls", 6, false),
        };

        public static IReadOnlyList<LocationDef> Locations => _locations;

        public static bool TryGet(string id, out LocationDef def)
        {
            foreach (LocationDef d in _locations)
            {
                if (d.Id == id)
                {
                    def = d;
                    return true;
                }
            }
            def = default;
            return false;
        }
    }
}
