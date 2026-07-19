// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps.Login
{
    // The between-scene loading / message screen, styled like the realm and
    // character select screens: gate background + tall gothic frame, the same
    // message that the default LoadingGump would show (connecting, verifying,
    // entering, or an error popup), rendered in the new font. An optional themed
    // OK/Cancel button maps to the same callback the default gump used.
    internal class CustomLoadingGump : Gump
    {
        private const int W = 1280, H = 720;
        private const int PanelW = 500, PanelH = 668;
        private const int PanelX = (W - PanelW) / 2;
        private const int PanelY = 24;
        private const int PanelCX = PanelX + PanelW / 2;
        private const int BtnW = 185, BtnH = 48;
        private const float MsgSize = 24f;

        private readonly ILoginFont _msgFont;
        private readonly TtfLabel _msgLabel;

        public CustomLoadingGump(World world, string message, LoginButtons showButtons, Action<int> onButton)
            : base(world, 0, 0)
        {
            CanCloseWithRightClick = false;
            AcceptKeyboardInput = false;

            var cormorant = LoginAssets.Cormorant;
            var cinzel = LoginAssets.Cinzel;
            var textColor = new Color(0xD3, 0xC2, 0xA1);

            Add(new TextureImage(LoginAssets.CharSelectBackground, 0, 0, W, H));
            Add(new NineSliceFrame(PanelX, PanelY, PanelW, PanelH, scale: 0.33f, withOrnaments: true,
                cornerOffsetX: 17, cornerOffsetY: 29,
                dTL: new Point(4, -3), dTR: new Point(-1, -4), dBL: new Point(4, 4), dBR: new Point(0, 4),
                ornTopDeltaY: 3, ornBotDeltaY: -1));

            message ??= string.Empty;
            _msgFont = cormorant;
            var msgW = cormorant.Measure(message, MsgSize).X;
            _msgLabel = new TtfLabel(message, cormorant, MsgSize, textColor, 0.95f,
                PanelCX - (int)(msgW / 2), PanelY + (int)(PanelH * 0.42f));
            Add(_msgLabel);

            // Optional themed OK / Cancel button, centered near the bottom.
            string btnLabel = showButtons == LoginButtons.OK ? "OK"
                            : showButtons == LoginButtons.Cancel ? "Cancel"
                            : null;

            if (btnLabel != null)
            {
                int buttonId = (int)(showButtons == LoginButtons.OK ? LoginButtons.OK : LoginButtons.Cancel);
                Add(new ImageButton(ButtonStyle.Dark, btnLabel, cinzel, 20f,
                    PanelCX - BtnW / 2, PanelY + PanelH - BtnH - 60, BtnW, BtnH)
                {
                    Clicked = () => onButton?.Invoke(buttonId)
                });
            }
        }

        public void SetMessage(string message)
        {
            message ??= string.Empty;
            _msgLabel.Text = message;
            var msgW = _msgFont.Measure(message, MsgSize).X;
            _msgLabel.X = PanelCX - (int)(msgW / 2);
        }
    }
}
#endif
