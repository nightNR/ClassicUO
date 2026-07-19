// SPDX-License-Identifier: BSD-2-Clause

#if CUSTOM_LOGIN_SCENE
using ClassicUO.Configuration;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Login;
using ClassicUO.Renderer.LoginFonts;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps.Login
{
    internal class CustomLoginGump : Gump
    {
        private const int W = 1280, H = 720;

        // Frame sits BELOW the fresco's Dark Paradise logo (logo occupies the top
        // ~40% of the background), sized/placed to match the reference mockup.
        private const int FrameScale100 = 33;                 // NineSliceFrame scale = 0.33 (~10% larger, less blocky corners)
        private const int PanelW = 440;
        private const int PanelH = 374;
        private const int PanelX = (W - PanelW) / 2;
        private const int PanelY = 320;

        private const int PanelCX = PanelX + PanelW / 2;

        // Control sizes: widths narrowed per request, heights follow the visible
        // (cropped) art aspect so nothing is stretched.
        private const int InputW = 288, InputH = 52;    // input bar ~5.5:1
        private const int BtnW = 267, BtnH = 68;        // button band ~3.9:1

        public CustomLoginGump(World world, LoginScene scene) : base(world, 0, 0)
        {
            CanCloseWithRightClick = false;
            AcceptKeyboardInput = false;

            var cormorant = LoginAssets.Cormorant;
            var cinzel = LoginAssets.Cinzel;
            var labelColor = new Color(0xD3, 0xC2, 0xA1);

            // Fullscreen fresco background, then the gothic frame panel on top.
            Add(new TextureImage(LoginAssets.Background, 0, 0, W, H));
            Add(new NineSliceFrame(PanelX, PanelY, PanelW, PanelH, scale: FrameScale100 / 100f, withOrnaments: true,
                cornerOffsetX: 17, cornerOffsetY: 29,
                dTL: new Point(4, -3), dTR: new Point(-1, -4), dBL: new Point(4, 4), dBR: new Point(0, 4),
                ornTopDeltaY: 3, ornBotDeltaY: -1));

            bool reconnect = LoginReconnectPolicy.UseReconnectGump(
                Settings.GlobalSettings.AutoLogin,
                Settings.GlobalSettings.Username,
                Settings.GlobalSettings.Password,
                scene.ForceFullLogin);

            if (reconnect)
            {
                BuildReconnectState(scene, cormorant, cinzel, labelColor);
            }
            else
            {
                BuildFullFormState(scene, cormorant, cinzel, labelColor);
            }
        }

        // Position a control by the center of its visible bar.
        private static int CenterX(int w) => PanelCX - w / 2;
        private static int TopFromCenter(int centerY, int h) => centerY - h / 2;

        private void BuildFullFormState(LoginScene scene, ILoginFont cormorant, ILoginFont cinzel,
            Color labelColor)
        {
            const int accCenter = 390, pwCenter = 458, loginCenter = 542, quitCenter = 624;

            var account = new TtfTextBox(cormorant, 18f, CenterX(InputW), TopFromCenter(accCenter, InputH), InputW, InputH, isPassword: false)
            {
                Placeholder = "Account Name"
            };
            account.SetText(Settings.GlobalSettings.Username);
            Add(account);

            var password = new TtfTextBox(cormorant, 18f, CenterX(InputW), TopFromCenter(pwCenter, InputH), InputW, InputH, isPassword: true)
            {
                Placeholder = "Password"
            };
            password.SetText(Crypter.Decrypt(Settings.GlobalSettings.Password));
            Add(password);

            if (!string.IsNullOrEmpty(scene.PopupMessage))
            {
                Add(new TtfLabel(scene.PopupMessage, cormorant, 14f, new Color(0xC0, 0x30, 0x30), 1f,
                    CenterX(InputW) + 4, (pwCenter + loginCenter) / 2 - 8));
            }

            Add(new ImageButton(ButtonStyle.Priority, "Login", cinzel, 22f, CenterX(BtnW), TopFromCenter(loginCenter, BtnH), BtnW, BtnH)
            {
                Clicked = () => scene.Connect(account.Text, password.Text)
            });

            Add(new ImageButton(ButtonStyle.Dark, "Quit", cinzel, 22f, CenterX(BtnW), TopFromCenter(quitCenter, BtnH), BtnW, BtnH)
            {
                Clicked = () => Client.Game.Exit()
            });

            account.EnterPressed += _ => scene.Connect(account.Text, password.Text);
            password.EnterPressed += _ => scene.Connect(account.Text, password.Text);
        }

        private void BuildReconnectState(LoginScene scene, ILoginFont cormorant, ILoginFont cinzel,
            Color labelColor)
        {
            const int statusY = 410, reconnectCenter = 490, quitCenter = 578;

            string status = string.IsNullOrEmpty(scene.PopupMessage)
                ? $"Reconnecting as {Settings.GlobalSettings.Username}"
                : scene.PopupMessage;
            Add(new TtfLabel(status, cormorant, 17f, labelColor, 0.9f, CenterX(InputW) + 4, statusY));

            Add(new ImageButton(ButtonStyle.Priority, "Reconnect", cinzel, 22f, CenterX(BtnW), TopFromCenter(reconnectCenter, BtnH), BtnW, BtnH)
            {
                Clicked = () => scene.Connect(
                    Settings.GlobalSettings.Username,
                    Crypter.Decrypt(Settings.GlobalSettings.Password))
            });

            Add(new ImageButton(ButtonStyle.Dark, "Quit", cinzel, 22f, CenterX(BtnW), TopFromCenter(quitCenter, BtnH), BtnW, BtnH)
            {
                Clicked = () => Client.Game.Exit()
            });
        }
    }
}
#endif
