// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Controls
{
    // A single grid cell: centered item art, full-cell hitbox, stack-count badge,
    // and the same interactions ItemGump exposes inside a standard container.
    internal sealed class GridContainerItem : Control
    {
        private readonly GridContainerView _view;
        private readonly HitBox _hit;
        private readonly Label _count;

        public GridContainerItem(GridContainerView view, uint serial, int size)
        {
            _view = view;
            LocalSerial = serial;

            Item item = _view.World.Items.Get(serial);

            if (item == null)
            {
                Dispose();

                return;
            }

            CanMove = false;
            AcceptMouseInput = true;
            WantUpdateSize = false;

            Width = size;
            Height = size;

            AlphaBlendControl background = new AlphaBlendControl { Width = size, Height = size };
            Add(background);

            _hit = new HitBox(0, 0, size, size, null, 0f);
            Add(_hit);

            if (_view.World.ClientFeatures.TooltipsEnabled)
            {
                _hit.SetTooltip(item);
            }

            _count = new Label(
                item.Amount > 1 && item.ItemData.IsStackable ? item.Amount.ToString() : string.Empty,
                true,
                0x0481,
                align: TEXT_ALIGN_TYPE.TS_LEFT
            )
            {
                X = 1,
                Y = size - 14
            };

            Add(_count);
        }

        public override void Update()
        {
            if (IsDisposed)
            {
                return;
            }

            base.Update();

            if (!_view.World.InGame)
            {
                return;
            }

            // Mirrors ItemGump.Update: begin a pickup once the drag threshold or the
            // double-click window is passed while the left button is held over this cell.
            if (
                !Client.Game.UO.GameCursor.ItemHold.Enabled
                && Mouse.LButtonPressed
                && UIManager.LastControlMouseDown(MouseButtonType.Left) == this
                && (
                    Mouse.LastLeftButtonClickTime != 0xFFFF_FFFF
                        && Mouse.LastLeftButtonClickTime != 0
                        && Mouse.LastLeftButtonClickTime + Mouse.MOUSE_DELAY_DOUBLE_CLICK < Time.Ticks
                    || CanPickup()
                )
            )
            {
                AttemptPickUp();
            }
            else if (_hit.MouseIsOver)
            {
                SelectedObject.Object = _view.World.Get(LocalSerial);
            }
        }

        protected override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            if (button != MouseButtonType.Left)
            {
                base.OnMouseUp(x, y, button);
                return;
            }

            // Held item released over a cell -> drop into this container (grid has no
            // meaningful slot coordinates; 0xFFFF/0xFFFF lets the server place it).
            if (
                Client.Game.UO.GameCursor.ItemHold.Enabled
                && !Client.Game.UO.GameCursor.ItemHold.IsFixedPosition
            )
            {
                GameActions.DropItem(
                    Client.Game.UO.GameCursor.ItemHold.Serial,
                    0xFFFF,
                    0xFFFF,
                    0,
                    _view.ContainerSerial
                );

                Mouse.CancelDoubleClick = true;
                return;
            }

            // Single click: route through the delayed-click manager, same as ItemGump.
            SelectedObject.Object = _view.World.Get(LocalSerial);
            base.OnMouseUp(x, y, button);
        }

        protected override void OnMouseOver(int x, int y)
        {
            SelectedObject.Object = _view.World.Get(LocalSerial);
        }

        protected override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
        {
            // Mirrors ItemGump.OnMouseDoubleClick.
            if (button != MouseButtonType.Left || _view.World.TargetManager.IsTargeting)
            {
                return false;
            }

            Item item = _view.World.Items.Get(LocalSerial);
            Item container;

            if (
                !Keyboard.Ctrl
                && ProfileManager.CurrentProfile.DoubleClickToLootInsideContainers
                && item != null
                && !item.IsDestroyed
                && !item.ItemData.IsContainer
                && item.IsEmpty
                && (container = _view.World.Items.Get(item.RootContainer)) != null
                && container != _view.World.Player.FindItemByLayer(Layer.Backpack)
            )
            {
                GameActions.GrabItem(_view.World, LocalSerial, item.Amount);
            }
            else
            {
                GameActions.DoubleClick(_view.World, LocalSerial);
            }

            return true;
        }

        // Mirrors ItemGump.CanPickup (drag threshold + split-menu handoff).
        private bool CanPickup()
        {
            Point offset = Mouse.LDragOffset;

            if (
                Math.Abs(offset.X) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
                && Math.Abs(offset.Y) < Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
            )
            {
                return false;
            }

            SplitMenuGump split = UIManager.GetGump<SplitMenuGump>(LocalSerial);

            if (split == null)
            {
                return true;
            }

            split.X = Mouse.LClickPosition.X - 80;
            split.Y = Mouse.LClickPosition.Y - 40;
            UIManager.AttemptDragControl(split, true);
            split.BringOnTop();

            return false;
        }

        // Mirrors ItemGump.AttemptPickUp (honors RelativeDragAndDropItems /
        // ScaleItemsInsideContainers offsets). is_gump is always false for container art.
        private void AttemptPickUp()
        {
            Item item = _view.World.Items.Get(LocalSerial);

            if (item == null)
            {
                return;
            }

            ref readonly var spriteInfo = ref Client.Game.UO.Arts.GetArt(item.DisplayedGraphic);

            int centerX = spriteInfo.UV.Width >> 1;
            int centerY = spriteInfo.UV.Height >> 1;

            if (
                ProfileManager.CurrentProfile != null
                && ProfileManager.CurrentProfile.ScaleItemsInsideContainers
            )
            {
                float scale = UIManager.ContainerScale;
                centerX = (int)(centerX * scale);
                centerY = (int)(centerY * scale);
            }

            if (
                ProfileManager.CurrentProfile != null
                && ProfileManager.CurrentProfile.RelativeDragAndDropItems
            )
            {
                Point p = new Point(
                    centerX - (Mouse.Position.X - ScreenCoordinateX),
                    centerY - (Mouse.Position.Y - ScreenCoordinateY)
                );

                GameActions.PickUp(_view.World, LocalSerial, centerX, centerY, offset: p);
            }
            else
            {
                GameActions.PickUp(_view.World, LocalSerial, centerX, centerY);
            }
        }

        public override bool AddToRenderLists(RenderLists renderLists, int x, int y, ref float layerDepthRef)
        {
            base.AddToRenderLists(renderLists, x, y, ref layerDepthRef);
            float layerDepth = layerDepthRef;

            Item item = _view.World.Items.Get(LocalSerial);

            if (item == null)
            {
                return true;
            }

            ref readonly var artInfo = ref Client.Game.UO.Arts.GetArt(item.DisplayedGraphic);
            var rect = Client.Game.UO.Arts.GetRealArtBounds(item.DisplayedGraphic);

            Vector3 hueVector = ShaderHueTranslator.GetHueVector(
                item.Hue,
                item.ItemData.IsPartialHue,
                1f
            );

            // Center the art within the cell (mirrors GridLootItem centering).
            Point size = new Point(_hit.Width, _hit.Height);
            Point point = new Point();

            if (rect.Width < _hit.Width)
            {
                size.X = rect.Width;
                point.X = (_hit.Width >> 1) - (size.X >> 1);
            }

            if (rect.Height < _hit.Height)
            {
                size.Y = rect.Height;
                point.Y = (_hit.Height >> 1) - (size.Y >> 1);
            }

            var texture = artInfo.Texture;
            var sourceRectangle = artInfo.UV;

            if (texture != null)
            {
                renderLists.AddGumpWithAtlas(
                    batcher =>
                    {
                        batcher.Draw(
                            texture,
                            new Rectangle(x + point.X, y + point.Y, size.X, size.Y),
                            new Rectangle(
                                sourceRectangle.X + rect.X,
                                sourceRectangle.Y + rect.Y,
                                rect.Width,
                                rect.Height
                            ),
                            hueVector,
                            layerDepth
                        );
                        return true;
                    }
                );
            }

            if (_hit.MouseIsOver)
            {
                Vector3 hoverHue = ShaderHueTranslator.GetHueVector(0);
                hoverHue.Z = 0.2f;

                renderLists.AddGumpNoAtlas(
                    batcher =>
                    {
                        batcher.Draw(
                            SolidColorTextureCache.GetTexture(Color.Yellow),
                            new Rectangle(x + 1, y + 1, Width - 1, Height - 1),
                            hoverHue,
                            layerDepth
                        );
                        return true;
                    }
                );
            }

            return true;
        }
    }
}
