// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System.Collections.Generic;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer.LoginFonts;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps.Login
{
    // Realm/server selection over the gate background: a tall gothic frame with a
    // vertical list of RealmButtons, a red Select (next) and a dark Back button.
    internal class CustomServerSelectionGump : Gump
    {
        private const int W = 1280, H = 720;

        // Frame stretched almost to the top edge.
        private const int PanelW = 500, PanelH = 668;
        private const int PanelX = (W - PanelW) / 2;
        private const int PanelY = 24;
        private const int PanelCX = PanelX + PanelW / 2;

        private const int RowW = 400, RowH = 60, RowGap = 12;
        private const int BtnW = 185, BtnH = 48;

        private readonly List<RealmButton> _rows = new List<RealmButton>();
        private int _selectedIndex;
        private readonly LoginScene _scene;
        private ServerListEntry[] _servers;

        public CustomServerSelectionGump(World world, LoginScene scene) : base(world, 0, 0)
        {
            _scene = scene;
            CanCloseWithRightClick = false;
            AcceptKeyboardInput = true;

            var cormorant = LoginAssets.Cormorant;
            var cinzel = LoginAssets.Cinzel;
            var labelColor = new Color(0xD3, 0xC2, 0xA1);

            Add(new TextureImage(LoginAssets.CharSelectBackground, 0, 0, W, H));
            Add(new NineSliceFrame(PanelX, PanelY, PanelW, PanelH, scale: 0.33f, withOrnaments: true,
                cornerOffsetX: 17, cornerOffsetY: 29,
                dTL: new Point(4, -3), dTR: new Point(-1, -4), dBL: new Point(4, 4), dBR: new Point(0, 4),
                ornTopDeltaY: 3, ornBotDeltaY: -1));

            // Title, centered.
            const string title = "SELECT REALM";
            var titleSize = cinzel.Measure(title, 26f, 3f);
            Add(new TtfLabel(title, cinzel, 26f, labelColor, 1f, PanelCX - (int)(titleSize.X / 2), PanelY + 46, 3f));

            var servers = scene.Servers ?? System.Array.Empty<ServerListEntry>();
            _servers = servers;
            // Default focus on the realm remembered in settings (fall back to first).
            int defaultIdx = scene.GetServerIndexFromSettings();
            if (defaultIdx < 0 || defaultIdx >= servers.Length) defaultIdx = 0;
            _selectedIndex = servers.Length > 0 ? defaultIdx : -1;

            int rowX = PanelCX - RowW / 2;
            int rowY = PanelY + 110;
            for (int i = 0; i < servers.Length; i++)
            {
                var row = new RealmButton(servers[i], cormorant, 18f, rowX, rowY, RowW, RowH)
                {
                    Selected = i == _selectedIndex
                };
                int captured = i;
                row.Clicked = btn =>
                {
                    foreach (var r in _rows) r.Selected = false;
                    btn.Selected = true;
                    _selectedIndex = captured;
                };
                row.Activated = btn =>
                {
                    _selectedIndex = captured;
                    scene.SelectServer((byte)servers[captured].Index);
                };
                Add(row);
                _rows.Add(row);
                rowY += RowH + RowGap;
            }

            // Bottom buttons: Back (dark) then Select (red), bottom-right.
            const int sideMargin = 40;
            int btnY = PanelY + PanelH - BtnH - 34;
            int selX = PanelX + PanelW - BtnW - sideMargin;   // right gap = sideMargin
            int backX = PanelX + sideMargin;                  // left gap = sideMargin (symmetric)

            Add(new ImageButton(ButtonStyle.Dark, "Back", cinzel, 20f, backX, btnY, BtnW, BtnH)
            {
                Clicked = () => scene.StepBack()
            });

            Add(new ImageButton(ButtonStyle.Priority, "Select", cinzel, 20f, selX, btnY, BtnW, BtnH)
            {
                Clicked = Confirm
            });
        }

        private void Confirm()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _servers.Length)
            {
                _scene.SelectServer((byte)_servers[_selectedIndex].Index);
            }
        }

        public override void OnKeyboardReturn(int textID, string text) => Confirm();
    }
}
#endif
