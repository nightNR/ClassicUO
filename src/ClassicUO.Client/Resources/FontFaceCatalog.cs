// SPDX-License-Identifier: BSD-2-Clause
using System.Collections.Generic;

namespace ClassicUO.Resources
{
    public readonly struct FontFace
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string TtfResource;

        public FontFace(string id, string displayName, string ttfResource)
        {
            Id = id;
            DisplayName = displayName;
            TtfResource = ttfResource;
        }
    }

    public static class FontFaceCatalog
    {
        private static readonly FontFace[] _faces =
        {
            new FontFace("cinzel",        "Cinzel",          "Cinzel.ttf"),
            new FontFace("cormorant",     "Cormorant",       "Cormorant.ttf"),
            new FontFace("inter",         "Inter",           "Inter.ttf"),
            new FontFace("interbold",     "Inter Bold",      "InterBold.ttf"),
            new FontFace("sourcesans",    "Source Sans",     "SourceSans.ttf"),
            new FontFace("clfuoclassic",  "CLF-UoClassic",   "CLFUoClassic.ttf"),
            new FontFace("imfellenglish", "IM Fell English", "IMFellEnglish.ttf"),
            new FontFace("medievalsharp", "MedievalSharp",   "MedievalSharp.ttf"),
        };

        public static IReadOnlyList<FontFace> Faces => _faces;

        public static bool TryGet(string faceId, out FontFace face)
        {
            foreach (FontFace f in _faces)
            {
                if (f.Id == faceId)
                {
                    face = f;
                    return true;
                }
            }
            face = default;
            return false;
        }

        public static byte[] GetBytes(string faceId)
        {
            return TryGet(faceId, out FontFace face) ? AtlasFontRegistry.LoadTtf(face.TtfResource) : null;
        }
    }
}
