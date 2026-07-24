// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Globalization;

namespace ClassicUO.Assets
{
    public enum AssetOverrideKind
    {
        Gump,
        Art,
        Land,
        Texmap,
        Light,
    }

    public sealed partial class AssetOverrides
    {
        public bool Enabled { get; private set; }

        public static bool TryParseId(string stem, out uint id)
        {
            id = 0;

            if (string.IsNullOrEmpty(stem))
                return false;

            if (stem.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(
                    stem.AsSpan(2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out id
                );
            }

            return uint.TryParse(
                stem,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out id
            );
        }
    }
}
