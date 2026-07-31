using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassicUO.Resources
{
    public partial class Loader
    {
        [FileEmbed.FileEmbed("cuologo.png")]
        public static partial ReadOnlySpan<byte> GetCuoLogo();

        [FileEmbed.FileEmbed("game-background.png")]
        public static partial ReadOnlySpan<byte> GetBackgroundImage();

#if CUSTOM_LOGIN_SCENE
        // Forward slashes: valid path separator on Windows AND Linux (the FileEmbed generator resolves
        // these relative to the project dir). Backslashes are Windows-only and break the Linux build.
        [FileEmbed.FileEmbed("../../Assets/background-image.png")]
        public static partial ReadOnlySpan<byte> GetLoginBackground();

        [FileEmbed.FileEmbed("../../Assets/background-character-select.png")]
        public static partial ReadOnlySpan<byte> GetCharSelectBackground();

        // Buttons: dark/secondary style
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/19-secondary-neutral.png")]
        public static partial ReadOnlySpan<byte> GetButtonNeutral();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/20-secondary-hover.png")]
        public static partial ReadOnlySpan<byte> GetButtonHover();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/21-secondary-pressed.png")]
        public static partial ReadOnlySpan<byte> GetButtonDown();

        // Buttons: priority/primary (red) style
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/16-primary-neutral.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioNeutral();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/17-primary-hover.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioHover();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/18-primary-pressed.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioMDown();

        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/13-input-neutral.png")]
        public static partial ReadOnlySpan<byte> GetTextInputNeutral();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/14-input-focus.png")]
        public static partial ReadOnlySpan<byte> GetTextInputFocus();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/15-input-disabled.png")]
        public static partial ReadOnlySpan<byte> GetTextInputDisabled();

        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/02-top-left-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerTL();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/03-top-right-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerTR();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/04-bottom-left-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerBL();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/05-bottom-right-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerBR();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/06-left-repeat-side.png")]
        public static partial ReadOnlySpan<byte> GetFrameSideL();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/07-right-repeat-side.png")]
        public static partial ReadOnlySpan<byte> GetFrameSideR();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/12-center-fill.png")]
        public static partial ReadOnlySpan<byte> GetFrameCenter();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/10-top-center-ornament.png")]
        public static partial ReadOnlySpan<byte> GetFrameTopOrn();
        [FileEmbed.FileEmbed("../../Assets/gothic-login-ui/11-bottom-center-ornament.png")]
        public static partial ReadOnlySpan<byte> GetFrameBottomOrn();

        [FileEmbed.FileEmbed("../../Assets/fonts/Cinzel/static/Cinzel-SemiBold.ttf")]
        public static partial ReadOnlySpan<byte> GetCinzelSemiBold();
        [FileEmbed.FileEmbed("../../Assets/fonts/Cormorant_Garamond/static/CormorantGaramond-SemiBold.ttf")]
        public static partial ReadOnlySpan<byte> GetCormorantSemiBold();
#endif
    }

    /// <summary>
    /// Dynamic on-demand registry mapping (faceId, size) combos to unicode
    /// font slots in the dynamic pool (<see cref="AtlasFontRegistry.DynamicSlotFirst"/>-
    /// <see cref="AtlasFontRegistry.DynamicSlotLast"/>). Nothing is registered up
    /// front: <see cref="Attach"/> is called once at boot (right after
    /// <c>FileManager.Load(...)</c>), and slots are allocated + rasterized lazily
    /// the first time <see cref="TryResolve"/> is asked for a given combo.
    /// <see cref="Reset"/> frees every dynamic slot on profile activation. The
    /// private <see cref="Entry"/>/<see cref="Entries"/> table below is kept only
    /// as a legacy lookup for <see cref="LegacySlotToFaceSize"/>, mapping the old
    /// fixed slots 20-40 to their (faceId, size) for profile migration.
    /// </summary>
    public static class AtlasFontRegistry
    {
        public readonly struct Entry
        {
            public readonly int Slot;
            public readonly string DisplayName;
            public readonly string TtfResource;
            public readonly int PixelSize;

            public Entry(int slot, string displayName, string ttfResource, int pixelSize)
            {
                Slot = slot;
                DisplayName = displayName;
                TtfResource = ttfResource;
                PixelSize = pixelSize;
            }
        }

        /// <summary>
        /// Fixed slot order: 20-22 Cinzel, 23-25 Cormorant, 26-28 Inter, 29-31 SourceSans,
        /// 32-34 CLF-UoClassic, 35-37 IM Fell English, 38-40 MedievalSharp,
        /// each family ascending 14/18/22. Slot count must stay below
        /// FontsLoader.MAX_UNICODE_FONTS.
        /// </summary>
        public static readonly Entry[] Entries =
        {
            new Entry(20, "Cinzel 14", "Cinzel.ttf", 14),
            new Entry(21, "Cinzel 18", "Cinzel.ttf", 18),
            new Entry(22, "Cinzel 22", "Cinzel.ttf", 22),
            new Entry(23, "Cormorant 14", "Cormorant.ttf", 14),
            new Entry(24, "Cormorant 18", "Cormorant.ttf", 18),
            new Entry(25, "Cormorant 22", "Cormorant.ttf", 22),
            new Entry(26, "Inter 14", "Inter.ttf", 14),
            new Entry(27, "Inter 18", "Inter.ttf", 18),
            new Entry(28, "Inter 22", "Inter.ttf", 22),
            new Entry(29, "SourceSans 14", "SourceSans.ttf", 14),
            new Entry(30, "SourceSans 18", "SourceSans.ttf", 18),
            new Entry(31, "SourceSans 22", "SourceSans.ttf", 22),
            new Entry(32, "CLF-UoClassic 14", "CLFUoClassic.ttf", 14),
            new Entry(33, "CLF-UoClassic 18", "CLFUoClassic.ttf", 18),
            new Entry(34, "CLF-UoClassic 22", "CLFUoClassic.ttf", 22),
            new Entry(35, "IM Fell English 14", "IMFellEnglish.ttf", 14),
            new Entry(36, "IM Fell English 18", "IMFellEnglish.ttf", 18),
            new Entry(37, "IM Fell English 22", "IMFellEnglish.ttf", 22),
            new Entry(38, "MedievalSharp 14", "MedievalSharp.ttf", 14),
            new Entry(39, "MedievalSharp 18", "MedievalSharp.ttf", 18),
            new Entry(40, "MedievalSharp 22", "MedievalSharp.ttf", 22),
        };

        private static readonly Dictionary<string, byte[]> _ttfCache = new Dictionary<string, byte[]>();

        /// <summary>
        /// Loads (and caches) the embedded TTF bytes for <paramref name="logicalName"/>,
        /// so each face is read from the manifest once and reused across its three
        /// registered pixel sizes rather than re-read per slot.
        /// </summary>
        internal static byte[] LoadTtf(string logicalName)
        {
            if (_ttfCache.TryGetValue(logicalName, out byte[] cached))
            {
                return cached;
            }

            using System.IO.Stream s = typeof(AtlasFontRegistry).Assembly
                .GetManifestResourceStream(logicalName)
                ?? throw new InvalidOperationException($"Embedded TTF '{logicalName}' not found.");
            using var ms = new System.IO.MemoryStream();
            s.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            _ttfCache[logicalName] = bytes;
            return bytes;
        }

        // --- Dynamic on-demand slot pool -----------------------------------
        //
        // Slots 20-95 (FontsLoader.MAX_UNICODE_FONTS == 96) are no longer
        // registered up front. A (faceId, size) combo is only rasterized and
        // bound to a slot the first time something asks for it via
        // TryResolve; Reset() releases every dynamic slot (called whenever a
        // profile is (re)activated) so stale per-profile fonts don't linger
        // across character/profile switches.

        public const int DynamicSlotFirst = 20;
        public const int DynamicSlotLast = 95;

        private static ClassicUO.Assets.FontsLoader _fonts;
        private static readonly Dictionary<(string face, byte size), byte> _map = new();
        private static byte _next = DynamicSlotFirst;

        /// <summary>
        /// Stores the loader that dynamic slots will be registered against and
        /// clears any previously-tracked state. Call once at boot, right after
        /// <c>FontsLoader.Load()</c> has run.
        /// </summary>
        public static void Attach(ClassicUO.Assets.FontsLoader fonts)
        {
            _fonts = fonts;
            Reset();
        }

        /// <summary>
        /// Frees every dynamic slot currently in use and resets the allocation
        /// cursor back to <see cref="DynamicSlotFirst"/>. Call on profile
        /// activation so each profile starts from a clean slot pool.
        /// </summary>
        public static void Reset()
        {
            if (_fonts != null)
            {
                foreach (byte slot in _map.Values)
                {
                    _fonts.ResetAtlasFont(slot);
                }
            }
            _map.Clear();
            _next = DynamicSlotFirst;
        }

        /// <summary>
        /// Resolves a (faceId, size) combo to a unicode font slot, allocating
        /// and registering a new dynamic slot on first use. Returns false for
        /// an unknown faceId, a corrupt TTF, or an exhausted slot pool.
        /// </summary>
        public static bool TryResolve(string faceId, byte size, out byte slot)
        {
            slot = 0;
            if (_fonts == null || string.IsNullOrEmpty(faceId))
            {
                return false;
            }

            if (size < 8) size = 8;
            if (size > 32) size = 32;

            var key = (faceId, size);
            if (_map.TryGetValue(key, out slot))
            {
                return true;
            }

            byte[] bytes = FontFaceCatalog.GetBytes(faceId);
            if (bytes == null)
            {
                return false;                      // unknown face
            }

            if (_next > DynamicSlotLast)
            {
                Utility.Logging.Log.Warn($"AtlasFontRegistry: dynamic slot pool exhausted; '{faceId}' {size}px falls back to Classic. Restart to reclaim.");
                return false;                      // pool exhausted
            }

            byte candidate = _next;
            _fonts.RegisterAtlasFont(candidate, $"{faceId} {size}", bytes, size);
            if (!_fonts.IsAtlasFont(candidate))
            {
                return false;                      // corrupt TTF; slot left unusable
            }

            _map[key] = candidate;
            _next++;
            slot = candidate;
            return true;
        }

        /// <summary>
        /// Maps an old fixed slot (20-40, from the pre-dynamic-registry
        /// layout) to the (faceId, size) it used to represent, for profile
        /// migration. Returns null outside that range.
        /// </summary>
        public static (string face, byte size)? LegacySlotToFaceSize(byte legacySlot)
        {
            foreach (Entry e in Entries)
            {
                if (e.Slot == legacySlot)
                {
                    // e.DisplayName is like "Inter 18"; map to faceId + size.
                    int sp = e.DisplayName.LastIndexOf(' ');
                    string face = LegacyDisplayToFaceId(e.DisplayName.Substring(0, sp));
                    byte size = byte.Parse(e.DisplayName.Substring(sp + 1));
                    return (face, size);
                }
            }
            return null;
        }

        /// <summary>
        /// Helper for Task 4's profile migration: the faceId string for an old
        /// fixed legacy slot, or null if the slot isn't in the legacy range.
        /// </summary>
        public static string LegacyFaceId(byte legacySlot) => LegacySlotToFaceSize(legacySlot)?.face;

        private static string LegacyDisplayToFaceId(string display) => display switch
        {
            "Cinzel" => "cinzel",
            "Cormorant" => "cormorant",
            "Inter" => "inter",
            "SourceSans" => "sourcesans",
            "CLF-UoClassic" => "clfuoclassic",
            "IM Fell English" => "imfellenglish",
            "MedievalSharp" => "medievalsharp",
            _ => "",
        };
    }
}
