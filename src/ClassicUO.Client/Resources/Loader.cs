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
        [FileEmbed.FileEmbed("..\\..\\Assets\\background-image.png")]
        public static partial ReadOnlySpan<byte> GetLoginBackground();

        [FileEmbed.FileEmbed("..\\..\\Assets\\background-character-select.png")]
        public static partial ReadOnlySpan<byte> GetCharSelectBackground();

        // Buttons: dark/secondary style
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\19-secondary-neutral.png")]
        public static partial ReadOnlySpan<byte> GetButtonNeutral();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\20-secondary-hover.png")]
        public static partial ReadOnlySpan<byte> GetButtonHover();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\21-secondary-pressed.png")]
        public static partial ReadOnlySpan<byte> GetButtonDown();

        // Buttons: priority/primary (red) style
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\16-primary-neutral.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioNeutral();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\17-primary-hover.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioHover();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\18-primary-pressed.png")]
        public static partial ReadOnlySpan<byte> GetButtonPrioMDown();

        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\13-input-neutral.png")]
        public static partial ReadOnlySpan<byte> GetTextInputNeutral();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\14-input-focus.png")]
        public static partial ReadOnlySpan<byte> GetTextInputFocus();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\15-input-disabled.png")]
        public static partial ReadOnlySpan<byte> GetTextInputDisabled();

        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\02-top-left-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerTL();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\03-top-right-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerTR();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\04-bottom-left-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerBL();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\05-bottom-right-corner.png")]
        public static partial ReadOnlySpan<byte> GetFrameCornerBR();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\06-left-repeat-side.png")]
        public static partial ReadOnlySpan<byte> GetFrameSideL();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\07-right-repeat-side.png")]
        public static partial ReadOnlySpan<byte> GetFrameSideR();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\12-center-fill.png")]
        public static partial ReadOnlySpan<byte> GetFrameCenter();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\10-top-center-ornament.png")]
        public static partial ReadOnlySpan<byte> GetFrameTopOrn();
        [FileEmbed.FileEmbed("..\\..\\Assets\\gothic-login-ui\\11-bottom-center-ornament.png")]
        public static partial ReadOnlySpan<byte> GetFrameBottomOrn();

        [FileEmbed.FileEmbed("..\\..\\Assets\\fonts\\Cinzel\\static\\Cinzel-SemiBold.ttf")]
        public static partial ReadOnlySpan<byte> GetCinzelSemiBold();
        [FileEmbed.FileEmbed("..\\..\\Assets\\fonts\\Cormorant_Garamond\\static\\CormorantGaramond-SemiBold.ttf")]
        public static partial ReadOnlySpan<byte> GetCormorantSemiBold();
#endif
    }
}
