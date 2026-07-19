// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System.IO;
using ClassicUO.Renderer.LoginFonts;
using ClassicUO.Resources;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Login
{
    internal static class LoginAssets
    {
        private static GraphicsDevice Device => Client.Game.GraphicsDevice;

        private static Texture2D _background, _charSelectBg, _btnNeutral, _btnHover, _btnDown,
            _btnPrioNeutral, _btnPrioHover, _btnPrioDown,
            _inputNeutral, _inputFocus, _inputDisabled,
            _cornerTL, _cornerTR, _cornerBL, _cornerBR,
            _sideL, _sideR, _center,
            _topOrn, _bottomOrn;

        private static ILoginFont _cinzel, _cormorant;

        private static Texture2D Load(ref Texture2D slot, System.ReadOnlySpan<byte> bytes)
        {
            if (slot == null)
            {
                using var ms = new MemoryStream(bytes.ToArray());
                using var raw = Texture2D.FromStream(Device, ms);
                slot = BuildMipmapped(raw);
            }
            return slot;
        }

        // These PNGs are far larger than their on-screen size, so the frame is
        // heavily minified at draw time. Plain bilinear minification without
        // mipmaps drops thin highlights (the gold edge lines) and adds grain.
        // Build a full mip chain (box-filtered) once so AnisotropicClamp sampling
        // downscales smoothly and preserves those lines.
        private static Texture2D BuildMipmapped(Texture2D raw)
        {
            int w = raw.Width, h = raw.Height;
            var level = new Microsoft.Xna.Framework.Color[w * h];
            raw.GetData(level);

            var tex = new Texture2D(Device, w, h, mipMap: true, SurfaceFormat.Color);
            tex.SetData(0, null, level, 0, level.Length);

            for (int lvl = 1; lvl < tex.LevelCount; lvl++)
            {
                int nw = System.Math.Max(1, w / 2);
                int nh = System.Math.Max(1, h / 2);
                var next = new Microsoft.Xna.Framework.Color[nw * nh];

                for (int y = 0; y < nh; y++)
                {
                    int sy0 = y * 2, sy1 = System.Math.Min(sy0 + 1, h - 1);
                    for (int x = 0; x < nw; x++)
                    {
                        int sx0 = x * 2, sx1 = System.Math.Min(sx0 + 1, w - 1);
                        var a = level[sy0 * w + sx0];
                        var b = level[sy0 * w + sx1];
                        var c = level[sy1 * w + sx0];
                        var d = level[sy1 * w + sx1];
                        next[y * nw + x] = new Microsoft.Xna.Framework.Color(
                            (a.R + b.R + c.R + d.R) / 4,
                            (a.G + b.G + c.G + d.G) / 4,
                            (a.B + b.B + c.B + d.B) / 4,
                            (a.A + b.A + c.A + d.A) / 4);
                    }
                }

                tex.SetData(lvl, null, next, 0, next.Length);
                level = next;
                w = nw;
                h = nh;
            }

            return tex;
        }

        public static Texture2D Background => Load(ref _background, Loader.GetLoginBackground());
        public static Texture2D CharSelectBackground => Load(ref _charSelectBg, Loader.GetCharSelectBackground());
        public static Texture2D ButtonNeutral => Load(ref _btnNeutral, Loader.GetButtonNeutral());
        public static Texture2D ButtonHover => Load(ref _btnHover, Loader.GetButtonHover());
        public static Texture2D ButtonDown => Load(ref _btnDown, Loader.GetButtonDown());
        public static Texture2D ButtonPrioNeutral => Load(ref _btnPrioNeutral, Loader.GetButtonPrioNeutral());
        public static Texture2D ButtonPrioHover => Load(ref _btnPrioHover, Loader.GetButtonPrioHover());
        public static Texture2D ButtonPrioDown => Load(ref _btnPrioDown, Loader.GetButtonPrioMDown());
        public static Texture2D InputNeutral => Load(ref _inputNeutral, Loader.GetTextInputNeutral());
        public static Texture2D InputFocus => Load(ref _inputFocus, Loader.GetTextInputFocus());
        public static Texture2D InputDisabled => Load(ref _inputDisabled, Loader.GetTextInputDisabled());
        public static Texture2D CornerTL => Load(ref _cornerTL, Loader.GetFrameCornerTL());
        public static Texture2D CornerTR => Load(ref _cornerTR, Loader.GetFrameCornerTR());
        public static Texture2D CornerBL => Load(ref _cornerBL, Loader.GetFrameCornerBL());
        public static Texture2D CornerBR => Load(ref _cornerBR, Loader.GetFrameCornerBR());
        public static Texture2D SideL => Load(ref _sideL, Loader.GetFrameSideL());
        public static Texture2D SideR => Load(ref _sideR, Loader.GetFrameSideR());
        public static Texture2D Center => Load(ref _center, Loader.GetFrameCenter());
        public static Texture2D TopOrn => Load(ref _topOrn, Loader.GetFrameTopOrn());
        public static Texture2D BottomOrn => Load(ref _bottomOrn, Loader.GetFrameBottomOrn());

        public static ILoginFont Cinzel =>
            _cinzel ??= new FontStashLoginFont(Device, Loader.GetCinzelSemiBold().ToArray());
        public static ILoginFont Cormorant =>
            _cormorant ??= new FontStashLoginFont(Device, Loader.GetCormorantSemiBold().ToArray());

        public static void Reset()
        {
            (_cinzel as System.IDisposable)?.Dispose();
            (_cormorant as System.IDisposable)?.Dispose();
            _cinzel = null;
            _cormorant = null;

            _background?.Dispose();
            _charSelectBg?.Dispose();
            _btnNeutral?.Dispose();
            _btnHover?.Dispose();
            _btnDown?.Dispose();
            _btnPrioNeutral?.Dispose();
            _btnPrioHover?.Dispose();
            _btnPrioDown?.Dispose();
            _inputNeutral?.Dispose();
            _inputFocus?.Dispose();
            _inputDisabled?.Dispose();
            _cornerTL?.Dispose();
            _cornerTR?.Dispose();
            _cornerBL?.Dispose();
            _cornerBR?.Dispose();
            _sideL?.Dispose();
            _sideR?.Dispose();
            _center?.Dispose();
            _topOrn?.Dispose();
            _bottomOrn?.Dispose();

            _background = _charSelectBg = _btnNeutral = _btnHover = _btnDown =
                _btnPrioNeutral = _btnPrioHover = _btnPrioDown =
                _inputNeutral = _inputFocus = _inputDisabled =
                _cornerTL = _cornerTR = _cornerBL = _cornerBR =
                _sideL = _sideR = _center =
                _topOrn = _bottomOrn = null;
        }
    }
}
#endif
