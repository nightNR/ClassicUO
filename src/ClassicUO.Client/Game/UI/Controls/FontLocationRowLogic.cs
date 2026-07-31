// SPDX-License-Identifier: BSD-2-Clause
using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Resources;

namespace ClassicUO.Game.UI.Controls
{
    // Pure face/size choice mapping for FontLocationRow. No graphics dependencies
    // so it can be unit-tested directly (mirrors GridContainerLayout). FontLocationRow
    // itself cannot be constructed in the unit-test host: its Combobox children build
    // a ResizePic/Label that resolve through the process-wide Client.Game.UO singleton
    // (Gumps.GetGump / FileManager.Fonts), which only the real FNA bootstrap populates.
    internal static class FontLocationRowLogic
    {
        public const int SIZE_MIN = 8;
        public const int SIZE_MAX = 32;

        public static string[] BuildFaceIds()
        {
            var ids = new List<string> { "" };
            foreach (FontFace f in FontFaceCatalog.Faces)
            {
                ids.Add(f.Id);
            }
            return ids.ToArray();
        }

        public static string[] BuildFaceLabels()
        {
            var labels = new List<string> { "Classic" };
            foreach (FontFace f in FontFaceCatalog.Faces)
            {
                labels.Add(f.DisplayName);
            }
            return labels.ToArray();
        }

        public static int ResolveFaceIndex(string[] faceIds, string currentFace)
        {
            if (!string.IsNullOrEmpty(currentFace))
            {
                int idx = Array.IndexOf(faceIds, currentFace);
                if (idx > 0)
                {
                    return idx;
                }
            }
            return 0;
        }

        public static int ResolveSizeIndex(byte currentSize)
        {
            return Math.Clamp((int)currentSize, SIZE_MIN, SIZE_MAX) - SIZE_MIN;
        }

        public static FontChoice BuildChoice(int faceIndex, int sizeIndex, string[] faceIds)
        {
            if (faceIndex <= 0)
            {
                return new FontChoice { Face = "", Size = 0 };
            }
            return new FontChoice { Face = faceIds[faceIndex], Size = (byte)(sizeIndex + SIZE_MIN) };
        }
    }
}
