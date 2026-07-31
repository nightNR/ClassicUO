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
    /// Single source of truth for the slot &lt;-&gt; (display name, TTF face, pixel size)
    /// mapping for the 12 live-rasterized TTF fonts that populate synthetic unicode font
    /// slots 20-31. Both the font-init registration (<see cref="ClassicUO.UltimaOnline"/>'s
    /// LoadUOFiles, which calls <see cref="RegisterAll"/> right after
    /// <c>FileManager.Load(...)</c>) and the font picker UI must read this table rather
    /// than re-deriving slot numbers/names, so they cannot drift apart.
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

        /// <summary>
        /// Registers all 12 live TTF slots into <paramref name="fonts"/> in the fixed
        /// order defined by <see cref="Entries"/>. Call exactly once, after
        /// <c>FontsLoader.Load()</c> has run and before any gump renders text.
        /// </summary>
        public static void RegisterAll(ClassicUO.Assets.FontsLoader fonts)
        {
            foreach (Entry entry in Entries)
            {
                byte[] ttf = LoadTtf(entry.TtfResource);
                fonts.RegisterAtlasFont(entry.Slot, entry.DisplayName, ttf, entry.PixelSize);
            }

            // Build-freshness marker: the token below is bumped whenever the TTF
            // render path changes, so the console line proves WHICH build is
            // running. "solidify+3x-oversample+tint" == 3x oversampling + coverage
            // solidify (opaque interiors) + draw-time grayscale tint. An older
            // string means a stale binary is running, not the current code.
            ClassicUO.Utility.Logging.Log.Trace(
                $"AtlasFontRegistry: registered {Entries.Length} TTF font slots (live StbTrueType rasterization) [path=RAW+color+weight+border2-aa-dilate]");
        }
    }
}
