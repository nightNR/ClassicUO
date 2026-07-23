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

        // Baked TTF glyph atlases for the in-game unicode font picker (synthetic
        // slots 20-31, see AtlasFontRegistry below + FontsLoader.RegisterAtlasFont).
        // Unlike the CUSTOM_LOGIN_SCENE-gated TTFs above, these are always embedded:
        // in-game font rendering doesn't depend on the custom login scene feature.
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cinzel-14.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCinzel14();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cinzel-18.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCinzel18();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cinzel-22.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCinzel22();

        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cormorant-14.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCormorant14();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cormorant-18.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCormorant18();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Cormorant-22.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasCormorant22();

        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Inter-14.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasInter14();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Inter-18.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasInter18();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/Inter-22.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasInter22();

        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/SourceSans-14.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasSourceSans14();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/SourceSans-18.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasSourceSans18();
        [FileEmbed.FileEmbed("../../Assets/fonts/atlas/SourceSans-22.uofont")]
        public static partial ReadOnlySpan<byte> GetAtlasSourceSans22();
    }

    /// <summary>
    /// Single source of truth for the slot &lt;-&gt; (display name, embedded atlas bytes)
    /// mapping for the 12 baked TTF atlas fonts that populate synthetic unicode font
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

            public Entry(int slot, string displayName)
            {
                Slot = slot;
                DisplayName = displayName;
            }
        }

        /// <summary>
        /// Fixed slot order: 20-22 Cinzel, 23-25 Cormorant, 26-28 Inter, 29-31 SourceSans,
        /// each family ascending 14/18/22.
        /// </summary>
        public static readonly Entry[] Entries =
        {
            new Entry(20, "Cinzel 14"),
            new Entry(21, "Cinzel 18"),
            new Entry(22, "Cinzel 22"),
            new Entry(23, "Cormorant 14"),
            new Entry(24, "Cormorant 18"),
            new Entry(25, "Cormorant 22"),
            new Entry(26, "Inter 14"),
            new Entry(27, "Inter 18"),
            new Entry(28, "Inter 22"),
            new Entry(29, "SourceSans 14"),
            new Entry(30, "SourceSans 18"),
            new Entry(31, "SourceSans 22"),
        };

        /// <summary>
        /// Returns the embedded atlas bytes for one of the slots in <see cref="Entries"/>.
        /// A switch (rather than a Func-based table) because ReadOnlySpan&lt;byte&gt; is a
        /// ref struct and can't be a generic type argument or captured in a delegate.
        /// </summary>
        public static ReadOnlySpan<byte> GetAtlasBytes(int slot)
        {
            switch (slot)
            {
                case 20: return Loader.GetAtlasCinzel14();
                case 21: return Loader.GetAtlasCinzel18();
                case 22: return Loader.GetAtlasCinzel22();
                case 23: return Loader.GetAtlasCormorant14();
                case 24: return Loader.GetAtlasCormorant18();
                case 25: return Loader.GetAtlasCormorant22();
                case 26: return Loader.GetAtlasInter14();
                case 27: return Loader.GetAtlasInter18();
                case 28: return Loader.GetAtlasInter22();
                case 29: return Loader.GetAtlasSourceSans14();
                case 30: return Loader.GetAtlasSourceSans18();
                case 31: return Loader.GetAtlasSourceSans22();
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, "Not a registered atlas font slot.");
            }
        }

        /// <summary>
        /// Registers all 12 baked atlases into <paramref name="fonts"/> in the fixed
        /// order defined by <see cref="Entries"/>. Call exactly once, after
        /// <c>FontsLoader.Load()</c> has run and before any gump renders text.
        /// </summary>
        public static void RegisterAll(ClassicUO.Assets.FontsLoader fonts)
        {
            foreach (Entry entry in Entries)
            {
                fonts.RegisterAtlasFont(entry.Slot, entry.DisplayName, GetAtlasBytes(entry.Slot));
            }
        }
    }
}
