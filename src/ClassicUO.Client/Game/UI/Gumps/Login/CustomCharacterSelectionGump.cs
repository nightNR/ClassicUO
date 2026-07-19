// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using System.Collections.Generic;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Login;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps.Login
{
    // Character selection: same gate background + tall frame as realm select, but
    // the character rows live in a scrollable CharacterList. Red Select enters the
    // world with the chosen character; dark Back returns to realm selection.
    internal class CustomCharacterSelectionGump : Gump
    {
        private const int W = 1280, H = 720;

        private const int PanelW = 500, PanelH = 668;
        private const int PanelX = (W - PanelW) / 2;
        private const int PanelY = 24;
        private const int PanelCX = PanelX + PanelW / 2;

        private const int ListW = 400, RowH = 60, RowGap = 12;
        private const int BtnW = 185, BtnH = 48;

        private readonly CharacterList _list;

        public CustomCharacterSelectionGump(World world, LoginScene scene) : base(world, 0, 0)
        {
            CanCloseWithRightClick = false;
            AcceptKeyboardInput = false;

            var cormorant = LoginAssets.Cormorant;
            var cinzel = LoginAssets.Cinzel;
            var labelColor = new Color(0xD3, 0xC2, 0xA1);

            Add(new TextureImage(LoginAssets.CharSelectBackground, 0, 0, W, H));
            Add(new NineSliceFrame(PanelX, PanelY, PanelW, PanelH, scale: 0.33f, withOrnaments: true,
                cornerOffsetX: 17, cornerOffsetY: 29,
                dTL: new Point(4, -3), dTR: new Point(-1, -4), dBL: new Point(4, 4), dBR: new Point(0, 4),
                ornTopDeltaY: 3, ornBotDeltaY: -1));

            const string title = "SELECT CHARACTER";
            var titleSize = cinzel.Measure(title, 26f, 3f);
            Add(new TtfLabel(title, cinzel, 26f, labelColor, 1f, PanelCX - (int)(titleSize.X / 2), PanelY + 46, 3f));

            // Build items from non-empty character slots.
            var items = new List<CharacterList.Item>();
            string[] chars = scene.Characters ?? System.Array.Empty<string>();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!string.IsNullOrEmpty(chars[i]))
                {
                    items.Add(new CharacterList.Item(chars[i], i));
                }
            }

            // Scrollable list viewport, sized to fit above the bottom buttons.
            int listX = PanelCX - ListW / 2;
            int listY = PanelY + 110;
            int listH = PanelH - 110 - 34 - BtnH - 30;
            _list = new CharacterList(items, cormorant, 18f, listX, listY, ListW, listH, RowH, RowGap)
            {
                Activated = slot => scene.SelectCharacter((uint)slot)
            };
            Add(_list);

            const int sideMargin = 40;
            int btnY = PanelY + PanelH - BtnH - 34;
            int selX = PanelX + PanelW - BtnW - sideMargin;
            int backX = PanelX + sideMargin;

            Add(new ImageButton(ButtonStyle.Dark, "Back", cinzel, 20f, backX, btnY, BtnW, BtnH)
            {
                Clicked = () => scene.StepBack()
            });

            Add(new ImageButton(ButtonStyle.Priority, "Select", cinzel, 20f, selX, btnY, BtnW, BtnH)
            {
                Clicked = () =>
                {
                    int slot = _list.SelectedSlot;
                    if (slot >= 0)
                    {
                        scene.SelectCharacter((uint)slot);
                    }
                }
            });
        }
    }
}
#endif
