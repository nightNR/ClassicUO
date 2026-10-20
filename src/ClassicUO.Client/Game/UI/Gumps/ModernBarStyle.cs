// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    /// Snapshot of the profile's modern status-bar settings, plus the pure layout and color helpers
    /// the gump uses. Kept free of rendering so it can be unit tested.
    /// </summary>
    internal readonly struct ModernBarStyle
    {
        public const int MinScalePercent = 50;
        public const int MaxScalePercent = 300;

        // Layout in unscaled units; every value is multiplied by Scale and rounded.
        public const int BaseWidth = 200;
        public const int HeaderHeight = 30;
        public const int RowHeight = 26;
        public const int BodyPadding = 4;
        public const int Padding = 6;
        public const int Radius = 6;
        public const int BorderThickness = 1;
        public const int IconSize = 12;
        public const int TargetArrowSize = 26;
        public const int BarHeight = 22;
        public const int DistanceZoneWidth = 28;
        public const int NameFontSize = 20;
        public const int ValueFontSize = 18;

        // Muted dark-medieval palette: aged bronze, tarnished gold, dull steel, dried blood.
        public const uint DefaultBackground = 0x0B1620;
        public const uint DefaultBorder = 0x2F6DB5;
        public const uint DefaultHeader = 0x0F2A47;
        public const uint DefaultTrack = 0x05090E;
        public const uint DefaultHp = 0x9E302B;
        public const uint DefaultMana = 0x3366B0;
        public const uint DefaultStamina = 0xA68428;
        public const uint DefaultPoison = 0x5E8E40;
        public const uint DefaultMortal = 0xB06A28;
        public const uint DefaultAlert = 0xE5484D;

        /// <summary>Bump when the default palette changes; older profiles are reset to it once.</summary>
        public const int PaletteVersion = 4;

        public static void MigratePalette(Profile p)
        {
            if (p == null || p.ModernBarPaletteVersion >= PaletteVersion)
            {
                return;
            }

            p.ModernBarBackground = DefaultBackground;
            p.ModernBarBorder = DefaultBorder;
            p.ModernBarHeader = DefaultHeader;
            p.ModernBarTrack = DefaultTrack;
            p.ModernBarHp = DefaultHp;
            p.ModernBarMana = DefaultMana;
            p.ModernBarStamina = DefaultStamina;
            p.ModernBarPoison = DefaultPoison;
            p.ModernBarMortal = DefaultMortal;
            p.ModernBarAlert = DefaultAlert;
            p.ModernBarPaletteVersion = PaletteVersion;
        }

        /// <summary>The slider's 100% is this fraction of the raw layout units.</summary>
        public const float ScaleBase = 0.6f;

        public readonly float Scale;
        public readonly bool ShowValues;
        public readonly bool NotorietyAccent;
        public readonly float BackgroundOpacity;
        public readonly Vector3 Background, Border, Header, Track, Hp, Mana, Stamina, Poison, Mortal, Alert;

        public ModernBarStyle(Profile p)
        {
            MigratePalette(p);
            Scale = ClampScale(p?.ModernBarScale ?? 100) / 100f * ScaleBase;
            ShowValues = p?.ModernBarShowValues ?? true;
            NotorietyAccent = p?.ModernBarNotorietyAccent ?? true;
            BackgroundOpacity = Math.Clamp(p?.ModernBarBackgroundOpacity ?? 90, 0, 100) / 100f;
            Background = Rgb(p?.ModernBarBackground ?? DefaultBackground);
            Border = Rgb(p?.ModernBarBorder ?? DefaultBorder);
            Header = Rgb(p?.ModernBarHeader ?? DefaultHeader);
            Track = Rgb(p?.ModernBarTrack ?? DefaultTrack);
            Hp = Rgb(p?.ModernBarHp ?? DefaultHp);
            Mana = Rgb(p?.ModernBarMana ?? DefaultMana);
            Stamina = Rgb(p?.ModernBarStamina ?? DefaultStamina);
            Poison = Rgb(p?.ModernBarPoison ?? DefaultPoison);
            Mortal = Rgb(p?.ModernBarMortal ?? DefaultMortal);
            Alert = Rgb(p?.ModernBarAlert ?? DefaultAlert);
        }

        public static int ClampScale(int percent) => Math.Clamp(percent, MinScalePercent, MaxScalePercent);

        public int S(int units) => Math.Max(1, (int)MathF.Round(units * Scale));

        public static Vector3 Rgb(uint rrggbb)
        {
            return new Vector3(((rrggbb >> 16) & 0xFF) / 255f, ((rrggbb >> 8) & 0xFF) / 255f, (rrggbb & 0xFF) / 255f);
        }

        public static Vector3 Mix(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, Math.Clamp(t, 0f, 1f));

        public int Width(bool showDistance) => S(BaseWidth) + (showDistance ? S(DistanceZoneWidth) : 0);

        public int Height(int rows) => S(HeaderHeight) + S(BodyPadding) * 2 + rows * S(RowHeight);

        /// <summary>Width in pixels of a bar fill for the given fraction; 0 only when the fraction is 0.</summary>
        public static int FillWidth(float fraction, int trackWidth, int minWidth)
        {
            if (fraction <= 0f || trackWidth <= 0)
            {
                return 0;
            }

            return Math.Clamp((int)MathF.Round(trackWidth * Math.Min(fraction, 1f)), Math.Min(minWidth, trackWidth), trackWidth);
        }

        /// <summary>
        /// Even-numbered pixel font size for a base size at the current scale, so a continuous
        /// scale slider only ever allocates a bounded set of font slots.
        /// </summary>
        /// <summary>Pixels to push TTF text down so its glyphs (not its line box) sit centered.</summary>
        public int TextNudge(int baseSize) => FontPixels(baseSize) / 8;

        public byte FontPixels(int baseSize)
        {
            int px = (int)MathF.Round(baseSize * Scale / 2f) * 2;

            return (byte)Math.Clamp(px, 8, 72);
        }
    }
}
